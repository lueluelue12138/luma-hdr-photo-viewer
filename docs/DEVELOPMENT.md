# Luma

Windows 11 原生图库与图片查看器。C# / WinUI 3 负责界面，C++ / Direct3D 11 / WIC 负责解码与 FP16 scRGB 呈现，SQLite 保存图库索引。

## 启动

本机便携版：`C:\workspace\Luma\artifacts\Luma\Luma.App.exe`。整个文件夹必须一起保留，不能只复制 EXE。

- 直接启动进入图库；导入文件夹建立相册，照片保留在原位置。
- 将照片或文件夹拖入图库，或点击顶部的“打开照片”按钮。
- 在“设置 → 在 Windows 中选择 Luma”中将便携版添加到开始菜单并注册为可选图片查看器，由用户在 Windows 设置中选择默认格式。移动便携版后需再次注册。本机应用专属跳转仍打开通用默认应用页；若应用列表未显示 Luma，可按 `.jpg`、`.png` 等扩展名选择。系统查看器枚举已识别 Luma，完整默认应用选择流程尚未验收。
- `Luma.App.exe "D:\Photos\photo.jpg"` 直接打开照片，先解码当前照片，再列举同文件夹图片，不等待全图库扫描。
- 本地数据位于 `%LOCALAPPDATA%\Luma`；数据库为 `library.db`，异常日志为 `luma.log`，NVIDIA 日志为 `ngx`。

## 已实现

0.1.16 缓存已完成的高质量 SDR 缩略图，采用无损压缩、像素/尺寸校验、源文件大小/修改时间与 DPI/尺寸键，跨进程复用；损坏或不可写时退回 WIC。磁盘缓存位于本地数据目录的 `thumbnail-cache-v1`，上限 256 MiB / 4096 项，与 32 MiB 内存图片缓存独立；原生 HDR 不写入 SDR 缓存。3043 张相册重开后的首屏实现卡片完整等待实测约 56–87 ms，首次未缓存仍约 1.0–1.2 秒，不含进程启动。旧版“快滚后约 0.85 秒”的计时还包含刷新，已修正并在验证记录保留说明，不能把测量修正算作优化收益。

0.1.15 按预览缩略图栏的实际尺寸选择解码像素，保留 DPI 余量；图库卡片继续使用原有较高分辨率。较大的缓存或正在处理的缩略图可被小控件复用，不重新读取同一文件；小尺寸结果不能替代图库所需的大尺寸。3043 张相册的缩略图缓存实测从约 31.6 MiB 降到 6.9 MiB。多次随机开图仍约 0.34–0.37 秒，没有稳定速度下降的证据，因此主要收益是像素处理和内存开销；完整数据与功能回归见验证记录。

0.1.14 将直接 WIC 缩略图扩展到常见 SDR 格式，复用有独立预算的像素暂存缓冲，减少大块临时数组及每次缓存插入的排序分配。浮点原生 HDR 和解码失败仍使用 Windows 缩略图提供程序；照片的 FP16/NVIDIA 路径不变。新增完整只读相册压力验证，测试数据库与实际图库分离。3043 张、约 54.8 GiB 相机 JPG 的滚动与随机开图通过：两轮滚动 UI 回调 P95 约 9 ms，40 次跨位置开图首图中位约 369 ms，邻图预读命中 5–7 ms。缩略图未缓存时首屏仍需约 0.8 秒全部完成，历史约 120 ms 调度峰值尚未确定原因；这些继续列为性能待办，未声称所有操作已达到瞬时响应。资源、GC 对照与测量边界见验证记录。

0.1.13 继续优化大 JPG 的图库及预览缩略图：后台使用已有 WIC 缩小解码、ICC 色彩转换和 EXIF 方向处理，直接交给 WinUI 位图，减少 Windows 缩略图服务与重复解码的等待。保留四个加载槽、32 MiB / 96 项缓存、过时请求检查及其他格式的系统缩略图路径；不调用 GPU 或 NVIDIA 增强。缩略图按 DPI 选择像素尺寸，并修正竖图的居中裁切。扫描期间收到的刷新不再被丢弃，会合并到下一轮；无变化的刷新保留现有卡片。96 个大 JPG 路径的同版本对照中，快速滚动后回到首屏的缩略图等待约 2.79 秒 → 268 ms；样本为同一张 7008×4672 照片的副本，不代表所有相册或冷磁盘表现。更多样本及界面调度峰值见验证记录。

0.1.12 在刚关闭的预览之间短暂复用 D3D 设备和 NVIDIA 初始化会话，减少重新开图的等待。最多保留一个完整的闲置渲染器，10 秒后释放；关闭时清空照片、窗口和毛玻璃缓冲，不共享活动窗口的上下文。SDK 功能实例在图片缓冲变化或窗口关闭时释放，避免不同尺寸照片造成实际显存持续增长。同尺寸纹理和当前照片的原图对照继续复用。统一 SDK 会话生命周期，避免关闭一个设备后让其他窗口的功能创建失效；停止工作的已注册设备在最后一个 SDK 使用者退出前可重新移交。普通 SDR 不触发新的 SDK 初始化或 AI 调用。首次新进程/缓存过期后的初始化成本仍存在，SDK/驱动内部缓存与更多显示拓扑继续验证。

0.1.11 复用同一预览中尺寸/格式兼容的原图、SDK 输入和增强纹理，避免每张照片重复分配；每张新图仍更新输入并真正执行所请求的 HDR/VSR。透明度属性在解码时计算一次，缓存照片上传不再重复扫描所有像素。新 SDR、透明图片和原生 HDR 图片释放过时的 AI 图片缓冲。两轮原生流水线对照、大图细节、真实 SDK 像素及窗口生命周期验证见记录。

0.1.10 移除激活路径选择与预览窗口构造中的同步文件查询，避免慢磁盘在创建界面前阻塞；解码返回文件大小与修改时间，保持历史数据准确。文件已消失时预览显示该文件的错误。新增实际 SDR / HDR / VSR / 组合路径的首图与处理完成测量，见验证记录。

0.1.9 继续优先优化性能：跨预览窗口共享有预算的解码缓存，前台/预读合并同一解码，关闭后重新打开可复用像素。未变化的图库刷新保留卡片和封面；快速滚动跳过离屏卡片的缩略图任务。照片与悬浮毛玻璃同帧呈现，绘制/Present 在后台，静态照片复用原生模糊结果。实际启动、内存、显存、长相册与 HDR/VSR 验证见 docs/VERIFICATION.md。

按需引用原 SDK 的 WinUI、基础、交互与文字模块，移除未使用的 Windows ML、AI、搜索与 Widgets，保留自包含离线运行。发布到新目录再替换，避免旧依赖残留。文件/文件夹选取改用 Windows App SDK 的桌面原生选择器；实际导入、打开照片、取消与再次选择通过 Computer Use 验证。

0.1.6 统一图库按钮与排序的原生圆角风格，移除相册卡片的整块悬停底色，放大时保留文字与图片圆角。原生滚动条使用独立侧边留白；照片数量和文件夹路径移到标题下方，取消占用照片区域的固定底部提示。

0.1.5 修复在线相册刷新后残留已删除照片的问题，忽略 `._` 元数据文件，并为无法读取的封面寻找可用照片。减少窗口缩放时的重复布局和解码，显示器检测在后台运行。点击照片可手动隐藏/恢复浮层；空格对照优先于按钮和缩略图的默认操作，回车确认控件。缩略图选中边框完整包围照片，滚轮缩放采用缓动，全屏有原生 Composition 内容过渡和对应退出图标。

0.1.4 修复原图对照的性能回退：空格键和“按住查看原图”直接选择已驻留显存的原图/HDR/VSR 纹理，不重复解码、初始化或调用 AI。长按重复按键不重新排队；对照不改变保存的增强开关。首次增强尚未完成时仍需处理，但已有结果后的对照只等待正常画面呈现。

0.1.3 优化看图：按显示尺寸解码预览，JPEG 使用 WIC 原生缩小解码，在色彩转换前降低像素量；放大或真正执行增强时加载原始细节。邻图预读与当前图片解码独立排队，普通 SDR 看图跳过 NGX 初始化。窗口按目标屏幕工作区居中，图库工具栏统一尺寸；缩小自动居中、放大可拖拽并约束边界；玻璃使用中性灰色材质，保留实际照片的采样颜色。

浅色、深色及跟随系统；图库 Mica 外壳、预览 Desktop Acrylic 透光留边、原生标题栏、实际采样照片的模糊悬浮面板；图库虚拟化、后台缩略图；递归导入、多文件夹相册、汇总图库、搜索、收藏、最近打开；相册重命名、封面、排序、刷新、变更监听及离线索引保留。从图库移除相册保留原文件。

预览默认按比例完整适配，不裁切；缩放、平移、旋转、100%、全屏、邻图预读、过时解码取消、原图对照；浮层闲置淡出，点击照片可手动隐藏/恢复，手动隐藏期间鼠标移动不再打断。增强、绘制和 Present 在后台执行，SwapChainPanel 绑定与界面控件更新留在界面线程。页面、卡片和加载指示使用 Composition 动画，视角缩放使用缓动，浮层与 GPU 玻璃同步渐变，并遵循 Windows 关闭动画的设置。底部缩略图栏保持居中且有宽度上限。

0.1.2 默认跟随 Windows 主题；明确选择浅色或深色时保留选择。HDR、VSR、风格、中间灰、峰值及自动匹配开关保存至本地设置，新照片和应用重启后沿用。VSR 仅在首次使用时默认关闭；增强请求与实际生效状态独立，当前照片或设备不支持时仍明确报告原因。

底部缩略图使用 WinUI 的横向虚拟化列表、滚动条及平滑滚动；滚轮只浏览列表，不更换当前照片。翻图按钮位于预览左右边缘，鼠标悬停时淡入，方向键继续翻图。窗口内容通过 Windows Composition 在准备首帧后淡入和位移，列表使用原生主题过渡。系统关闭动画时尊重设置；远程连接可能改变动画观感，未强制修改系统设置。

0.1.1 修复反复打开照片导致的 WinUI 重入崩溃：Windows 文件与缩略图服务全部移至后台，返回内存数据后才更新界面。窗口在最终 DPI 布局与两次合成帧完成后显示；D3D / NVIDIA 初始化及释放在后台，SwapChainPanel 的绑定和解绑留在界面线程，多个窗口的 NGX 调用串行执行。

玻璃使用 Windows Direct2D 的原生高斯模糊，对实际线性 FP16 照片场景处理，仅用于控件背景，照片本身不透明且不会变模糊。保留模糊后的预乘 alpha，使照片与透明留边交界处渐变，避免硬边。弹窗使用独立的照片材质背景以正确遮挡下层控件；其控件背景转换为 SDR 位图，照片的 HDR 呈现始终保留 FP16。关闭 Windows 透明效果或启用高对比度时使用可读的不透明背景。WinUI Acrylic 无法采样 SwapChainPanel，见 [微软说明](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel)；使用的 [原生高斯模糊](https://learn.microsoft.com/en-us/windows/win32/direct2d/gaussian-blur) 由 Windows 实现。

NVIDIA RTX™ Video SDK 1.1 的 TrueHDR 与 VSR 独立控制，VSR 默认关闭。仅 HDR 不创建或执行 VSR。成功调用并在当前 HDR 屏幕上呈现后才显示“已生效”。结果缓存于当前照片的 GPU 纹理；原图对照不重复执行 AI。SDK 缺失或不兼容时可正常查看 SDR / 原生 HDR 图片。

TrueHDR 输入为 sRGB RGB UNORM，输出直接使用 FP16 线性 scRGB，不经过 8-bit 图片控件。SDR 按 Windows SDR 白电平显示；HDR 中间灰默认匹配此亮度，避免固定 50 nits 造成明显变暗。“亮度与显示器”中可手动调整 SDK 中间灰及峰值参数。HDR 不保证所有区域更亮，暗部与整体色调由实际模型和参数决定。

普通解码与 GPU 缩放不调用 VSR。HDR 没有固定 8K 门槛，7008×4672 已按完整分辨率通过真实 SDK 验证。CPU 单图解码约 256 MB 预算；每个预览窗口的 GPU 纹理预算为专用显存的四分之一、范围 256 MB–1 GB，超过预算时按比例处理。D3D11 纹理轴上限为 16384。增强面板会说明资源预算造成的缩小或不可用。预算不包含 NVIDIA 模型及驱动自身分配，整个进程内存可能高于解码缓存预算。

## 格式与当前限制

JPEG、PNG、BMP、GIF、TIFF、JPEG XR（JXR/WDP/HDP）、ICO。GIF 与多页 TIFF 当前显示第一帧。原生 HDR 支持 WIC 浮点 scRGB JPEG XR，绕过 SDR→HDR 增强；HEIF/AVIF 增益图、RAW、动画播放及跨文件夹虚拟相册留待后续。超大图片按解码预算缩小后，100% 仍按原始尺寸布局，不能恢复已缩小的像素细节。

显示器/HDR 状态变化和设备丢失已有检测与重建路径；换屏、休眠恢复及非 RTX 硬件的实机覆盖仍需扩展。HDR 截图可能出现 Windows 截图映射偏差，验证以 FP16 GPU 读回为准。

## 构建与验证

需要 .NET SDK 8.0.425、MSVC x64 C++ 工具和 Windows SDK（本机为 10.0.26100.0）。本机已装 .NET 在 `.tools/dotnet`，脚本优先使用该 SDK。使用 Windows App SDK 2.5.1 对应的组件版本：WinUI 2.3.9、基础 2.3.12、交互 2.1.9、文字 2.1.0。SDK 和工具不放入源代码仓库。

```powershell
.\scripts\Build.ps1                 # 原生核心 + WinUI Release
.\scripts\Build.ps1 -Publish        # 自包含便携版 artifacts/Luma
.\scripts\Verify.ps1                # 25 项图库/激活检查 + WIC / D3D11 / RTX 检查
.\scripts\Verify.ps1 -Photo "D:\Photos\large.jpg"
python .\tests\benchmark_photos.py "D:\Photos\large.jpg"
python .\tests\benchmark_textures.py "D:\Photos\large.jpg" --output artifacts\texture-run --expect-reuse
python .\tests\verify_texture_transitions.py
python .\tests\benchmark_startup.py "D:\Photos\large.jpg" --output artifacts\startup-run
python .\tests\verify_viewer_lifecycle.py "D:\Photos\large.jpg" --runs 3
python .\tests\verify_viewer_interaction.py "D:\Photos\large.jpg"
python .\tests\benchmark_decode.py "D:\Photos\large.jpg"
python .\tests\verify_gallery_performance.py --large-photo "D:\Photos\large.jpg"
python .\tests\verify_gallery_performance.py --read-only-folder "D:\Photos\album"
python .\tests\verify_gallery_performance.py --read-only-folder "D:\Photos\album" --viewer-stress
python .\tests\benchmark_thumbnail_cache.py "D:\Photos\album"
python .\tests\verify_thumbnail_pixels.py
.\scripts\Package.ps1 -SkipBuild    # 生成并验证未签名 MSIX
```

测试需要 Python 与 Pillow。`Generate-Assets.py` 从仓库 `branding/Luma.png` 重新生成维护者提供的透明图标尺寸。NVIDIA SDK 按仓库根目录 [SDK 说明](../external/nvidia/README.md) 放置；不放置也可构建，增强按钮会准确报告不可用。

实际通过的检查、亮度测量及尚未完成的实机项见 [验证记录](docs/VERIFICATION.md)。解锁后已补完全屏恢复、小窗口布局、收藏快捷键及系统注册检查；完整系统选择器交互、跨屏和休眠恢复仍需扩展验收。

MSIX 为 `artifacts/Luma_0.1.16.0_x64.msix`，便携 ZIP 为 `artifacts/Luma_0.1.16_x64.zip`。包包含实际支持格式的文件关联和开始菜单入口。本地 MSIX 未签名，不能按正式安装包直接安装；需要使用 Publisher 匹配的受信任签名证书，或后续由 Microsoft Store 签名。当前直接使用便携版。未更改系统证书信任或默认关联选择。

用户反馈、性能/体积优化和发行准备持续记录在 [待办清单](docs/TODO.md)，新增事项也统一写入此文档。

## 许可与发行

Luma 原创代码及图标使用 MIT；第三方组件适用各自独立许可，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。This software contains source code provided by NVIDIA Corporation.

NVIDIA RTX™ Video SDK 提供 HDR 和 VSR 增强。NVIDIA SDK / DLL 没有重新授予 MIT 许可；本地输出附带 NVIDIA 原始许可。正式发行前仍需完成该许可的分发条件、通知与商标使用核对，以及名称/商标检查、MSIX 签名和商店验收。项目没有公开发布。

Windows 默认应用集成依据 [Microsoft 默认程序注册文档](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs)及[默认设置 URI 文档](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings)。scRGB SDR 白电平处理依据 [Microsoft Advanced Color 文档](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)。
