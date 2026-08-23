<img width="477" height="735" alt="PixPin_2026-08-23_16-05-05" src="https://github.com/user-attachments/assets/f5cf109d-616d-44d5-ad64-93afe1967fa8" /># 墨识 OCR

一个免费的原生 Windows OCR 与翻译工具，使用你自己的百度 API。

## 功能
<img width="1752" height="1176" alt="e61677e2441f393f40c248fdfd222e72" src="https://github.com/user-attachments/assets/6a2e82e3-592d-41f1-aa68-56f1422a9e36" />

- `Ctrl+Shift+A` 全屏框选截图并自动识别
- 截图、识别和翻译快捷键均可自由录入；支持直接使用 `F1` 到 `F24`
- 打开、拖放或粘贴 PNG/JPG/WEBP/BMP 图片
- 识别成功后自动复制结果到剪贴板，也可编辑并翻译为常用语言
- 识别成功后可按 `Esc` 快速隐藏界面，软件继续在后台运行
- 支持百度智能云 OCR 标准版与高精度版
- 支持百度翻译开放平台通用文本翻译
- 超大图片自动缩放压缩，WEBP 自动转码
- 长文本自动分段翻译并控制请求频率
- API 密钥保存在 Windows 凭据管理器
- 最近 30 条识别/翻译历史保存在本机
- 点击关闭按钮后自动缩小到系统托盘，可从托盘重新打开或彻底退出
- 可选择登录 Windows 时静默启动
- 支持设置页切换夜间模式，主界面和设置页同步使用深色主题

默认快捷键：截图 `Ctrl+Shift+A`，识别 `Ctrl+F8`，翻译 `Ctrl+F9`。三项快捷键都可在设置页直接按键录入或禁用；`F1` 到 `F24` 可不加修饰键直接使用。

## API 凭据

需要准备两组凭据：

- 百度智能云文字识别应用的 `API Key` 和 `Secret Key`
- 百度翻译开放平台通用文本翻译的 `APP ID` 和密钥

应用会自动完成百度 OCR access token 获取及百度翻译 MD5 签名。
<img width="477" height="735" alt="PixPin_2026-08-23_16-05-05" src="https://github.com/user-attachments/assets/399770f9-5173-4c28-97e2-23783bb29181" />

## 构建

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

运行环境：Windows 10/11。GitHub Release 中的自包含版本无需预装 .NET。
