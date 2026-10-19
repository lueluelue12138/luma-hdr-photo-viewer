# 商店审核备注草稿

提交时可复制下面的英文说明，并填入正式身份和可公开测试素材；这不是已经通过认证的声明。

Luma is a native WinUI 3 desktop photo viewer for Windows 11 x64. It requires no account, payment, background service or administrator elevation. The current UI is primarily Simplified Chinese.

`runFullTrust` is required for the WinUI desktop process, local user-selected folders, WIC image decoding, D3D11/FP16 rendering and compatible NVIDIA RTX Video SDK integration. It is not a request for administrator privileges. File associations are declared in the package; the user's default app remains under Windows control. No unrelated restricted capabilities are requested.

Basic review: open Luma, choose a local JPEG/PNG or import a folder, open a photo, zoom/pan, fit to window, switch photos using thumbnails, open photo details and return to the library. No login is required. Settings provides theme selection and links to Windows HDR settings and the Microsoft HDR Calibration app/Store page; the app does not change system HDR or calibration silently.

For a complete supported Live / Motion Photo, click the concentric-circle icon or press L to play/stop. Audio starts muted and the chosen sound setting persists. Apple assets need their original matching MOV; supported Android assets contain an embedded video. Windows media codecs decode the clip; some HEVC content requires the Windows HEVC extension. SDR video can use RTX HDR on compatible hardware. Native PQ/HLG/Dolby Vision video and .livp are not currently supported; the static photo remains available. VSR is suspended during playback.

Native HDR rendering requires an HDR-capable display with Windows HDR enabled. RTX HDR and VSR additionally require compatible NVIDIA RTX hardware/drivers. They are not expected to succeed on a generic virtual certification host. Native HDR is not dependent on RTX. HEIC decoding requires Windows HEIF/HEVC image extensions. These dependencies are disclosed in the listing.

HDR/AI verification must be performed on compatible hardware using the actual packaged application; a successful build or a screenshot alone does not establish HDR correctness. Review the repository's `docs/HDR-FORMATS.md` and `docs/VERIFICATION.md` for tested formats and limits.

## 2026-10-19 本机验收

- 维护者授权的临时签名副本已在真实商店身份下安装、启动、指定包打开 PNG、同身份包修订升级及卸载；原安装版 / 图库保留，临时证书与私钥已清理。原始未签名上传候选没有修改。
- 打包版原生 HDR 的 12 个夹具 / 19 条格式与 UI 结果、Live SDR 播放生命周期通过。代理远程输出不是活动 HDR，声音/UI 综合自动化用例也未完成；维护者随后明确反馈上述实屏 RTX HDR / VSR、Live HDR 与声音体验已补验通过，作为人工验收记录，保留自动化限制。
- 默认图库位置此机观察到共享 `%LOCALAPPDATA%\Luma`，四表最终逐行与静止备份一致；不把此结果外推为所有 Windows 版本的迁移保证。详细限制和失败 / 恢复记录见 `docs/VERIFICATION.md`。
- 年龄分级由维护者确认，后台为 3+ / Everyone；已保存 7 张桌面截图和确认的支持邮箱，电话 / 地址留空。未上传包、提交认证或公开发布。

## 提交前仍需完成

- 补充干净用户环境和其他系统上的 EXE / MSIX 数据位置与迁移覆盖；维护者已反馈实屏 HDR/VSR、Live HDR 与声音体验补验通过。
- 可选：用 Windows App Certification Kit 检查签名测试包并保存报告。微软已将 WACK 标记为停止维护，正式认证由 Partner Center 执行；本机基础运行验收不代替 Partner Center 的正式认证。见 [当前微软说明](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps#validate-your-app-package-locally)。
- 在 Partner Center 按真实情况填写年龄分级、市场、隐私和附加许可。只展示查看器功能时不要误报内置任意成人或暴力内容；用户自己的图片不等于应用内置内容。
- 核对 NVIDIA 现有许可下本次商店分发适用的通知、许可展示与商标要求，完成适用事项；不冒充 NVIDIA 官方应用。
- 截图与测试素材必须拥有公开展示 / 分发权限，未取得授权不上传个人照片。
