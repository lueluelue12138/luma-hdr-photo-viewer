# 构建与发布流程

公开项目为 **Luma HDR Photo Viewer**：https://github.com/lueluelue12138/luma-hdr-photo-viewer 。应用界面、数据目录和 EXE 继续使用 Luma，避免迁移已有图库。

## 当前已准备的流程

| 触发 | 行为 | 输出 |
| --- | --- | --- |
| 推送 `main` / Pull Request / 手动运行 CI | 检查完整源码历史、图库逻辑、SDK-free WIC/D3D11/FP16 路径，构建并验证安装包 | standard EXE、便携 ZIP、SHA256、构建信息；Actions 开发产物保留 14 天，不生成 Release |
| 推送与源码版本一致的 `v*` 标签 | 检查 RTX 分发前置条件，取得私有 SDK，构建 RTX 包并检查安装生命周期 | **Release 草稿**，默认不公开；EXE、ZIP、SHA256、构建信息 |

`standard` 不带 NVIDIA SDK 或运行库，不能执行 RTX HDR / VSR；仍能查看 SDR 和已支持的原生 HDR。`rtx` 必须完整带入合法取得的 SDK 和原始运行库，不会在缺少 SDK 时自动回退成 standard 发布。

目前不推送 Release 标签、不上传完整 RTX 下载包、不发布宣传。仓库公开与正式下载版发布是两个步骤。

## EXE 安装包

使用固定版本、SHA256 与发布者签名校验的 Inno Setup 7.1.0，在项目 `.tools` 目录准备编译器。支持 Windows 11 x64、当前用户安装，不需要管理员权限；建立开始菜单入口，注册实际支持格式为可选应用，不修改 `UserChoice` 或扩展名默认程序。

卸载移除安装文件和本次注册，保留 `%LOCALAPPDATA%\Luma` 图库、设置与缓存，也不删除原照片。测试使用另外编译的 Installer Test 身份、隔离安装目录与注册表命名，不替换真实 Luma 的注册。

```powershell
./scripts/Build-Release.ps1 -Variant standard -TestInstaller
python tests/verify_release.py --variant standard
python tests/verify_installer.py

# 仅本地开发候选；未完成许可发布检查时不要分发 RTX 包
./scripts/Build-Release.ps1 -Variant rtx
python tests/verify_release.py --variant rtx
```

输出文件示例：`Luma-0.1.16-windows-x64-standard-setup.exe`、`Luma-0.1.16-windows-x64-standard-portable.zip`。构建信息明确标识标准/RTX 能力、源码版本和提交。EXE 当前未做应用代码签名；编译器本身的签名校验不等于 Luma 安装包签名。

## 完整 RTX 发布之前

1. 核对本次实际下载的 `NVIDIA_RTX_Video_SDK_License.pdf` 与补充条款，包括 object-code 分发、至少同等保护的下游条款、通知及商标使用要求；确认受控 CI 使用 SDK 的方式。源码 MIT 不替代 NVIDIA 的许可。
2. 将审阅后的用户许可写入 `packaging/RTX-EULA.rtf`，随安装向用户展示。当前没有用未经审阅的占位条款冒充这份文件。
3. 使用获准的私有 SDK 存储，配置仓库 **secret** `NVIDIA_SDK_URL`，和 **variable** `NVIDIA_SDK_SHA256`。URL 应为有效的 HTTPS 私有下载地址；脚本校验 SHA256、解压路径和体积。SDK 的头文件、库、样例、文档和 ZIP 均不上传为公开产物。
4. 以上事项完成后，维护者明确设置 **variable** `RTX_DISTRIBUTION_READY=true`。默认未设置；缺失或未完成时工作流会拒绝 RTX 分发，不能绕过后静默发布基础版。
5. 在真实 RTX / HDR 设备完成与本次标签对应的 HDR-only、VSR-only、组合、原图对照与生命周期检查。托管 CI 检查构建、文件和安装，不证明 AI 效果。
6. 更新项目版本和 MSIX 版本，完成合适的测试、提交及共同作者核对，再推送标签。例：项目 `0.1.17`、MSIX `0.1.17.0` 对应 `v0.1.17` 或 `v0.1.17-beta.1`。不一致会失败。
7. 核对生成的 Release 草稿、能力说明、签名状态、SHA256、安装/升级/卸载、公开样例截图与说明，再决定是否公开。工作流拒绝覆盖已有公开 Release。

NVIDIA 通知、商标使用提交、购买签名证书和公开草稿均需按实际发布决定进行；本轮不会代发外部邮件或提交未授权的发布资料。

## 后续 Store 路线

保留现有 `scripts/Package.ps1` 的 MSIX 构建路径；Store 身份、签名、共享运行库、认证和完整默认应用验收稍后实施。GitHub EXE 下载版和 Microsoft Store 的 MSIX 是独立发行方式，不要求在本轮同时完成。

参考：[GitHub 标签触发](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[GitHub Release 草稿](https://docs.github.com/en/rest/releases/releases#create-a-release)、[Inno Setup 当前用户安装](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)。NVIDIA 条款以自行下载的实际原始许可为准。
