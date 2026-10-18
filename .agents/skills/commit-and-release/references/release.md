# 正式 Release 与 MSIX

先确认当前用户确实要求本轮正式发布。普通完成迭代、push main、本机安装不隐含创建 / 推送发布 tag 或公开 Release 的授权。

## GitHub 发布

1. 检查当前源码、上一正式 Release、所有未发布变更与尚未完成的实机事项；不要只读取最后一个 commit。同步应用 / manifest 版本、README / 设置使用说明及 `docs/releases/vX.Y.Z.md`，写用户能感知的变化、依赖与明确限制。草稿文案来自同一 tag 中的文件。
2. 完成本轮验证并提交，工作区与目标源提交干净、署名正确；推送对应分支后确认主 CI 通过。发布 tag 固定到已验证的精确提交，不移动旧 tag、不覆盖已公开版本；需要修复则递增版本。
3. 推送已授权的 `vX.Y.Z`（或明确的预发布 tag）。`.github/workflows/release.yml` 在 `v*` tag 推送时构建完整 RTX 包，执行源边界 / Core / 原生 / 包 / 隔离安装检查，然后**仅创建 GitHub Release 草稿**。tag 和 draft 都不等于已经正式公开，因此仓库 Releases 栏可能仍没有新正式版本。
4. 等待对应 workflow 实际完成，检查 tag 仍指向目标 commit。核对下载的四附件：`Luma-<version>-windows-x64-rtx-setup.exe`、`Luma-<version>-windows-x64-rtx-portable.zip`、`SHA256SUMS.txt`、`build-info.json`。来源 commit / sourceDirty=false、RTX HDR / VSR 标志、NVIDIA 输入版本与运行时 hash 应一致；核对附件校验和，实测字节数与 MiB 分开报告。
5. 用下载的消费者包做受本轮改动影响的实机验证，包括安装 / 数据保留。实际 RTX / FP16 效果证据来自支持硬件，不来自 hosted CI。没有下载 / 检查的附件不能声称本机验证过；记录限制并解决发布阻塞项。
6. 在对应公开发布授权下将草稿公开；正式版设置为非预发布 / latest，预发布保持 prerelease。检查公开可见性、目标提交、版本说明与四附件 / digest / 下载链接。发布后同步本机到这份正式安装包，再记录结果；同版本本地版不能自动冒充正式云端二进制。

当前 `.github/workflows/ci.yml` 的普通分支构建生成 standard 开发附件，不具备完整 RTX 能力；不能代替 RTX 发布工作流。NVIDIA SDK 从受限输入仓库的固定 revision / archive SHA256 获取，只读访问；密钥、archive、头文件 / lib 不属于发布资产。使用可用的已认证 GitHub 连接器 / CLI 操作，不打印凭据，不将私有数据库、照片、转储作为附件。

若工作流失败，先定位失败阶段，在相关源码 / 配置上修复并重新验证；不通过略过校验、篡改来源或把不完整 standard 包改名来完成发布。已公开 Release 的不可变内容不擅自替换。

## 微软商店

`.github/workflows/store.yml` 是手动 `workflow_dispatch`：读取真实产品身份，准备完整 RTX MSIX，运行 `verify_store_package.py` 并上传构建附件；**目前没有自动上传 / 提交 Partner Center**。推送 Release tag 不自动运行此 Store 工作流。

具体步骤读 `docs/MICROSOFT-STORE.md`，身份配置和打包参数以 `scripts/Build-Store.ps1` 为准。Name / Publisher / PublisherDisplayName / 预留显示名称取 Partner Center 的真实产品标识，不自行编造。不要提交本机私有身份文件或证书。

准备好 MSIX / 校验文件、真实授权截图、商店文案、隐私政策和审核说明；验证文件激活、WindowsApps 只读运行、HDR / VSR、升级 / 卸载、EXE 共存与数据迁移。EXE 安装验证不能替代这些。未签名本地 MSIX 不能直接当可信正式安装器使用，不擅自安装信任证书。

维护者默认自行在 Partner Center 上传并提交审核。清楚交代需要上传的文件、填写项与尚未通过的验证，不声称已上架 / 已认证。
