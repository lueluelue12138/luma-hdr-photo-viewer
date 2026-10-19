# 微软商店页面文案草稿

用于 Partner Center 手动填写。名称以实际预留结果为准；当前应用界面主要为简体中文，不宣称已有完整英文界面。

## 产品名称

Luma HDR Photo Viewer

## 简短描述

让好 HDR 屏幕，也用来欣赏自己的照片。原生 HDR 预览，普通照片也能体验 RTX HDR。

## 描述

如果你有一块很喜欢的 HDR 屏幕，别只用它看电影。

旅行时的夕阳、夜晚的霓虹、相机里舍不得删掉的回忆，也值得在这块屏幕上重新看看。Luma 是免费、开源的 Windows 原生照片查看器：美观的界面、顺手的相册，以及为 HDR 屏幕准备的看图体验。

支持的原生 HDR 照片可以直接显示；普通 SDR 照片则能通过 NVIDIA RTX Video HDR 增强光影。按住空格，随时对比原图或可用的 SDR 底图。HDR 与 AI 超分独立控制，效果由你选择。

导入文件夹即可浏览相册，支持相册置顶与排序、搜索、收藏和最近打开；HDR、实况与普通静态照片可多选筛选。缩放拖拽、全屏和拍摄信息随手可用。照片留在原文件夹，不需要注册或上传。

还能回放实况照片里快门前后的片刻：支持完整 iPhone HEIC＋MOV，以及已验证的 Google、Pixel、三星和小米动态照片。点一下实况图标即可播放，声音选择会记住；动态部分为 SDR 时，也能选择 RTX HDR。

使用条件：Windows 11 x64。HDR 显示需要 HDR 屏幕并开启 Windows HDR；RTX HDR / VSR 另需兼容的 NVIDIA RTX 显卡和驱动。原生 HDR 不需要 RTX；HEIC 需要 Windows HEIF / HEVC 扩展，相关扩展的可用性和费用由微软商店决定。

当前支持 Ultra HDR / ISO / Apple 增益图 JPEG、Apple / ISO 增益图和 PQ HEIC，以及线性 scRGB JPEG XR。并非所有同扩展名图片都包含受支持的 HDR 信息。AVIF HDR、RAW、GIF 动画、原生 HDR 实况视频和 .livp 暂未支持；请保留完整实况原片与配套视频。大相册首次缩略图、AI 初始化与首次实况播放可能需要等待。

RTX HDR 是 AI 增强，不代表恢复拍摄时未记录的信息。Luma 是独立项目，并非 NVIDIA 官方应用。

## 功能列表

- 原生 HDR 照片预览：Ultra HDR JPEG、兼容的 iPhone HEIC 和 Windows JXR 截图。
- NVIDIA RTX Video HDR 为普通照片增强光影；VSR 超分独立开关。
- 实况 / Motion Photo 按需播放、记住原声选择；支持的 SDR 动态也可使用 RTX HDR，播放期间暂停 VSR。
- Windows 原生界面、深浅主题、悬浮工具栏、全屏和缩放拖拽。
- 文件夹相册、置顶排序、HDR / 实况多选筛选、搜索与收藏；原照片保留原位置。
- 拍摄信息与 EXIF 搜索复制；Windows HDR 状态、峰值亮度及校准入口。

## 可填字段

- 类别建议：照片与视频（以后台可选项为准）。
- 价格建议：免费；无广告、无应用内购买、无登录。
- 关键词候选：HDR、照片查看器、RTX HDR、Ultra HDR、HEIC、JXR、photo viewer（按后台数量与长度限制取舍）。
- 隐私说明：https://github.com/lueluelue12138/luma-hdr-photo-viewer/blob/main/PRIVACY.md
- 支持页面：https://github.com/lueluelue12138/luma-hdr-photo-viewer/issues
- 项目网站：https://lueluelue12138.github.io/luma-hdr-photo-viewer/
- 附加许可说明：Luma 原创代码为 MIT；NVIDIA 组件适用随包独立条款及原始许可。正式提交时将 `packaging/RTX-EULA.rtf` 内容及原始许可链接同步到后台适用的附加许可字段，不能把整包都声明为 MIT。

## 截图计划（尚未上传）

使用真实发布版界面和有公开展示授权的照片，准备相册、看图、显示增强、图片详情、HDR 设置 4–5 张 PNG。建议每张 1366×768 或更高，不超过 50 MB，不使用带私人路径 / 人像的用户截图，也不把 README 产品示意图当作真实截图。普通商店截图不能证明实际 HDR 峰值或还原真实 HDR 亮度。

2026-10-19：已准备维护者原始看图截图的未编辑 PNG 副本 `artifacts/store-assets/Luma-Viewer-SDR.png`（2118×1255，3,269,066 字节），没有使用 README 的标签合成图，没有上传 Partner Center。其余截图与最终上传仍待维护者确认。
