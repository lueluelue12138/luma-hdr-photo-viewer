---
name: commit-and-release
description: Luma 项目的迭代交付流程：按改动验证、共同署名提交、按授权推送、维护固定预览并升级本机安装版；需要正式发布或准备 MSIX 时使用对应流程。
---

# Luma 迭代交付

在本项目完成一轮开发、提交 / 推送、更新本机版本或准备 Release 时使用。先读仓库根 `AGENTS.md`；本文中的路径相对仓库根，脚本和工作流是实际参数与行为的依据。

## 授权与交付范围

- 当前维护者已要求：每轮验证通过后提交并推送；每次应用版本更新，同时升级本机已安装版。后续“先不推送 / 等我验收”等明确指示覆盖默认流程，不重复索取已有授权。
- 推送普通分支不等于允许推送发布 tag、公开 Release 或提交微软商店。发布 tag / 公开 Release 须有对应用户请求；Store 保持用户自行提交，除非之后另行授权。
- 纯文档 / skill 修改不递增应用版本，也不重新出应用包；如果用户要求补装已有最新版，安装那份经过验证的载荷。

## 1. 确认范围与版本

1. 检查 `git status`、当前分支、remote、已有差异及相关运行进程。保留他人未提交内容，不做无关回退。
2. 将新反馈记入 `docs/TODO.md`，查阅相近验证记录。确定本轮需要的验证，不照搬全部历史测试。
3. 应用版本以 `src/Luma.App/Luma.App.csproj` 的 `Version` 为准，`packaging/AppxManifest.xml` 必须同步为 `X.Y.Z.0`。发行 tag 的基础版本必须一致。文档记录版本、测量口径和限制，不把计划写成已完成。

## 2. 验证与候选构建

- 先用增量构建检查中间改动。相关发布验证复用一个空闲的 `artifacts/Luma-candidate`；成功后维护 `artifacts/Luma/Luma.App.exe`，保留一份上一可用版，运行期间不能替换文件。完整目录一起交付，不能只复制 EXE。
- 托管逻辑 / 数据库改动运行 Core 检查；UI 改动还需实际候选 EXE 的受影响操作、主题与宽窄布局。复用 `tests/verify_*.py` 的相应入口，先看参数；夹具使用隔离图库，不能用生产数据库做写入测试。

```powershell
.\.tools\dotnet\dotnet.exe run --project tests/Luma.Tests/Luma.Tests.csproj -c Release
python tests/check_public_source.py
```

- 原生解码、HDR / Live、缩放、渲染或资源生命周期变化，选择对应格式 / 交互 / 性能回归。HDR / VSR 成功须检查真实 SDK evaluation 和实际 FP16 输出；托管 CI 无 RTX GPU，不能替代实机验证。
- 原生库必须来自本轮受验证的构建；仅在原生源码及依赖未变、来源和 hash 可核验时复用已验证组件。不要默认使用可能陈旧的 `src/Luma.Native/bin`。
- 快捷 publish 之后同样执行 `scripts/Collect-Licenses.ps1 -Output <载荷目录>`，并核对原生第三方及 NVIDIA 原始许可。不能用占位文件交付。完整出包优先使用已有 `Build.ps1` / `Build-Release.ps1`。
- `build-info.json` 要对应实际二进制的版本、源提交、dirty 状态、RTX 能力及组件来源。提交后如果旧构建仍标记 dirty，不只改元数据冒充精确构建：重新构建并补跑受影响的冒烟检查。文档提交不改变旧二进制真实来源。
- 在 `docs/TODO.md` 勾选实际完成项，`docs/VERIFICATION.md` 保存结果 / 证据路径 / 尚未覆盖项。README 和设置页只在用户可见行为有变化时同步更新。实测与估算、回调延迟与帧率分开。

## 3. 提交与推送

执行根 `AGENTS.md` 的署名检查：读取当前身份、local `luma.coAuthorName` / `luma.coAuthorEmail`；Codex 临时使用 `OpenAI Codex <codex@openai.com>`。姓名 / 邮箱取本 checkout 已确认配置，不把具体维护者写进公共规则。

```powershell
git config --show-origin --get user.name
git config --show-origin --get user.email
git config --local --get luma.coAuthorName
git config --local --get luma.coAuthorEmail
git -c user.name="OpenAI Codex" -c user.email="codex@openai.com" var GIT_AUTHOR_IDENT
git diff --staged --stat
git diff --cached --check
```

仅暂存本轮相关文件。标题描述实际结果，可沿用 `feat:` / `fix:` / `perf:` / `docs:`；复杂改动正文解释行为与必要验证，末尾空一行后写每位参与者唯一的 `Co-authored-by:`。多行提交信息写到临时文件，用 `commit -F`，不要把 shell 转义当成换行。

提交后检查 `git show -s --format=fuller HEAD` 和 `git show -s --format=%B HEAD | git interpret-trailers --parse`；修正归属须符合根规则，已推送历史不能自行重写。公开边界检查同时覆盖新增已提交对象；不要强制添加忽略的照片、数据库、转储、SDK 输入或密钥。

在已有授权范围内推送当前目标分支，使用明确 remote / branch；有新远端提交时先检查并整合，不 force push。核对远端 SHA 和对应 CI 结果。CI 失败不能报告整轮通过，修复相关失败并重新验证；纯文档迭代无需为了 CI 再本机出包。

## 4. 同步预览与本机安装

应用版本更新后的默认交付包括固定预览和本机安装；读 [本机升级流程](references/local-install.md)。使用已验证完整 RTX 包，覆盖同一安装目录，正常关闭运行窗口、备份数据、校验升级后版本 / 文件 / 数据，再重开。不能把 standard CI 包当作完整 RTX 版安装。

若安装失败，保存日志、保持 / 恢复上一份可用预览，说明哪个版本仍在本机运行；不能仅凭安装器启动就报告升级成功。需要补充验证记录时再作相关文档提交并按授权推送。

## 5. 仅在请求正式发布时

读 [Release 与 MSIX](references/release.md)。整理上个正式版至目标提交的用户可见变化，将 `docs/releases/vX.Y.Z.md` 随源代码提交后才打 tag；构建草稿、验证实际附件，再按授权公开。MSIX 构建与 Store 提交分别处理。

每次新建 Release 的版本说明都加入固定的[夸克网盘下载](https://pan.quark.cn/s/109763c10614)入口；正式发布完成后，在交付回复中明确提醒维护者手动上传本版安装包和便携 ZIP、更新该固定分享目录。该提醒属于每次发版交付，不创建定时提醒，不把 GitHub 发布成功视为网盘已同步；维护者确认或实际核对前，记录为待同步。具体文案与核对项见上述参考文档。

交付说明简短列出：完成内容、验证及限制、提交 / 推送状态、固定预览和本机实际版本；仅在真正公开后提供 Release 链接。安装 / 出包成功不代表新增 HDR 效果验证或通过商店认证。
