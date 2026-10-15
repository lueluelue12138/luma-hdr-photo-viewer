# Luma HDR Photo Viewer

**如果你有一块好 HDR 屏幕，别只用它看电影。也用它，重新看看自己的照片。**

![Luma 产品展示：浅色相册与照片预览、悬浮毛玻璃、独立 RTX HDR 与 VSR 控制](docs/images/luma-product-showcase.webp)

<sub>AI 生成的产品展示示意，示例风景为虚构，界面细节以实际版本为准。</sub>

旅行时的落日、夜里的灯火、相机里舍不得删的回忆，都值得在好屏幕上认真看一次。

Luma 是一个免费、开源的 Windows 原生照片查看器。它既能显示主流的 **原生 HDR 照片**，也把 **NVIDIA RTX Video HDR 带到了普通 SDR 照片**：不用重新拍摄，打开已有的 JPG，就能试着找回更有临场感的光影。其中 RTX HDR 增强需要兼容的 RTX 显卡与开启 HDR 的屏幕，实际效果随照片和设备而异。

**A native Windows photo viewer with NVIDIA RTX Video HDR for SDR photos, and native HDR image rendering.** Rediscover the photos you already love, with a clean interface that puts the picture first.

## 为什么做 Luma

我有一块很喜欢的 HDR 显示器。看 HDR 电影时，夕阳的光、夜里的霓虹、明暗之间的层次，经常让我忍不住暂停，把某一帧当成照片欣赏。

可轮到自己拍的照片，体验就没那么顺手了。Windows 的 HDR 生态还不够完整，我一直没找到一个同时满足这些心愿的查看器：**好看、好用、轻便，还能让 HDR 屏幕好好展示自己的回忆。**

后来，在浏览器里看普通视频时，RTX Video HDR 的效果给了我一个念头：既然它能让视频里的光影更动人，为什么不能用来欣赏自己的照片？Luma 就从这个念头开始了。

我们想补上 Windows HDR 体验里“看照片”这一块。打开就看，把界面留给照片，让好屏幕的价值也落在自己的生活里。RTX HDR 是对普通照片的 AI 增强，不能恢复拍摄时没有记录的信息；照片本身带有 HDR 时，则直接呈现它自己的光影。

## 用 Luma 看照片

- **熟悉的照片，换一种光影。** 普通 JPG 也能开启 RTX HDR，重新看看日落、灯光和明暗层次；使用官方 NVIDIA RTX Video SDK。
- **HDR 与 VSR 各自选择。** 可以只开 HDR；VSR 默认关闭，是否使用 AI 超分由你决定。按住空格，对比原图。
- **照片是主角。** 原生 WinUI 3、浅色/深色主题、Mica、悬浮毛玻璃面板；完整适配原比例，缩放、拖拽和全屏看图。
- **相册顺着你的习惯。** 添加文件夹、搜索、收藏、最近打开与多相册；照片留在原文件夹，从图库移除相册也不会删除原文件。
- **拍摄细节，随手查看。** 顶部信息入口可查看日期、相机、镜头、曝光及 HDR 来源；更多 EXIF 支持分组搜索、复制详情和打开文件位置。
- **没有 RTX，也能看图。** 普通 SDR 和已支持的原生 HDR 图片有独立显示路径；增强不可用时会说明原因。

## 第一次体验 HDR？先把屏幕准备好

1. **开启 Windows HDR。** 在系统“设置 → 系统 → 显示”中选中要用的屏幕，打开“使用 HDR”。Luma 设置页有直达显示设置的入口。
2. **让 Windows 认识你的屏幕。** 使用微软免费的 [Windows HDR Calibration](https://apps.microsoft.com/detail/9N7F2SM5D1LR)，按图案提示完成校准并保存。Luma 可直接打开已安装的校准应用，未安装时进入商店。
3. **打开自己的照片。** 普通照片在顶部“显示增强”中开启 RTX HDR；原生 HDR 照片会自动识别。按住空格对照原图或可用的 SDR 底图，找到自己喜欢的效果。

Luma 设置页会显示当前屏幕的 HDR 状态和 **Windows 报告的峰值亮度**。它可以帮助你发现明显不合理的系统报告，但不是亮度实测或“校准合格证”。厂商标称峰值的测试面积、显示模式可能不同，校准请以图案为准，别为了对上宣传数值而硬调。详见 [HDR 屏幕准备指南](docs/HDR-SETUP.md) 与 [微软校准说明](https://support.microsoft.com/windows/hardware/display-graphics/calibrate-your-hdr-display-using-the-windows-hdr-calibration-app)。

## 当前状态

**正在开发，尚未发布正式下载版。** 当前源码版本为 `0.1.23`，性能和实际使用体验仍在持续完善。首次未缓存的大相册缩略图、首次 AI 初始化，以及更多显示器上的验证还需要继续优化。

GitHub Actions 的 **standard** 开发构建可验证安装与普通看图，**不包含 NVIDIA SDK，也不提供 RTX HDR / VSR**。版本标签触发的 **rtx** 流程从受控私有构建输入读取官方 SDK，自动生成包含 TrueHDR / VSR 运行库的完整 EXE 与便携 ZIP，并上传到 **Release 草稿**；正式公开下载由维护者另行决定。两种构建都会明确标识能力并保留独立许可，详见 [发布流程](docs/RELEASING.md)。

## 支持什么

| 内容 | 当前支持 |
| --- | --- |
| 普通图片 | JPEG、PNG、BMP、GIF、TIFF、ICO |
| 原生 HDR 图片 | iPhone HEIC 的 Apple / ISO 增益图、PQ HEIC；Android Ultra HDR / ISO / Apple 增益图 JPEG；线性 scRGB JPEG XR（JXR / WDP / HDP）。直接显示，无需 RTX；增益图可按住空格对照 SDR 底图 |
| SDR → RTX HDR | 官方 TrueHDR；需要兼容 RTX GPU、驱动与启用 HDR 的屏幕 |
| AI 超分 | 官方 RTX VSR，独立开关，默认关闭 |
| 暂未支持 | HLG HEIC 的完整验证、AVIF HDR、JPEG XL、OpenEXR、RAW、动画播放；GIF / 多页 TIFF 当前显示第一帧 |

主流 HDR 格式会继续扩展，但**普通 SDR 照片的 RTX HDR 体验**仍是 Luma 的核心方向。大图片可能依据资源预算缩小处理；HDR 不保证每个区域都更亮，结果取决于图片、模型、显示器和参数。

HEIC 需要 Windows HEIF 图像扩展和 HEVC 视频扩展。已验证真实 iPhone 原片、ISO 增益图及 Apple 官方 PQ 样本；增益图跟随 Windows 白色与峰值。具体支持范围及后续验证见 [HDR 格式说明](docs/HDR-FORMATS.md)。

## 自己构建

需要 Windows 11 x64、[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)、Visual Studio C++ x64 工具、CMake 与 Windows SDK。准确 SDK 版本见 `global.json`。首次构建会下载并校验固定版本的开放源码 HDR/JPEG 解码器及 NASM；后续复用本地构建缓存，不随应用分发源码和工具。

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
