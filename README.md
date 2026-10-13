<p align="center"><img src="src/Luma.App/Assets/Logo256.png" width="88" alt="Luma icon"></p>

# Luma HDR Photo Viewer

**重新看看你喜欢的照片，让普通照片也有 HDR 的光影。**

Luma 是一个免费、开源的 Windows 原生图库与照片查看器。它把 NVIDIA RTX Video HDR 带到静态照片：打开一张熟悉的 JPG，在兼容的 RTX 显卡与 HDR 屏幕上，重新感受日落、灯光和明暗层次。

**A native Windows photo viewer with NVIDIA RTX Video HDR for SDR photos, and native HDR image rendering.** Rediscover the photos you already love, with a clean interface that puts the picture first.

## 为什么做 Luma

看照片应该简单，也应该好看。我们希望把 Windows 的自然交互、清晰的照片显示和 RTX HDR 放在同一个轻松使用的应用里。

- **普通照片，也能体验 RTX HDR。** 使用官方 RTX Video SDK 的 TrueHDR，将 SDR 照片呈现在真实的 FP16 / scRGB HDR 路径上。
- **HDR 与 VSR 各自选择。** 可以只开 HDR；VSR 默认关闭，是否使用 AI 超分由你决定。按住空格，对比原图。
- **照片是主角。** 原生 WinUI 3、浅色/深色主题、Mica、悬浮毛玻璃面板；完整适配原比例，缩放、拖拽和全屏看图。
- **相册顺着你的习惯。** 添加文件夹、搜索、收藏、最近打开与多相册；照片留在原文件夹，从图库移除相册也不会删除原文件。
- **没有 RTX，也能看图。** 普通 SDR 和已支持的原生 HDR 图片有独立显示路径；增强不可用时会说明原因。

## 当前状态

**正在开发，尚未发布正式下载版。** 当前源码版本为 `0.1.16`，性能和实际使用体验仍在持续完善。首次未缓存的大相册缩略图、首次 AI 初始化，以及更多显示器上的验证还需要继续优化。

GitHub Actions 的 **standard** 开发构建可验证安装与普通看图，**不包含 NVIDIA SDK，也不提供 RTX HDR / VSR**。完整 **rtx** 版本的自动构建与 Release 草稿流程已经预留，需先完成独立 SDK、分发许可与发布检查。两种构建都会明确标识能力，详见 [发布流程](docs/RELEASING.md)。

## 支持什么

| 内容 | 当前支持 |
| --- | --- |
| 普通图片 | JPEG、PNG、BMP、GIF、TIFF、ICO |
| 原生 HDR 图片 | WIC 浮点 scRGB JPEG XR：JXR / WDP / HDP；直接显示，不再进行 SDR → HDR 增强 |
| SDR → RTX HDR | 官方 TrueHDR；需要兼容 RTX GPU、驱动与启用 HDR 的屏幕 |
| AI 超分 | 官方 RTX VSR，独立开关，默认关闭 |
| 暂未支持 | HEIF / AVIF 增益图、JPEG XL、OpenEXR、RAW、动画播放；GIF / 多页 TIFF 当前显示第一帧 |

主流 HDR 格式会继续扩展，但**普通 SDR 照片的 RTX HDR 体验**仍是 Luma 的核心方向。大图片可能依据资源预算缩小处理；HDR 不保证每个区域都更亮，结果取决于图片、模型、显示器和参数。

## 自己构建

需要 Windows 11 x64、[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)、Visual Studio C++ x64 工具与 Windows SDK。准确 SDK 版本见 `global.json`。

```powershell
git clone https://github.com/lueluelue12138/luma-hdr-photo-viewer.git
cd luma-hdr-photo-viewer
./scripts/Build.ps1 -Publish -WithoutNvidia
# 运行 artifacts/Luma/Luma.App.exe
```

如果要体验 RTX HDR / VSR，通过自己的 NVIDIA 开发者账户取得 [RTX Video SDK](https://developer.nvidia.com/rtx-video-sdk)，按 [SDK 放置说明](external/nvidia/README.md) 准备，再运行：

```powershell
./scripts/Build.ps1 -Publish -RequireNvidia
```

普通看图不需要取得 SDK。完整便携文件夹必须一起保留，不能只复制 EXE。

图库与设置保存在 `%LOCALAPPDATA%\Luma`。打开文件夹会建立索引；双击图片启动预览时，不等待全图库扫描。默认查看器由你在 Windows 默认应用设置中选择，Luma 不改写系统的默认关联选择。

## 一起完善它

如果你也喜欢照片，欢迎提出使用感受、可复现的问题或改进建议。尤其欢迎不同 HDR 屏幕、RTX 显卡和大相册的实际反馈。

[待办清单](docs/TODO.md) · [已完成的验证与测量边界](docs/VERIFICATION.md) · [构建细节与迭代记录](docs/DEVELOPMENT.md) · [参与贡献](CONTRIBUTING.md) · [发布流程](docs/RELEASING.md)

## 许可

Luma 的原创代码与图标使用 [MIT](LICENSE)。第三方组件适用自己的许可，见 [第三方声明](THIRD-PARTY-NOTICES.md)。NVIDIA SDK 与运行库没有被重新授权为 MIT，不随公开源码仓库提供。

This software contains source code provided by NVIDIA Corporation. Luma is an independent project and is not affiliated with or endorsed by NVIDIA or Microsoft.
