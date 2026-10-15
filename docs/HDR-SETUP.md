# 让 HDR 屏幕准备好看照片

如果你已经有一块好 HDR 显示器，却觉得照片的光影不太对，先看看 Windows 是否正确使用了它。Luma 的设置页把系统入口、当前屏幕信息和校准说明放在了一起。

## 1. 先开启系统 HDR

打开 Luma「设置 → Windows HDR 设置」，在 Windows「系统 → 显示」中选中目标屏幕，进入 HDR，打开「使用 HDR」。系统会决定该屏幕是否可用；远程连接、连接方式、显卡驱动和显示器模式也可能影响可用状态。

这是 Windows 的屏幕开关。照片预览中的 **RTX HDR** 则是普通照片的 AI 增强开关，两者作用不同。系统 HDR 未开启时，Luma 无法向这块屏幕输出真正的 HDR。

## 2. 使用微软的 HDR 校准工具

Luma 的「打开 HDR 校准」优先启动当前用户已安装的 Windows HDR Calibration；找不到或启动失败时打开它的 [Microsoft Store 页面](https://apps.microsoft.com/detail/9N7F2SM5D1LR)，由你自行安装。商店无法打开时尝试网页，不会自动下载安装。

这个工具需要 Windows 11 和开启 HDR 的屏幕。将校准窗口移到你要用的屏幕，在平时欣赏照片的环境光下，按提示全屏测试。依照图案刚好消失的位置调整暗部、亮部和全屏亮度，并保存配置文件；饱和度不用为了“更鲜艳”一味拉高。过多饱和度可能让颜色显得不自然。

校准帮助 Windows 更准确地描述和使用屏幕的显示范围，并不增加显示器的硬件亮度。已经正确校准、显示正常的屏幕也不必反复调整。更换屏幕或显示配置后，再检查一次较合适。操作细节以 [微软的校准指南](https://support.microsoft.com/windows/hardware/display-graphics/calibrate-your-hdr-display-using-the-windows-hdr-calibration-app) 为准。

## 3. 怎样理解设置页里的亮度

| 显示项目 | 含义 |
| --- | --- |
| Windows HDR 状态 | 此 Luma 主页窗口所在屏幕当前是否处于 HDR 模式；不是照片增强开关的状态 |
| Windows 报告的峰值 | Windows / 驱动向应用报告的小面积最高亮度，单位 nits；可能包含有效校准配置文件中的数据 |
| 当前 SDR 白色亮度 | Windows 在此 HDR 屏幕上用于呈现 SDR 白色的参考亮度，与系统的 SDR 内容亮度设置有关 |
| 全屏亮度 | 系统报告的全屏最大亮度，通常与小面积峰值不同；在校准说明展开区显示 |

这些数值**不是 Luma 用仪器测出来的**。它们不能证明屏幕已经校准，也不能单凭和宣传值一致就判断效果正确。不同测试窗口面积、显示模式、面板状态和厂商条件，都可能带来差异。出现明显不合理的值时，可检查目标屏幕、HDR 模式、驱动和校准配置文件；按实际测试图案校准，不照抄宣传数字。

页面只在 HDR 开启时将亮度作为 HDR 数据展示。未开启、系统未报告有效值或远程会话无法读取时，显示明确的未开启 / 未报告 / 不可用状态，不填入推测值。切换屏幕、返回 Luma 或点击刷新后重新读取。显示峰值不会被 RTX HDR 参数的 2000 nits 上限截断；屏幕报告和具体增强参数是两回事。

## 4. 回到照片

普通 SDR 照片可以在预览顶部「显示增强」中开启 RTX HDR，需要兼容的 NVIDIA RTX 显卡、驱动和包含官方 SDK 的构建。VSR 是另一个独立的 AI 超分开关，默认关闭。

已支持的原生 HDR 照片直接显示，不叠加 RTX HDR。带增益图的照片可以按住空格查看 SDR 底图；PQ / 线性 HDR 图片未必自带可供对照的 SDR 原图，详见 [HDR 格式说明](HDR-FORMATS.md)。

HDR 不是让整张照片一起变白。它提供更宽的明暗表现范围，实际观感也取决于内容。截图和远程桌面还可能经过 SDR 转换，不能用截图判断实际屏幕的高光亮度。

## 信息来源与实现边界

- [微软：启动 Windows 设置](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings)：使用公开的 `ms-settings:display` 显示设置入口，用户在系统内选择屏幕并切换 HDR。
- [微软：Advanced Color / HDR](https://learn.microsoft.com/windows/win32/direct3darticles/high-dynamic-range)：通过当前窗口所在输出的 `IDXGIOutput6::GetDesc1` 读取颜色空间、峰值和全屏亮度，通过 DisplayConfig 获取屏幕名称与 SDR 白色。
- [微软：显示校准管线](https://learn.microsoft.com/windows/win32/wcs/display-calibration-mhc)：校准配置文件可以包含显示器亮度元数据并影响系统报告。

设置查询在后台执行，只创建短生命周期 DXGI 查询对象，不创建 D3D 设备、交换链或 NVIDIA 上下文，不扫描相册。返回应用时刷新；窗口移动后延迟 350 ms 合并查询，隐藏设置页后停止刷新。
