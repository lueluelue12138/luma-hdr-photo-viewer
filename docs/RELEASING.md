# 构建与发布流程

公开项目为 **Luma HDR Photo Viewer**：https://github.com/lueluelue12138/luma-hdr-photo-viewer 。应用界面、数据目录和 EXE 继续使用 Luma。

## 自动流程

| 触发 | 行为 | 输出 |
| --- | --- | --- |
| 推送 `main` / Pull Request / 手动运行 CI | 源码边界、图库逻辑、SDK-free WIC/D3D11/FP16、安装生命周期检查 | standard EXE、便携 ZIP、SHA256、构建信息；Actions 产物保留 14 天 |
| 推送与源码版本一致的 `v*` 标签 | 精确检出源码和私有 SDK、校验 SDK、编译完整 RTX 应用、核对包及隔离安装 | 完整 **RTX EXE、便携 ZIP、SHA256、构建信息**，自动上传到 **Release 草稿** |

`standard` 不包含 NVIDIA 运行库，不能执行 RTX HDR/VSR。`rtx` 必须真正以官方 SDK 编译，并包含原始 TrueHDR/VSR release DLL、独立运行库条款和原始许可 PDF；SDK 缺失或验证失败时直接失败，不降级发布 standard。

首次 `v0.1.16` 标签检出成功，但旧流程在人工分发就绪变量处停止。0.1.17 将完整包的构建和私有草稿上传接通，不再用公开发行审批作为编译前置条件。原 `v0.1.16` 标签保持指向；新流程使用新版本标签验证。

## SDK 构建输入与权限

维护者已取得的 RTX Video SDK 1.1.0 原包保存在其**私有构建依赖仓库**，不放进公开 Luma 仓库。发布 CI 使用专用的 **只读 deploy key**，仅能读取该私有仓库，不能写入仓库，也不使用维护者的个人访问令牌。SDK 输入同时固定 Git 提交和 ZIP SHA256；编译机只进行内部构建，不对外提供 SDK 云服务。

仓库一次性配置如下：

| 类型 | 名称 | 含义 |
| --- | --- | --- |
| Secret | `NVIDIA_SDK_DEPLOY_KEY` | 专用只读 SSH 私钥 |
| Variable | `NVIDIA_SDK_REPOSITORY` | 私有构建输入仓库的 `owner/name` |
| Variable | `NVIDIA_SDK_REVISION` | 输入仓库的精确 40 位提交 SHA |
| Variable | `NVIDIA_SDK_SHA256` | 原始 SDK ZIP 的 SHA256 |

当前官方 1.1.0 ZIP 的 SHA256 为 `abf4f34e2b5a618e355b0d5a0365d8ecc3db4396e756e4c850a867e1ae2ed69e`。`Install-NvidiaSdk.ps1` 检查大小、SHA256、解压路径、符号链接和展开预算。公开上传白名单只有安装包、便携 ZIP、SHA256 和构建信息；SDK ZIP、头文件、库、样例、开发工具、密钥和原照片均不上传。

更换 SDK 时更新私有输入提交与 SHA256，再验证对应版本。轮换 deploy key 时先添加新的只读公钥、更新 secret，成功后移除旧公钥。保持依赖仓库私有，不把私钥写入源码或日志。

## EXE 与便携包

使用固定版本、SHA256 与发布者签名核对的 Inno Setup 7.1.0；Windows 11 x64、当前用户安装、不需要管理员权限。建立开始菜单入口、注册实际支持格式为可选查看器，不修改 `UserChoice` 或扩展名默认程序。卸载保留本地图库、设置、缓存和原照片；另一个便携副本重新注册后的归属也保留。

RTX 安装器显示 `packaging/RTX-EULA.rtf`，并提供按钮在接受前打开**原始 NVIDIA 许可 PDF**。相同运行库条款位于便携包的 `licenses/NVIDIA-runtime-terms.rtf`；MIT 仅覆盖 Luma 原创代码和图标。原始 PDF 与 DLL 保持字节不变。

```powershell
# 普通基础版
./scripts/Build-Release.ps1 -Variant standard -TestInstaller
python tests/verify_release.py --variant standard
python tests/verify_installer.py

# 已按 external/nvidia/README.md 放置官方 SDK 的完整本地候选
./scripts/Build-Release.ps1 -Variant rtx -Tag v0.1.17 -TestInstaller
python tests/verify_release.py --variant rtx
python tests/verify_installer.py
```

输出为 `Luma-0.1.17-windows-x64-rtx-setup.exe`、`Luma-0.1.17-windows-x64-rtx-portable.zip`、`SHA256SUMS.txt` 和 `build-info.json`。构建信息记录版本、源码提交、是否干净、RTX 能力、SDK 输入版本/哈希及原始运行库/许可哈希。`-Distribution` 要求已提交且干净的源码，不代表代替 NVIDIA 的许可或完成正式公开发行审批。

目前安装包与应用未做代码签名；编译器签名核对不等于应用已签名。完整包为离线自包含，用户不需下载开发 SDK、.NET SDK 或额外配置私有仓库；实际 RTX 功能仍需要兼容 GPU、驱动和开启 HDR 的屏幕。

## 标签与 Release

1. 完成合适验证，提交代码与文档，并核对工具作者及人类共同署名。
2. 项目版本、MSIX 版本和标签基础版本一致。例如 `0.1.17` / `0.1.17.0` 对应 `v0.1.17` 或 `v0.1.17-beta.1`。
3. 推送附注标签；工作流构建并上传完整 RTX 包到 Release 草稿。包名、能力和源码提交不匹配会失败，已公开的 Release 不会被覆盖。
4. 在真实 RTX/HDR 设备使用该云端包验证 HDR-only、VSR-only、组合、原图对照、安装与窗口生命周期。托管编译机没有 RTX 显卡，它的绿色检查不证明 AI 效果。
5. 公开下载和宣传之前，核对原始 NVIDIA 许可及补充条款适用于本项目的分发、商业发布通知、商标使用等要求；完成必要事项后再决定公开草稿。构建出可用私有候选不等于 NVIDIA 已批准商标或商业发行。没有代发 NVIDIA 通知或提交未经授权的宣传材料。

原标签不静默移动。若要修复标签中的源码/许可，提交后使用新版本标签；仅外部配置或临时网络失败可重跑原任务。

## Store 路线

保留 `scripts/Package.ps1` 的 MSIX 构建。Store 身份、签名、共享运行库、认证与正式默认应用验收继续待办；本轮不上传 Microsoft Store。

参考：[GitHub deploy key](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/managing-deploy-keys)、[标签触发](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[Release 草稿](https://docs.github.com/en/rest/releases/releases#create-a-release)、[Inno Setup 当前用户安装](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)。NVIDIA 条款以维护者实际下载、随包保留的原始许可为准。
