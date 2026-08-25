# 墨识 OCR

一个免费的原生 Windows OCR 与翻译工具，支持自带百度云或腾讯云 API 凭据。

## 功能

- `Ctrl+Shift+A` 全屏框选截图并自动识别
- 截图、识别和翻译快捷键均可自由录入；支持直接使用 `F1` 到 `F24`
- 打开、拖放或粘贴 PNG/JPG/WEBP/BMP 图片
- 识别成功后自动复制结果到剪贴板，也可编辑并翻译为常用语言
- 识别成功后可按 `Esc` 快速隐藏界面，软件继续在后台运行
- 表格识别支持独立全局快捷键，默认 `Ctrl+F10`
- OCR 服务商可在设置中选择百度智能云或腾讯云
- 百度支持标准版、高精度版，以及“表格文字识别 V2”和“表格文字识别-提交请求”两档表格接口
- 腾讯云支持通用文字识别、通用文字识别（高精度版）、表格识别（V1）和表格识别（V2）
- 表格结果按行列整理为制表符分隔文本并自动复制
- 支持百度翻译开放平台通用文本翻译
- 超大图片自动缩放压缩，WEBP 自动转码
- 长文本自动分段翻译并控制请求频率
- API 密钥保存在 Windows 凭据管理器
- 最近 30 条识别/翻译历史保存在本机
- 点击关闭按钮后自动缩小到系统托盘，可从托盘重新打开或彻底退出
- 可选择登录 Windows 时静默启动
- 支持设置页切换夜间模式，主界面和设置页同步使用深色主题

默认快捷键：截图 `Ctrl+Shift+A`，识别 `Ctrl+F8`，翻译 `Ctrl+F9`，表格识别 `Ctrl+F10`。四项快捷键都可在设置页直接按键录入或禁用；`F1` 到 `F24` 可不加修饰键直接使用。

表格识别：打开或截取包含表格的图片后，点击识别文本区域右下角的“表格识别”按钮。先在设置页选择 OCR 服务商，再选择对应的表格接口；百度异步接口会自动轮询结果。

## API 凭据

需要准备两组凭据：

- 百度智能云文字识别应用的 `API Key` 和 `Secret Key`（使用百度时）
- 腾讯云 OCR 的 `SecretId` 和 `SecretKey`（使用腾讯云时）
- 百度翻译开放平台通用文本翻译的 `APP ID` 和密钥

应用会自动完成百度 OCR access token 获取及百度翻译 MD5 签名。

## 构建

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

运行环境：Windows 10/11。GitHub Release 中的自包含版本无需预装 .NET。
