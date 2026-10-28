# HDR 照片格式与实现范围

## Windows 截图转换后的绝对亮度（0.1.33）

独立转换器 0.1.1 以原片 scRGB 绝对亮度编码标准 Ultra HDR JPG，质量 100 / 4:4:4 底图与全尺寸 RGB 增益图，并按原片实际编码色域通道峰值设置显示适配容量。旧版 HDR-only 快捷路径的默认容量对应 10000 nits，容易在常见屏幕上压低增益权重；完整重建数值接近不能证明自动显示观感一致。

新输出的 EXIF ImageDescription 含明确的 `Luma Absolute HDR v1; ReferenceWhiteNits=203; Source=scRGB80` 来源说明。Luma 对这个精确、完整且有界的 TIFF 标记使用增益模型 3：保留标准完整重建、SDR 底图对照及 FP16 呈现，但像 JXR 一样保持绝对亮度，不再次按 Windows SDR 白色和屏幕 headroom 改变曝光。其他相机 / 手机增益图继续采用原有标准自适应路径；无标记、损坏或不同说明均不触发此行为。该来源说明是可选扩展，其他软件仍按标准增益图自行显示，不能据此承诺跨查看器完全一致。

维护者实图的实际 GPU FP16 自动显示对照及有损误差见 [验证记录](VERIFICATION.md)。未为小米或某个手机峰值做增强，也未把软件重建数值称作手机实测亮度。

调研及实现：2026-10-14；显示控制与 RGB XMP 兼容更新：2026-10-15，Luma 0.1.21。原生 HDR 不依赖 RTX，也不会重复进行 NVIDIA SDR→HDR 增强。扩展名和整数位深本身不能证明图片是 HDR。

## 三个平台

| 来源 | 常见形式 | 当前处理 |
| --- | --- | --- |
| Android | Ultra HDR JPEG，SDR 底图 + XMP / ISO 增益图 | 解析并重建线性 HDR，经 FP16 / scRGB 显示 |
| Apple / iPhone | HEIC 中的 Apple / ISO 增益图；PQ HDR HEIC；分享后的增益图 JPEG | 已支持 Apple / ISO 增益图 HEIC、PQ HEIC、Apple 增益图 JPEG；实测维护者 iPhone 15 原片、公开相机 HEIC、iPadOS ISO HEIC 和 Apple 官方 PQ 样本 |
| Windows | 浮点线性 scRGB JPEG XR 截屏，JXR / WDP / HDP | 保留原始浮点亮度，经 FP16 显示，已有 6 张实际截屏验证 |
| 待补充 | HLG HEIC、AVIF HDR、HDR PNG、HDR 基底 / 替代色域增益图 | 尚未作为完整能力宣传 |

[Android 格式规范](https://developer.android.com/media/platform/hdr-image-format)、[Apple HDR 图像说明](https://developer.apple.com/videos/play/wwdc2024/10177/)、[Apple 官方 HDR 示例](https://developer.apple.com/documentation/uikit/supporting-hdr-images-in-your-app)。

## HEIC 解码

利用已经安装的 Windows **HEIF 图像扩展 + HEVC 视频扩展**。本轮没有安装系统扩展；缺少解码器时返回安装提示。扩展的可用性和收费由 Microsoft Store 决定，Luma 不捆绑 HEVC、libheif 或整套视频工具。standard 和 RTX 版都具有此原生 HDR 路径。

Luma 只解析有界的 HEIF 项目关系和元数据，像素、网格、旋转及裁切交给 WIC。Apple 增益图通过系统 `GUID_WICPixelFormat8bppGain` 取得；ISO 图通过 `tmap` / `dimg` 关联选择底图和增益图，选择只发生在内存副本，原文件不变。HEIC / HEIF 已加入文件激活、图库索引、选择器及安装包关联清单。[Microsoft WIC HEIF 接口](https://learn.microsoft.com/en-us/windows/win32/wic/heif-codec)。

Apple 路径遵循 [Apple 发布的计算说明](https://developer.apple.com/documentation/appkit/applying-apple-hdr-effect-to-your-photos)：底图按其 sRGB / Display P3 曲线线性化，增益按 Rec.709 反变换，使用线性 headroom 相乘；有明确 XMP headroom 时直接读取，否则从 MakerNote 33 / 48 推导。所用参考库把 Apple 的线性 XMP headroom 当成 log2，并按普通 ISO 增益公式处理；0.1.19 在自主代码中修正这两个 Apple 兼容问题，HEIC 与 JPEG 都采用独立公式测试，上游源码未改动。

ISO 元数据及 gain 运算使用固定 [libultrahdr 2.0.2](https://github.com/google/libultrahdr/tree/e5f5a022fe96fc4dc2ee35c19f733a50df807abe)，JPEG 使用 libjpeg-turbo 3.2.0，原始许可和 Adobe 声明保留。PQ HEIC 使用 WIC 返回的浮点线性 scRGB，已与独立 HEVC 解码及 ST.2084 公式核对，经真实 FP16 读回；没有把 10-bit PQ 转为普通 RGBA8 看图。

### RGB XMP 增益图 JPEG（0.1.21）

部分手机 / 编辑软件分别记录 R、G、B 的增益上下限、Gamma 和偏移，采用 `rdf:Seq`，也可用单值广播。[Android 规范](https://developer.android.com/media/platform/hdr-image-format#hdr-gain-map-metadata) 允许这两种表达。固定版本的上游 XMP 解析仅识别部分单值属性；Luma 自主代码使用 Windows XmlLite 补齐属性、元素、1 / 3 项有序数组及命名空间别名，逐通道重建，ISO 元数据仍优先。未改上游源码，也不将 Apple 曲线套成 Adobe 公式。

XML 长度、深度、节点与数值均设边界，禁止 DTD / 外部实体。缺失、无效或当前不支持的可选 HDR 数据会保留可读取的 JPEG 主图，并明确标为 **兼容预览 · HDR 数据未读取**；该状态禁用 HDR / VSR 和 HDR 调参，不将普通预览冒称原生 HDR。实际主图无法解码仍显示加载失败；内存预算错误不作为成功预览吞掉。目前此兼容回退针对 JPEG，HEIC 的未知变体继续列在 TODO。

`tests/verify_xmp_gainmaps.py` 在本机和 CI 生成 RGB / Gamma / 元素 / 命名空间测试图，以独立公式和真实 FP16 读回验证；覆盖异常元数据、ISO 优先、缓存底图切换以及不可读主图。可选的本地用户照片对照不上传文件，不随源码或安装包发行。

## 亮度与屏幕适配

增益图描述相对于 SDR 底图的增益。缓存完整 HDR 时使用 203 nits 参考白转为 scRGB（1.0 = 80 nits），保留高光和宽色域负分量。显示时匹配 Windows SDR 白色，以峰值 / 白色比决定 ISO 对数权重，避免底图对照产生与照片无关的整体变暗。换屏或白色 / 峰值变化只调整 GPU 参数，无需重新解码或运行 AI。空格直接切换两份缓存纹理。

峰值通过与 RTX HDR 相同的 `IDXGIOutput6::GetDesc1` 读取，Windows 已应用的 MHC 校准配置可以覆盖这些显示器特性。[Microsoft HDR 呈现说明](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)、[校准管线](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc)。整个输出保持 FP16 / scRGB。

本机 Windows 报告约 **3675.74 nits** 峰值、**480 nits** SDR 白色。恒定增益测试经真实 GPU FP16 读回约 **3675 nits**；203 / 406 / 812 / 1600 nits 端点测试也通过。这证明管线可以表达系统报告的峰值，不是亮度计测量了屏幕。照片 headroom 不足时保持其范围，不额外制造高光。

NVIDIA SDK 1.1.0 明确规定 TrueHDR `MaxLuminance` 参数为 **400–2000 nits**，RTX 增强继续遵守此范围。**原生增益图不受这个 SDK 参数上限约束。** PQ / 原始 scRGB 是绝对亮度数据，自动模式不随 SDR 白色再次缩放；其自动跨屏峰值压缩仍在 TODO 中。

### 原生 HDR 的手动显示控制（0.1.20）

原生 HDR 与 RTX 增强各自保存亮度偏好。原生 HDR 允许手动中间灰参考 **10–300 nits**、峰值 **80–10000 nits**，自动模式保留 0.1.19 的映射。这里的数值是显示参数 / 参考值，并非测量某张照片实际具有多少中间灰或高光；自动值旁显示当前屏幕 / 参考数值。PQ / JXR 的自动峰值行明确标为“屏幕峰值参考”，自动模式保留原片绝对亮度。

- 增益图：手动中间灰决定 SDR 参考白（gray / 0.21586），手动峰值决定已有增益的对数权重。不会创造超出原片 headroom 的高光。
- PQ / JXR：手动中间灰是曝光控制，按相对于 203 × 0.21586 nits 的比例缩放线性 RGB；手动峰值在目标 75% 以上使用连续、保持 RGB 比例的指数肩部压缩，低于肩部的像素保持不变。这是本应用的显示曲线，不声称复现 Windows Photos 的私有映射。自动模式完全跳过此曲线。
- 全部调整在 GPU FP16 呈现时完成，不重新解码，不执行 RTX HDR / VSR，也不增加图片纹理。浮点源的负色域分量保留。设置和截图数值不等于屏幕物理亮度。

增益图 HEIC / JPEG 带有 SDR 底图，因此按住空格可以对照。PQ HEIC / JXR 通常没有 SDR 底图，自动模式的对照按钮明确提示这个限制；手动调整后，空格可临时查看未调整的 **HDR 原片**，松开恢复显示参数。原片及两份缓存纹理都不改写。关于两类 HDR 数据，参见 [Apple HDR 图像说明](https://developer.apple.com/videos/play/wwdc2024/10177/)；scRGB 采用 [Microsoft 的 80 nits 定义](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)。

`tests/verify_native_hdr_tuning.py` 检查真实 FP16 输出、增益 / 曝光 / 峰值的独立数值、自动恢复、毛玻璃场景缓存中按住原片的正确刷新，以及无新增 SDK 调用 / 图片显存。无 SDK 和 RTX 原生核心都运行同一检查。

## 性能与边界

- 缩略图使用 SDR 底图，不重建 HDR；浮点源交给 Windows 缩略图提供器。宽色域 HDR、SDR 对照和额外增益计算底图全部计入 CPU / GPU 预算。
- Apple 的 8-bit 曲线使用查找表，避免逐像素重复幂运算。按预览尺寸重建；HDR 输出预算 1600 万像素 / 16384 长边，压缩输入 256 MiB、HEIF 元数据 16 MiB。大图保持比例缩小，不因超过预览尺寸而禁用 HDR。
- Apple 增益版本覆盖 65536 / 131072，识别 sRGB、Display P3、Rec.2020。未知 ICC、反向 HDR 基底或尚不支持的增益应用色域明确报错，不猜测 HDR。
- 增益图 SDR 底图目前使用 WIC RGB8；**PQ HEIC 的高位深浮点路径独立**。高位深增益底图、复杂 ICC、更多手机导出格式仍需补充。
- libheif / Pillow 和 WIC 使用不同色度与缩放滤波，真实样本存在像素差异，未声称逐像素一致。平均 / 最大误差和首次解码时间见 [验证记录](VERIFICATION.md)。
- 0.1.20 对 10 张 Apple 官方 PQ 样本、2048 长边各解码三次，中位耗时约 1.11–2.03 秒；未清空系统文件缓存，不含窗口、上传或缩略图竞争。尝试系统 codec 的 float 缩小接口未取得收益，已撤回。512 长边数据不能用来宣传 2048 预览为瞬时加载，后续继续优化该路径。
- 后续补充真实 HLG、原生 PQ / JXR 的低峰值 / SDR 色调映射、多屏物理观感和 AVIF。测试照片及第三方参考解码器只保留在忽略的 QA 目录，不随应用或源码发行。

自动验证：`scripts/Get-HdrSamples.ps1` 固定样本 SHA256；`tests/verify_heic.py` 使用 QA 专用 pillow-heif / numpy 做独立对照；`tests/verify_hdr_peak.py` 检查真实 FP16 端点；隔离 WinUI 混合翻图和 Computer Use 检查验证来源、完整适配、SDK 跳过及缓存对照。余项见 [TODO](TODO.md)。
