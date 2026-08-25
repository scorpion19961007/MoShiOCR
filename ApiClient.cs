using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TencentCloud.Common;
using TencentCloud.Ocr.V20181119;
using TencentCloud.Ocr.V20181119.Models;

namespace MoShiOCR;

public sealed class ApiClient
{
    private const string OauthUrl = "https://aip.baidubce.com/oauth/2.0/token";
    private const string OcrBaseUrl = "https://aip.baidubce.com/rest/2.0/ocr/v1/";
    private const string TranslateUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private string? _cachedToken;
    private DateTime _tokenExpiresAt;

    public async Task<string> RecognizeAsync(byte[] imageBytes, AppSettings settings, string apiKey, string secretKey, string tencentSecretId, string tencentSecretKey, CancellationToken token)
    {
        if (settings.OcrProvider == "tencent")
            return await RecognizeTencentAsync(imageBytes, settings, tencentSecretId, tencentSecretKey, token);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("请先在设置中填写百度 OCR 的 API Key 和 Secret Key。");

        var accessToken = await GetAccessTokenAsync(apiKey.Trim(), secretKey.Trim(), token);
        var mode = settings.OcrMode == "accurate_basic" ? "accurate_basic" : "general_basic";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["image"] = Convert.ToBase64String(imageBytes),
            ["language_type"] = "CHN_ENG",
            ["detect_direction"] = "true",
            ["paragraph"] = "false",
            ["probability"] = "false"
        });
        using var response = await _http.PostAsync($"{OcrBaseUrl}{mode}?access_token={Uri.EscapeDataString(accessToken)}", content, token);
        var body = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(body);
        ThrowBaiduError(doc.RootElement, "OCR");

        if (!doc.RootElement.TryGetProperty("words_result", out var words)) return "";
        return string.Join(Environment.NewLine, words.EnumerateArray()
            .Where(item => item.TryGetProperty("words", out _))
            .Select(item => item.GetProperty("words").GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    public async Task<string> RecognizeTableAsync(byte[] imageBytes, AppSettings settings, string apiKey, string secretKey, string tencentSecretId, string tencentSecretKey, CancellationToken token)
    {
        if (settings.OcrProvider == "tencent")
            return await RecognizeTencentTableAsync(imageBytes, settings, tencentSecretId, tencentSecretKey, token);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("请先在设置中填写百度 OCR 的 API Key 和 Secret Key。");

        var accessToken = await GetAccessTokenAsync(apiKey.Trim(), secretKey.Trim(), token);
        return settings.TableOcrMode == "table_async"
            ? await RecognizeTableAsyncMode(imageBytes, accessToken, token)
            : await PostTableRequestAsync("table", imageBytes, accessToken, token);
    }

    private async Task<string> RecognizeTableAsyncMode(byte[] imageBytes, string accessToken, CancellationToken token)
    {
        using var response = await PostTableFormAsync("table_async", imageBytes, accessToken, token);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        ThrowBaiduError(doc.RootElement, "表格 OCR");
        if (!doc.RootElement.TryGetProperty("request_id", out var requestIdElement))
            return ExtractTableText(doc.RootElement);

        var requestId = requestIdElement.GetString();
        if (string.IsNullOrWhiteSpace(requestId)) return ExtractTableText(doc.RootElement);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            using var resultResponse = await PostAsyncForm("table_async_result", new Dictionary<string, string>
            {
                ["request_id"] = requestId
            }, accessToken, token);
            using var resultDoc = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync(token));
            ThrowBaiduError(resultDoc.RootElement, "表格 OCR");
            var text = ExtractTableText(resultDoc.RootElement);
            if (!string.IsNullOrWhiteSpace(text)) return text;
            if (IsFinished(resultDoc.RootElement)) return text;
        }
        throw new TimeoutException("百度表格 OCR 处理超时，请稍后重试。");
    }

    private async Task<string> PostTableRequestAsync(string endpoint, byte[] imageBytes, string accessToken, CancellationToken token)
    {
        using var response = await PostTableFormAsync(endpoint, imageBytes, accessToken, token);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        ThrowBaiduError(doc.RootElement, "表格 OCR");
        return ExtractTableText(doc.RootElement);
    }

    private async Task<HttpResponseMessage> PostTableFormAsync(string endpoint, byte[] imageBytes, string accessToken, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["image"] = Convert.ToBase64String(imageBytes),
            ["language_type"] = "CHN_ENG"
        });
        return await _http.PostAsync($"{OcrBaseUrl}{endpoint}?access_token={Uri.EscapeDataString(accessToken)}", content, token);
    }

    private async Task<HttpResponseMessage> PostAsyncForm(string endpoint, Dictionary<string, string> values, string accessToken, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(values);
        return await _http.PostAsync($"{OcrBaseUrl}{endpoint}?access_token={Uri.EscapeDataString(accessToken)}", content, token);
    }

    private static bool IsFinished(JsonElement root)
    {
        foreach (var property in new[] { "ret_code", "status", "state" })
        {
            if (!root.TryGetProperty(property, out var value)) continue;
            var text = value.ToString();
            if (text.Contains("success", StringComparison.OrdinalIgnoreCase) || text.Contains("finish", StringComparison.OrdinalIgnoreCase) || text == "0") return true;
        }
        return false;
    }

    private static string ExtractTableText(JsonElement root)
    {
        if (root.TryGetProperty("tables_result", out var tables) && tables.ValueKind == JsonValueKind.Array)
        {
            var output = new List<string>();
            foreach (var table in tables.EnumerateArray())
            {
                var rows = ExtractCellRows(table);
                if (rows.Count > 0) output.Add(string.Join(Environment.NewLine, rows.Select(row => string.Join("\t", row))));
                else
                {
                    var words = CollectWords(table).ToList();
                    if (words.Count > 0) output.Add(string.Join("\t", words));
                }
            }
            if (output.Count > 0) return string.Join(Environment.NewLine + Environment.NewLine, output);
        }
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            var nested = ExtractTableText(result);
            if (!string.IsNullOrWhiteSpace(nested)) return nested;
        }
        return string.Join(Environment.NewLine, CollectWords(root));
    }

    private static async Task<string> RecognizeTencentAsync(byte[] imageBytes, AppSettings settings, string secretId, string secretKey, CancellationToken token)
    {
        var client = CreateTencentClient(secretId, secretKey);
        token.ThrowIfCancellationRequested();
        var base64 = Convert.ToBase64String(imageBytes);
        if (settings.OcrMode == "general_accurate")
        {
            var response = await client.GeneralAccurateOCR(new GeneralAccurateOCRRequest { ImageBase64 = base64 });
            token.ThrowIfCancellationRequested();
            return string.Join(Environment.NewLine, (response.TextDetections ?? Array.Empty<TextDetection>())
                .Select(item => item.DetectedText).Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        // Tencent uses "zh" for mixed Chinese/English; "CHN_ENG" is a Baidu-only value.
        var basic = await client.GeneralBasicOCR(new GeneralBasicOCRRequest { ImageBase64 = base64, LanguageType = "zh" });
        token.ThrowIfCancellationRequested();
        return string.Join(Environment.NewLine, (basic.TextDetections ?? Array.Empty<TextDetection>())
            .Select(item => item.DetectedText).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static async Task<string> RecognizeTencentTableAsync(byte[] imageBytes, AppSettings settings, string secretId, string secretKey, CancellationToken token)
    {
        var client = CreateTencentClient(secretId, secretKey);
        token.ThrowIfCancellationRequested();
        var base64 = Convert.ToBase64String(imageBytes);
        if (settings.TableOcrMode == "table_v2")
        {
            var response = await client.RecognizeTableOCR(new RecognizeTableOCRRequest { ImageBase64 = base64, TableLanguage = "zh" });
            token.ThrowIfCancellationRequested();
            return ExtractTencentTableText(response.Data, response.TableDetections);
        }
        var v1 = await client.TableOCR(new TableOCRRequest { ImageBase64 = base64 });
        token.ThrowIfCancellationRequested();
        return ExtractTencentTableText(v1.Data, v1.TextDetections);
    }

    private static OcrClient CreateTencentClient(string secretId, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("请先在设置中填写腾讯云 OCR 的 SecretId 和 SecretKey。");
        return new OcrClient(new Credential { SecretId = secretId.Trim(), SecretKey = secretKey.Trim() }, "ap-guangzhou");
    }

    private static string ExtractTencentTableText(string? data, object? detections)
    {
        if (!string.IsNullOrWhiteSpace(data))
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var text = ExtractTableText(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (JsonException) { }
            if (data.Contains("<table", StringComparison.OrdinalIgnoreCase))
                return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(data, "<[^>]+>", " ")).Trim();
        }
        if (detections is TextTable[] oldCells)
            return FormatTencentCells(oldCells
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Text))
                .Select(cell => (Row: cell.RowTl ?? 0, Col: cell.ColTl ?? 0, Text: cell.Text)));
        if (detections is TableDetectInfo[] tables)
            return string.Join(Environment.NewLine + Environment.NewLine, tables.Select(table => FormatTencentCells((table.Cells ?? Array.Empty<TableCell>())
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Text))
                .Select(cell => (Row: cell.RowTl ?? 0, Col: cell.ColTl ?? 0, Text: cell.Text)))));
        return "";
    }

    private static string FormatTencentCells(IEnumerable<(long Row, long Col, string Text)> cells)
    {
        var rows = cells.GroupBy(cell => cell.Row).OrderBy(group => group.Key).Select(group =>
            string.Join("\t", group.OrderBy(cell => cell.Col).Select(cell => cell.Text)));
        return string.Join(Environment.NewLine, rows);
    }

    private static List<List<string>> ExtractCellRows(JsonElement table)
    {
        var cells = new List<(int Row, int Col, string Text)>();
        FindCells(table, cells);
        if (cells.Count == 0) return [];
        var rows = new List<List<string>>();
        foreach (var group in cells.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
        {
            var row = new List<string>();
            foreach (var cell in group.OrderBy(cell => cell.Col))
            {
                while (row.Count < cell.Col) row.Add("");
                row.Add(cell.Text);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static void FindCells(JsonElement element, List<(int Row, int Col, string Text)> cells)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("row_start", out var row) && element.TryGetProperty("col_start", out var col))
            {
                var text = element.TryGetProperty("words", out var words) ? words.GetString() : element.TryGetProperty("text", out var value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(text)) cells.Add((row.GetInt32(), col.GetInt32(), text));
            }
            foreach (var property in element.EnumerateObject()) FindCells(property.Value, cells);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) FindCells(child, cells);
    }

    private static IEnumerable<string> CollectWords(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(words.GetString())) yield return words.GetString()!;
            else if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())) yield return text.GetString()!;
            foreach (var property in element.EnumerateObject())
                foreach (var value in CollectWords(property.Value)) yield return value;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                foreach (var value in CollectWords(child)) yield return value;
    }

    public async Task<string> TranslateAsync(string text, AppSettings settings, string appId, string secret, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("请先在设置中填写百度翻译的 APP ID 和密钥。");

        var chunks = SplitByUtf8Bytes(text, 5200);
        var translations = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0) await Task.Delay(1100, token);
            translations.Add(await TranslateChunkAsync(chunks[i], settings.TargetLanguage, appId, secret, token));
        }
        return string.Join(Environment.NewLine, translations);
    }

    private async Task<string> TranslateChunkAsync(string text, string targetLanguage, string appId, string secret, CancellationToken token)
    {

        var salt = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var sign = Md5Hex(appId.Trim() + text + salt + secret.Trim());
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = "auto",
            ["to"] = ToBaiduLanguage(targetLanguage),
            ["appid"] = appId.Trim(),
            ["salt"] = salt,
            ["sign"] = sign
        });
        using var response = await _http.PostAsync(TranslateUrl, content, token);
        var body = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(body);
        ThrowBaiduError(doc.RootElement, "翻译");

        if (!doc.RootElement.TryGetProperty("trans_result", out var results)) return "";
        return string.Join(Environment.NewLine, results.EnumerateArray()
            .Where(item => item.TryGetProperty("dst", out _))
            .Select(item => item.GetProperty("dst").GetString()));
    }

    public async Task TestOcrAsync(AppSettings settings, string apiKey, string secretKey, string tencentSecretId, string tencentSecretKey, CancellationToken token)
    {
        if (settings.OcrProvider == "tencent")
        {
            _ = CreateTencentClient(tencentSecretId, tencentSecretKey);
            return;
        }
        _ = await GetAccessTokenAsync(apiKey.Trim(), secretKey.Trim(), token, true);
    }

    public async Task TestTranslateAsync(string appId, string secret, CancellationToken token)
    {
        var settings = new AppSettings { TargetLanguage = "英语" };
        _ = await TranslateAsync("测试", settings, appId, secret, token);
    }

    private async Task<string> GetAccessTokenAsync(string apiKey, string secretKey, CancellationToken token, bool force = false)
    {
        if (!force && _cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt) return _cachedToken;
        var url = $"{OauthUrl}?grant_type=client_credentials&client_id={Uri.EscapeDataString(apiKey)}&client_secret={Uri.EscapeDataString(secretKey)}";
        using var response = await _http.PostAsync(url, null, token);
        var body = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            var detail = doc.RootElement.TryGetProperty("error_description", out var error) ? error.GetString() : "无法获取 access token";
            throw new InvalidOperationException($"百度 OCR 鉴权失败：{detail}");
        }
        _cachedToken = tokenElement.GetString() ?? throw new InvalidOperationException("百度 OCR 未返回 access token。");
        var expires = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 2592000;
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 300));
        return _cachedToken;
    }

    private static void ThrowBaiduError(JsonElement root, string service)
    {
        if (!root.TryGetProperty("error_code", out var code)) return;
        var message = root.TryGetProperty("error_msg", out var msg) ? msg.GetString() : "未知错误";
        throw new InvalidOperationException($"百度{service}请求失败 ({code}): {message}");
    }

    private static string Md5Hex(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static List<string> SplitByUtf8Bytes(string text, int maxBytes)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentBytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            var bytes = Encoding.UTF8.GetByteCount(value);
            if (currentBytes + bytes > maxBytes && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
                currentBytes = 0;
            }
            current.Append(value);
            currentBytes += bytes;
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks.Count == 0 ? [""] : chunks;
    }

    private static string ToBaiduLanguage(string language) => language switch
    {
        "英语" => "en",
        "日语" => "jp",
        "韩语" => "kor",
        "法语" => "fra",
        "德语" => "de",
        "西班牙语" => "spa",
        _ => "zh"
    };
}
