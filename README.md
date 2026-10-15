# Luma HDR Photo Viewer

**如果你有一块好 HDR 屏幕，别只用它看电影。也用它，重新看看自己的照片。**

![Luma 产品展示：浅色相册与照片预览、悬浮毛玻璃、独立 RTX HDR 与 VSR 控制](docs/images/luma-product-showcase.webp)

Luma 是免费、开源的 Windows 原生照片查看器，支持**原生 HDR 照片**，也能通过 **NVIDIA RTX Video HDR** 为普通 JPG 带来更丰富的光影。

A native Windows photo viewer with RTX HDR for SDR photos and native HDR image support.

[版本发布](https://github.com/lueluelue12138/luma-hdr-photo-viewer/releases) · [HDR 入门](docs/HDR-SETUP.md) · [参与贡献](CONTRIBUTING.md)

**开发中 · 当前版本 `0.1.23` · 尚未发布正式下载版，可自行构建。**

## 为什么做 Luma

我有一块很喜欢的 HDR 显示器。看电影时，夕阳、霓虹和明暗之间的层次，经常让我忍不住暂停，把某一帧当成照片欣赏。

可轮到自己拍的照片，体验就没那么顺手了。Windows 的 HDR 生态还不够完整，我一直没找到一个美观、好用、轻便，又能好好利用 HDR 屏幕的照片查看器。

后来，在浏览器里看到 RTX Video HDR 对普通视频的增强效果，我想：自己拍的照片，是不是也能这样欣赏？Luma 就从这个念头开始了——补上 Windows HDR 体验里“看照片”这一块，让好屏幕也用来展示自己的回忆。

## 看图，也顺手

- **照片是主角。** 原生 WinUI 3、深浅主题、悬浮毛玻璃；支持缩放、拖拽和全屏看图。
- **效果由你选择。** HDR 与 VSR 独立控制，VSR 默认关闭；按住空格，对比原图或可用的 SDR 底图。
- **相册顺着你的习惯。** 文件夹导入、搜索、收藏、最近打开；照片留在原位置，移除相册不会删除原文件。
- **拍摄细节随手查看。** 顶部信息入口展示日期、相机、镜头、曝光与 HDR 来源，支持搜索和复制 EXIF。

## 开始体验 HDR

1. **开启系统 HDR。** 在 Windows“设置 → 系统 → 显示”中选中要用的屏幕，打开“使用 HDR”。
2. **检查屏幕校准。** Luma 设置页可打开微软的 [Windows HDR Calibration](https://apps.microsoft.com/detail/9N7F2SM5D1LR)，未安装时进入商店。按测试图案完成校准并保存配置。
3. **打开自己的照片。** 普通照片在顶部“显示增强”中开启 RTX HDR；已支持的原生 HDR 照片会自动识别，直接显示。

设置页可查看 **Windows 报告的屏幕峰值亮度**，它不是实测值。校准请以图案为准，不照填宣传数字。详细操作见 [HDR 屏幕准备指南](docs/HDR-SETUP.md)。

## 支持范围

| 内容 | 当前支持 |
| --- | --- |
| 普通图片 | JPEG、PNG、BMP、GIF、TIFF、ICO |
| 原生 HDR | iPhone HEIC 的 Apple / ISO 增益图、PQ HEIC；Android Ultra HDR / ISO / Apple 增益图 JPEG；线性 scRGB JPEG XR（JXR / WDP / HDP）。无需 RTX |
| SDR → RTX HDR | 官方 NVIDIA TrueHDR；需要兼容的 RTX 显卡、驱动、含 SDK 的构建与开启 HDR 的屏幕 |
| AI 超分 | 官方 NVIDIA RTX VSR |
| 暂未支持 | AVIF HDR、JPEG XL、OpenEXR、RAW、动画播放；HLG HEIC 尚未完成验证。GIF / 多页 TIFF 当前显示第一帧 |

HEIC 需要 Windows HEIF 图像扩展和 HEVC 视频扩展。具体格式、原图对照方式及验证范围见 [HDR 格式说明](docs/HDR-FORMATS.md)。

RTX HDR 是 AI 增强，不能恢复拍摄时没有记录的信息，也不保证每个区域都更亮。大图片可能依据资源预算缩小处理；首次未缓存的大相册缩略图、AI 初始化与更多显示器的适配仍在完善。

## 构建与发布

<details>
<summary>自行构建 Luma</summary>

需要 Windows 11 x64、[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)、Visual Studio C++ x64 工具、CMake 与 Windows SDK。准确 SDK 版本见 `global.json`。首次构建会下载并校验固定版本的开放源码 HDR/JPEG 解码器及 NASM，后续复用本地缓存。

```powershell
git clone https://github.com/lueluelue12138/luma-hdr-photo-viewer.git
cd luma-hdr-photo-viewer
./scripts/Build.ps1 -Publish -WithoutNvidia
# 运行 artifacts/Luma/Luma.App.exe
```

此构建支持普通看图与已支持的原生 HDR，**不提供 RTX HDR / VSR**。要使用 NVIDIA 增强，请通过自己的开发者账户取得 [RTX Video SDK](https://developer.nvidia.com/rtx-video-sdk)，按 [SDK 放置说明](external/nvidia/README.md) 准备后运行：

```powershell
./scripts/Build.ps1 -Publish -RequireNvidia
```

完整便携文件夹必须一起保留，不能只复制 EXE。

</details>

GitHub Actions 的 **standard** 开发包不含 NVIDIA SDK。**rtx** 标签流程使用受控 SDK 输入，自动构建 EXE 安装包和便携 ZIP，上传至 Release 草稿；正式公开下载由维护者另行决定。详见 [发布流程](docs/RELEASING.md)。

图库与设置保存在 `%LOCALAPPDATA%\Luma`，默认图片关联由你在 Windows 设置中选择。

## 一起完善它

欢迎反馈不同 HDR 屏幕、RTX 显卡与大相册的使用体验，也欢迎提出可复现的问题和改进建议。

[待办清单](docs/TODO.md) · [验证记录](docs/VERIFICATION.md) · [开发说明](docs/DEVELOPMENT.md) · [参与贡献](CONTRIBUTING.md)

## 许可

Luma 的原创代码与图标使用 [MIT](LICENSE)，第三方组件适用各自许可，见 [第三方声明](THIRD-PARTY-NOTICES.md)。NVIDIA SDK 与运行库不适用 MIT，不随公开源码提供。

This software contains source code provided by NVIDIA Corporation. Luma is an independent project and is not affiliated with or endorsed by NVIDIA or Microsoft.
