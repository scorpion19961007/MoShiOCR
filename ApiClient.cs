using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MoShiOCR;

public sealed class ApiClient
{
    private const string OauthUrl = "https://aip.baidubce.com/oauth/2.0/token";
    private const string OcrBaseUrl = "https://aip.baidubce.com/rest/2.0/ocr/v1/";
    private const string TranslateUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private string? _cachedToken;
    private DateTime _tokenExpiresAt;

    public async Task<string> RecognizeAsync(byte[] imageBytes, AppSettings settings, string apiKey, string secretKey, CancellationToken token)
    {
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

    public async Task TestOcrAsync(string apiKey, string secretKey, CancellationToken token) =>
        _ = await GetAccessTokenAsync(apiKey.Trim(), secretKey.Trim(), token, true);

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
