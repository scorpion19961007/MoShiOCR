# 墨识 OCR

一个原生 Windows OCR 与翻译工具，使用你自己的百度 API。

## 功能

- `Ctrl+Shift+A` 全屏框选截图并自动识别
- 截图、识别和翻译快捷键均可自由录入，保存后立即生效
- 打开、拖放或粘贴 PNG/JPG/WEBP/BMP 图片
- 识别结果可编辑、复制，并可翻译为常用语言
- 支持百度智能云 OCR 标准版与高精度版
- 支持百度翻译开放平台通用文本翻译
- 超大图片自动缩放压缩，WEBP 自动转码
- 长文本自动分段翻译并控制请求频率
- API 密钥保存在 Windows 凭据管理器
- 最近 30 条识别/翻译历史保存在本机
- 可选择登录 Windows 时静默启动，并通过系统托盘打开或退出

默认快捷键：截图 `Ctrl+Shift+A`，识别 `Ctrl+F8`，翻译 `Ctrl+F9`。三项快捷键都可在设置页直接按键录入或禁用。

## API 凭据

需要准备两组凭据：

- 百度智能云文字识别应用的 `API Key` 和 `Secret Key`
- 百度翻译开放平台通用文本翻译的 `APP ID` 和密钥

应用会自动完成百度 OCR access token 获取及百度翻译 MD5 签名。

## 构建

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

运行环境：Windows 10/11。GitHub Release 中的自包含版本无需预装 .NET。
