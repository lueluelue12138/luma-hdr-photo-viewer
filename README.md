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

0.1.4 修复原图对照的性能回退：空格键和“按住查看原图”直接选择已驻留显存的原图/HDR/VSR 纹理，不重复解码、初始化或调用 AI。长按重复按键不重新排队；对照不改变保存的增强开关。首次增强尚未完成时仍需处理，但已有结果后的对照只等待正常画面呈现。

0.1.3 优化看图：按显示尺寸解码预览，JPEG 使用 WIC 原生缩小解码，在色彩转换前降低像素量；放大或真正执行增强时加载原始细节。邻图预读与当前图片解码独立排队，普通 SDR 看图跳过 NGX 初始化。窗口按目标屏幕工作区居中，图库工具栏统一尺寸；缩小自动居中、放大可拖拽并约束边界；玻璃使用中性灰色材质，保留实际照片的采样颜色。

浅色、深色及跟随系统；图库 Mica 外壳、预览 Desktop Acrylic 透光留边、原生标题栏、实际采样照片的模糊悬浮面板；图库虚拟化、后台缩略图；递归导入、多文件夹相册、汇总图库、搜索、收藏、最近打开；相册重命名、封面、排序、刷新、变更监听及离线索引保留。从图库移除相册保留原文件。

预览默认按比例完整适配，不裁切；缩放、平移、旋转、100%、全屏、邻图预读、过时解码取消、原图对照；浮层闲置淡出，鼠标移动及键盘操作时恢复。增强在后台准备，GPU 完成后由界面线程呈现。页面、卡片、缩放和加载指示使用 Composition 动画，浮层与 GPU 玻璃同步渐变，并遵循 Windows 关闭动画的设置。底部缩略图栏保持居中且有宽度上限。

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

需要 .NET SDK 8.0.425、MSVC x64 C++ 工具和 Windows SDK（本机为 10.0.26100.0）。本机已装 .NET 在 `.tools/dotnet`，脚本优先使用该 SDK。WinUI 依赖 Windows App SDK 2.5.1。SDK 和工具不放入源代码仓库。

```powershell
.\scripts\Build.ps1                 # 原生核心 + WinUI Release
.\scripts\Build.ps1 -Publish        # 自包含便携版 artifacts/Luma
.\scripts\Verify.ps1                # 15 项图库检查 + WIC / D3D11 / RTX 检查
.\scripts\Verify.ps1 -Photo "D:\Photos\large.jpg"
python .\tests\verify_viewer_lifecycle.py "D:\Photos\large.jpg" --runs 3
python .\tests\verify_viewer_interaction.py "D:\Photos\large.jpg"
python .\tests\benchmark_decode.py "D:\Photos\large.jpg"
.\scripts\Package.ps1 -SkipBuild    # 生成并验证未签名 MSIX
```

测试需要 Python 与 Pillow。`Generate-Assets.py` 可重新生成原创几何图标。NVIDIA SDK 按 [external/nvidia/README.md](external/nvidia/README.md) 放置；不放置也可构建，增强按钮会准确报告不可用。

实际通过的检查、亮度测量及尚未完成的实机项见 [验证记录](docs/VERIFICATION.md)。解锁后已补完全屏恢复、小窗口布局、收藏快捷键及系统注册检查；完整系统选择器交互、跨屏和休眠恢复仍需扩展验收。

MSIX 为 `artifacts/Luma_0.1.4.0_x64.msix`，便携 ZIP 为 `artifacts/Luma_0.1.4_x64.zip`。包包含实际支持格式的文件关联和开始菜单入口。本地 MSIX 未签名，不能按正式安装包直接安装；需要使用 Publisher 匹配的受信任签名证书，或后续由 Microsoft Store 签名。当前直接使用便携版。未更改系统证书信任或默认关联选择。

用户反馈、性能/体积优化和发行准备持续记录在 [待办清单](docs/TODO.md)，新增事项也统一写入此文档。

## 许可与发行

Luma 原创代码及图标使用 MIT；第三方组件适用各自独立许可，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。This software contains source code provided by NVIDIA Corporation.

NVIDIA RTX™ Video SDK 提供 HDR 和 VSR 增强。NVIDIA SDK / DLL 没有重新授予 MIT 许可；本地输出附带 NVIDIA 原始许可。正式发行前仍需完成该许可的分发条件、通知与商标使用核对，以及名称/商标检查、MSIX 签名和商店验收。项目没有公开发布。

Windows 默认应用集成依据 [Microsoft 默认程序注册文档](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs)及[默认设置 URI 文档](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings)。scRGB SDR 白电平处理依据 [Microsoft Advanced Color 文档](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)。
