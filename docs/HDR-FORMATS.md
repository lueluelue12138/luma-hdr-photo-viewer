# HDR 照片格式与实现范围

调研及实现：2026-10-14，Luma 0.1.18。扩展名只说明容器：普通 JPEG、8-bit PNG 和 16-bit TIFF 不会因为文件名或位深被认作 HDR。原生 HDR 不需要 RTX GPU，也不经过 NVIDIA 的 SDR→HDR 增强。

## 三个平台的重点

| 来源 | 主要 HDR 照片形式 | Luma 当前处理 |
| --- | --- | --- |
| Android | Ultra HDR JPEG：可直接兼容 SDR 的底图、第二张增益图及 XMP / ISO 元数据，仍使用 `.jpg` / `.jpeg` | 已实现增益图解析与 HDR 重建，直接进入 FP16 / scRGB；不是只打开 SDR 底图 |
| Apple | 相机 HEIC 中的 Apple / ISO Adaptive HDR 增益图，分享或导出也可能保留为增益图 JPEG；另有 PQ / HLG 的 ISO HDR 图像 | 本轮覆盖 Apple 增益图 **JPEG** 与 ISO 增益图 JPEG；**尚未支持 HEIC HDR**，不能据此声称完整支持 iPhone 相机原始文件 |
| Windows | HDR 屏幕截图的浮点、线性 scRGB JPEG XR，常见扩展名 `.jxr` / `.wdp` / `.hdp` | 已有浮点解码，本轮补充真实 Windows 截屏验证及 codec 级缩小解码；不是所有 JPEG XR 都是 HDR |
| 跨平台 / Web | AVIF 中的 PQ / HLG 或增益图，带明确 HDR 色彩标记的 PNG 等 | 保留为后续范围；本轮不安装或分发 HEVC / AV1 / libheif，不注册尚未验证的扩展名 |

Android 14 起提供 Ultra HDR 显示支持；它沿用 JPEG 的兼容底图，应用必须处理增益图才能显示 HDR。[Android 显示文档](https://developer.android.com/media/grow/ultra-hdr/display)、[格式规范](https://developer.android.com/media/platform/hdr-image-format)。

Apple 区分 ISO HDR 与增益图 HDR，较新的 Adaptive HDR 使用 ISO 21496-1 的元数据和重建规则。HEIF、JPEG、AVIF 等容器可能承载不同编码，不能只依赖 `.heic` 推断亮度曲线。[Apple HDR 图像说明](https://developer.apple.com/videos/play/wwdc2024/10177/)、[Apple 增益图计算说明](https://developer.apple.com/documentation/appkit/applying-apple-hdr-effect-to-your-photos)。

Windows WIC 的浮点 / 定点格式一般以 scRGB 为推断色彩空间；HEIF 扩展另有获取 8-bit gain 表示的接口，返回的增益仍需应用解析元数据并重建。只安装扩展不等于应用自动具有完整 HEIC HDR 路径。[WIC 像素格式](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-native-pixel-formats)、[HEIF 扩展与 gain 表示](https://learn.microsoft.com/en-us/windows/win32/wic/heif-codec)。

## 本轮实现

- 固定 Google **libultrahdr 2.0.2** 源码提交 `e5f5a022fe96fc4dc2ee35c19f733a50df807abe`。通过官方解析器取得底图、增益图、XMP / ISO / Apple 参数；使用官方 `UltraHdr::applyGainMap` 重建，不另写 HDR 增益公式。当前参考库的 RGB888 分类路径存在兼容问题，Luma 使用其完整支持的 RGBA8888 底图路径，未修改上游源码。
- WIC 先按目标尺寸分别解码两张 JPEG，再交给参考库。参考 C API 的 resize 效果在完整 HDR 解码后才执行，因此没有直接用它来缩小巨幅图片。
- 保留 sRGB / Display P3 / Rec.2020 色域，输出线性 Rec.709/scRGB。参考库线性输出的参考白为 203 nits，DXGI scRGB 的 1.0 为 80 nits，转换时乘以 `203 / 80`；保留超过 1.0 的高光和宽色域转换产生的负分量。未知 ICC 色域会报明确信息，而不会猜成 sRGB。
- HDR 图像与正确旋转后的兼容 SDR 底图一同缓存，CPU 预算同时计入两者。HDR 开启的屏幕使用 HDR 纹理，SDR 屏幕使用兼容底图；按住空格也可直接切换底图，松开恢复缓存的 HDR，没有 AI 重算。
- 缩略图沿用 JPEG 的 SDR 底图、ICC 和 EXIF，专用入口不会为每张小卡片重建 HDR。JPEG XR 保留现有 Windows 缩略图提供器。
- 原生 HDR 禁用 RTX HDR / VSR，显示来源及“无需增强”；普通 JPEG 的增强偏好保持独立。无需更改已有 `.jpg` / `.jpeg` / `.jxr` 关联。
- 两个版本 standard / RTX 都静态包含这个开放源码解码器。原生 DLL 实测约增加 0.80 MiB，另有许可文本；无需另装图片解码运行库。源码、JPEG SIMD 用的 NASM 工具及测试照片不随应用分发。输入下载固定 SHA256，许可和归属声明见 [第三方声明](../THIRD-PARTY-NOTICES.md)。

库的支持范围、线性半浮点输出及构建说明见 [Google 官方源码](https://github.com/google/libultrahdr/tree/e5f5a022fe96fc4dc2ee35c19f733a50df807abe)。JPEG 依赖选用当前稳定的 [libjpeg-turbo 3.2.0 官方源代码发行包](https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.0)，静态链接并启用 SIMD。

## 边界与后续顺序

本轮验证了 XMP-only 与 ISO JPEG、上游两份 Apple JPEG 样本、合成 4800 万像素增益图照片，以及用户实际 Windows 截屏。尚未获得多个品牌 Android 手机的原始拍摄文件或新版本 iPhone HEIC 样本，不能从几个样本推出所有设备及导出软件都兼容。

为控制资源，HDR 输出仍受 1600 万像素 / 16384 长边预算约束；大图保持比例缩小，原始尺寸保留，文件可以打开。增益图 JPEG 压缩输入预算 256 MiB、源像素预算 128 MiPixels；后者是异常输入保护，不要求普通照片低于预览输出预算。

当前重建完整的源 HDR 意图；低峰值屏幕上的逐屏 headroom 自适应、照片峰值与实际显示能力的色调映射还需专项实现与验证。SDR 底图切换已完成，但不声称所有屏幕都得到完全相同的 HDR 观感。

下一步优先 **HEIC 的 Apple / ISO 增益图及 PQ / HLG**，再考虑 AVIF。Windows WIC 扩展可减少安装体积，但必须验证辅助图、元数据、10-bit 输出和缺少编解码器时的状态；另一条路线是 libheif / libavif，加上其解码依赖及独立许可，避免为了一个格式捆绑编码器与整套视频工具。

JPEG XL、OpenEXR、RAW、HDR 视频和格式转换不纳入当前照片预览迭代。详细验证证据及差异见 [验证记录](VERIFICATION.md)，余项持续记录在 [TODO](TODO.md)。
