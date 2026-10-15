# Live / 动态照片与 RTX HDR

首轮实现：2026-10-15，0.1.24 源码与本地候选。当前公开下载仍是 0.1.23；本轮没有创建标签或公开 Release。

## 怎么用

打开完整动态照片，静态画面出现后，顶部会显示 **LIVE**。点击或按 **L** 播放，再按一次停止；**Esc** 先停止实况。默认静音，可点击原声按钮开启声音。播完、切图、窗口失焦或关闭时停止，回到缓存中的静态照片。

**空格继续用于原图对照。** 播放时按住空格，视频继续前进，只暂停 RTX HDR 增强；松开继续增强。静态照片自带 HDR 时，停止播放会回到原生 HDR 查看路径。

视频按需打开，不在导入相册或悬停缩略图时启动。未找到完整视频时仍按普通照片查看；编码器缺失、视频损坏或暂不支持的视频 HDR 编码不会阻止静态照片显示。

## 首轮支持范围

| 资源 | 当前行为 |
| --- | --- |
| iPhone HEIC / JPEG + MOV | 核对 Apple 图片与视频里的 content identifier；同名但标识不同不会误配。真实 HEIC + HEVC MOV 已验证，JPEG 配对有构造元数据测试，尚待真机 JPEG 配对样本 |
| Android Motion Photo JPEG | 读取现代 XMP 容器和内嵌 MP4，支持增益图与视频同时存在；Google、Pixel、小米真实样本已验证 |
| Motion Photo HEIC | 读取 HEIF 元数据 / mpvd 视频范围；Google 与 Samsung One UI 6 HEIC 已验证 |
| Samsung JPEG | One UI 5 / 6 样本的原始 SEF 尾部与 sefd atom 分别处理；保留照片原件 |
| 旧 Google MicroVideo | 独立兼容分支；现代标志与容器优先，显式关闭不能被旧字段覆盖；构造测试通过，尚待旧真机样本 |
| 原生 HDR 视频、Dolby Vision、.livp、AVIF Motion Photo、其他厂商变体 | 尚未完成。识别到 PQ / HLG / Dolby Vision 时明确保留静态照片，不压成 SDR 后冒充原生 HDR 视频 |

视频使用 Windows 媒体组件解码；HEVC 视频可能需要 **Windows HEVC 视频扩展**。HEIC 照片仍依赖 Windows HEIF / HEVC 扩展。本轮没有增加 FFmpeg、外部播放器、NuGet 包或新的安装前置。

首次播放最多接受 60 秒、8192 单边 / 1600 万像素的视频；内嵌片段限制 256 MiB，元数据限制 4 MiB。Apple 候选配对有数量上限，超大目录中的重命名视频可能无法自动找到。保留原始配对文件最可靠。

## Live 和 HDR 分开判断

Live 说明有动态内容，HDR 说明亮度 / 色彩数据，二者不是一回事。静态增益图只还原静态照片，不能直接套在所有视频帧上。

维护者提供的小米文件：14,202,351 字节，照片 3072×4096，原生 HDR 增益图实际解码成功；内嵌视频范围 10,397,382 + 3,804,969 字节，Windows 解码后为 1088×1440。本轮视频元数据未识别出原生 HDR，按 SDR 逐帧应用 RTX HDR，实际 SDK 调用与变化的 FP16 像素读回通过。原文件 SHA256 前后相同，未上传或加入 Git。

动态 SDR 可以选择 RTX HDR；VSR 在播放期间暂停，避免隐式增加负载。RTX HDR 需要兼容显卡、驱动、包含 NVIDIA SDK 的构建和已开启 HDR 的屏幕。没有 RTX 也可走普通 SDR 视频显示路径；本机当前的动态集成测试使用 RTX 5070 Ti Laptop，未把其他 GPU 的表现视为已验证。

## 实现与性能边界

Windows MediaPlayer 的 frame-server 模式负责解封装、解码、时间轴与音频；帧直接写入 D3D11 GPU 表面，再经过 Luma 现有 TrueHDR 与 FP16 / scRGB 呈现。内嵌视频通过只读、有边界的流访问，无需解压临时 MP4，也不逐帧复制 CPU 像素。

每个播放实例仅允许一个待处理帧，忙时合并通知，不堆积任务。视频纹理和 SDK feature 复用，静态照片保留缓存；播放时隐藏的静态 renderer 不随缩放或浮层变化重绘。工具栏 / 缩略图玻璃随视频刷新；弹出菜单使用交互时的画面快照，避免每帧 GPU 读回。

本机小米 / iPhone 测试中，自有 GPU 纹理在循环播放前后分别保持 80,065,440 / 99,234,720 字节。单帧提交耗时与 UI 回调延迟见 [验证记录](VERIFICATION.md)，它们不是物理帧间隔或 GPU 总耗时。通知合并数量不等于视频丢帧率，不据此宣传稳定 60 FPS。

**首次点击仍有约 1.2–1.9 秒的媒体 / GPU / SDK 初始化成本**，准备期间保留静态图，重播、结束与取消均已回归。精确音画同步、首帧视觉过渡、长时间功耗、跨屏与高刷新率仍需专项验收。当前不承诺任意厂商、任意分享版本都保留 Live 数据。

## 样本来源与复现

样本只保存在忽略的 `artifacts/live-photos/` 下，未打包进产品或提交 Git。本机完整来源、固定 commit、文件大小与 SHA256 见 `samples.json`；维护者原件单独保存。公开可下载不等于获准随产品重新分发。

- [Google 官方 libmotionphoto 测试资源](https://github.com/google/libmotionphoto/tree/12fb3cb8d1649a5621c55dcf99e416d32e4c4ab3/lib/core/motion_photo/testdata)：JPEG、HEIC 与纯静态反例。
- [Immich 测试资源](https://github.com/immich-app/test-assets/tree/ec56cc6c1d76c45bc9615dc6ea232470a2b0a3a1/formats/motionphoto)：Pixel 6 Pro / 8a、Samsung One UI 5 / 6 JPEG / HEIC。
- [Kingfisher 作者测试资源](https://github.com/onevcat/Kingfisher-TestImages/tree/6b292374307c973005ebe279cfb77d5a91110c14/LivePhotos)：完整 Apple HEIC + MOV 配对。

```powershell
./.tools/dotnet/dotnet.exe run --project tests/Luma.Tests -c Release
python tests/verify_live_photo.py '<完整动态照片路径>' --app artifacts/Luma-live-final/Luma.App.exe
```

集成测试使用独立图库，验证 SDK / FP16、视频变化、空格对应的对照状态、音轨与静音开关、循环纹理预算、静态缓存恢复、准备中取消、自然结束、重播与播放中关闭。声音开关状态测试不代替人工听感与音画同步测试。

## 参考与待扩展

- [Android Motion Photo 规范](https://developer.android.com/media/platform/motion-photo-format)
- [Apple Live Photo 元数据](https://developer.apple.com/documentation/avfoundation/avcapturephotosettings/livephotomoviemetadata)
- [Microsoft MediaPlayer / frame-server](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)
- [Microsoft 视频传递函数](https://learn.microsoft.com/en-us/windows/win32/api/mfobjects/ne-mfobjects-mfvideotransferfunction)
- [NVIDIA RTX Video SDK](https://developer.nvidia.com/rtx-video-sdk)

后续按真实样本扩展原生 HDR 视频、.livp、更多厂商，以及图片 / 视频旋转裁切与主照片时点的细节。先保证流畅和正确显示，不声称“全球独家”。
