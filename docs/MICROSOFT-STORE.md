# 微软商店提交与发布

2026-10-20 维护者确认已在 Partner Center 提交首版：**Luma HDR Photo Viewer / Submission 1 / 0.1.31.0 已提交，等待认证结果；尚未确认通过审核或公开上架。** 本次按维护者反馈更新文档，未重新操作后台或代为提交。GitHub 正式版现为 0.1.32，与商店发布独立；国内用户另可使用维护者手动更新的[夸克网盘下载](https://pan.quark.cn/s/109763c10614)。

## 当前后台进度与下一步

- 属性已完成，项目网址已更新为上线官网；隐私政策保留；支持联系字段已按维护者确认改为可实际收信的邮箱，保存后重开核对。电话 / 地址位于 **Support info (optional)**，不是本页必填项，不需要为了消除草稿状态公开私人住址。
- 原有“中文（简体）/ zh-Hans”介绍包含维护者上传的完整展示截图，以及描述、简短描述、6 项功能、关键词与附加许可；相册置顶 / 排序和多选筛选已补充。不要因安装包带出其他语言就要求维护者重复上传这套素材，语言管理说明见下方。
- 维护者已亲自确认年龄分级；后台已进入分级结果页，IARC / PEGI 为 **3+**、ESRB 为 **Everyone**。
- 最后核对的定价是 **CNY 0、全球所有市场（当时 240 个）**；提交选项为 **除非我选择“立即发布”，否则不发布此提交**。若维护者未改变该选项，审核通过后仍需在后台选择“立即发布”。
- 正式包身份与本地忽略文件 `artifacts/store-identity.json` 一致。已配置四项 GitHub Variables，并从固定标签 **v0.1.31 / 34fce1f** 成功运行 [MSIX 构建 39221698142](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/39221698142)。云端完成完整 RTX 构建和源载荷 / MSIX 校验；已下载到 `artifacts/store-progress-20261019/candidate/Luma_0.1.31.0_x64.msix`，87,509,471 字节（83.46 MiB），本地身份 / SHA256 / 包完整性与 3,444 个内容块哈希核对通过。该本地原始文件仍未签名；后续上传由维护者完成，商店包校验不等于认证通过。
- 取得一次性测试签名授权后，真实商店身份的安装、启动、指定包文件关联打开、测试修订升级与卸载已实测；原生 HDR 的 12 个夹具 / 19 条结果及 Live SDR 生命周期通过。原图库四表逐行核对保留，临时安装 / 信任 / 私钥已清理。随后维护者明确确认实屏 RTX HDR / VSR、Live HDR 和声音综合体验已补验通过；作为人工验收记录，代理远程测试限制保留，详见 `docs/VERIFICATION.md`。原始上传文件 hash 不变，程序包上传与最终提交由维护者完成。

上述云端 / 本机验证是在提交前完成的历史记录；随后维护者上传同一原始 MSIX，后台显示 **Validated**，并于 2026-10-20 确认已提交。包校验通过与正式认证是不同阶段。接下来在提交概述查看结果，收到问题时按报告修正；公开上架确认后才加入商店下载入口。

## 当前更新文件（0.1.32）

2026-10-21 已发布 GitHub 正式版 0.1.32，同一固定源码 `881361c` 的完整 RTX 正式载荷已生成 **0.1.32.0** 商店身份 MSIX；包身份与此前的 0.1.31.0 一致，版本递增。505 个包内文件 / 13 个关联 / 原始 NVIDIA 许可 / SHA256 校验通过。此更新包尚未由代理上传或提交；本轮未重复安装测试签名 MSIX 或修改证书信任。

| 用途 | 本机位置 | 实际大小 |
| --- | --- | --- |
| EXE 安装版 | `artifacts/release-0.1.32/Luma-0.1.32-windows-x64-rtx-setup.exe` | 58.36 MiB |
| 便携版 | `artifacts/release-0.1.32/Luma-0.1.32-windows-x64-rtx-portable.zip` | 81.86 MiB |
| 商店上传包 | `artifacts/release-0.1.32/Luma_0.1.32.0_x64.msix` | 83.47 MiB |

微软商店的 MSIX 更新提交通道只上传第三份原始未签名包；EXE / ZIP 是直接分发文件，不上传临时签名副本或测试修订 0.1.31.1。三份文件集中在 `artifacts/release-0.1.32/`，EXE / ZIP 使用 `SHA256SUMS.txt`，商店包使用 `MSIX-SHA256SUMS.txt`。前面的 0.1.31.0 云端构建 / 首次提交记录保留为历史，不表示 0.1.32.0 已经上传。

## 维护者可直接打开的页面

| 页面 | 当前需要处理的内容 |
| --- | --- |
| [提交概述](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/overview) | 已由维护者提交；查看认证进度、结果及发布状态 |
| [年龄分级](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/ageratings) | 已由维护者确认；查看 3+ / Everyone 结果 |
| [中文商店介绍与截图](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/listings?languageid=262&languagecode=zh-hans) | 原有完整展示素材；位于“其他 Store 一览语言”分组 |
| [程序包](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/packages) | 维护者上传的 0.1.31.0 包已 Validated，随后已提交 |
| [属性与可选联系方式](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/properties) | 检查官网 / 隐私 / 支持地址；电话和地址可选 |
| [定价和可用性](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/availability) | 核对免费、销售地区和可见性，现有设置未改 |
| [提交选项](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/options) | 维持审核后手动发布 |

联系邮箱与本地测试记录保留；年龄声明、真实截图、原始 MSIX 上传和最终提交由维护者完成。代理没有代接受声明或提交认证；本次提交状态来自维护者反馈。后台 / 构建记录在忽略目录 `artifacts/store-progress-20261019/`，运行与清理证据在 `artifacts/store-runtime-20261019/`。

## 语言与 runFullTrust 说明

- “软件包中支持的语言”由 manifest 的 `zh-CN` / `en-US` 声明带出；“其他 Store 一览语言”中的 `zh-Hans` 是原先独立添加的简体中文商店介绍。这些都是同一产品的不同介绍，不等同于已经完成对应界面翻译。
- 商店至少完成一份语言介绍即可；不准备提供的介绍语言可在语言管理页移除，包括安装包带出的条目。此操作只管理商店介绍，不修改安装包语言。优先保留维护者已完善的 `zh-Hans` 素材，避免重复维护；未复核后台时不声称维护者已执行移除。[微软语言管理说明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info#store-listing-languages)。
- `runFullTrust` 用途字段最多 **500 字符**，不是 500 个英文单词。下列说明在限制内；提交时以实际字段校验为准：

  > Luma is a WinUI 3 desktop photo viewer. runFullTrust is required to run its packaged Win32 process and use native image decoding, Windows media playback, Direct3D HDR rendering and NVIDIA RTX Video SDK. It accesses user-selected photos and folders and stores its library and settings locally. No administrator elevation, background service, account or photo upload is required.

## 推荐路径

GitHub 继续提供完整 RTX EXE 安装包与便携 ZIP；微软商店采用 **MSIX**。两者使用相同应用能力，首轮保留自包含运行库，不为了包小引入额外首次下载或裁剪风险。

MSIX 可由商店负责签名、托管和更新，不需要先购买商业代码签名证书。商店也接受传统 EXE/MSI，但该路径需自行提供受信任的 Authenticode 签名和安装包下载地址；当前 GitHub 未签名 EXE 不能直接作为合格的 EXE 商店提交。[微软包要求](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)。

## 后续更新沿用的账户与身份

1. 开发者账户已由维护者注册；如后台另有身份验证 / 开发者协议待办，按账户提示完成即可，不提供密码或验证码。
2. 应用名称与产品身份已就绪，后续无需再次新建产品；预留并不等于上架。
3. 本地 `artifacts/store-identity.json` 已记录 Product identity 的四项真实值。它们不是登录密码；发布者个人资料由维护者确认，不能猜测 Publisher GUID 或改用示例值。
4. 确认发布者展示名称、联系渠道、免费定价、销售地区和年龄分级答案，以及有权公开的示例照片与真实截图。

微软要求 [名称预留](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name) 与实际身份匹配。当前仓库中的 `Luma.PhotoViewer / CN=Luma Development` 仅用于本地开发，不能直接提交为该账户的 Store 身份。

## 已有准备

- `scripts/Build-Store.ps1`：从完整 RTX 载荷复制到独立 staging，用正式身份生成清单，MakeAppx 校验、MSIX、SHA256 和来源记录；不改写 GitHub 载荷、不安装证书、不上传。
- `tests/verify_store_package.py`：逐文件核对 MSIX 与原载荷，检查 13 种格式关联、原始 NVIDIA DLL/PDF/条款、包身份、资源与校验和；不能替代 WACK 或运行时验收。
- 0.1.27 已从云端消费者载荷实际生成开发身份 MSIX：87,451,022 字节（83.40 MiB），504 文件与 13 项格式关联核对通过。它仍是 `CN=Luma Development`，未签名、未安装，不能作为正式商店身份包上传；四项真实身份现已获取；0.1.31 正式身份候选已在上方工作流生成，本次实际身份安装 / 运行测试结果见上文。
- `.github/workflows/store.yml`：手动构建 Store 候选，只保存 MSIX / 校验和 / 来源三类 Actions 产物，不自动提交或公开。缺少真实 Store 身份时直接失败。
- [隐私说明](../PRIVACY.md)、[商店中文文案](store/LISTING.zh-CN.md)、[审核备注与测试清单](store/CERTIFICATION.md)。应用仍以简体中文为主，不把英文介绍等同于完整英文 UI。
- 现有透明原始 logo 位于 `branding/Luma.png`，包内已有启动 / 磁贴图标。商店 300×300 tile 可由 `scripts/Prepare-StoreAssets.py` 生成；维护者已上传真实桌面截图。上方竖版 / 方形封面为可选宣传素材；下方 300×300 / 150×150 / 71×71 为商店磁贴图标，可用 Luma Logo，不上传时回退到包内图标。现有 `artifacts/store-assets/Luma-StoreTile-300.png` 可供维护者选用。

## 本地打包

```powershell
# 现有、已验证的完整 RTX 载荷 → 开发身份 MSIX；不能直接上传商店
./scripts/Build-Store.ps1 -SkipBuild -OutputDirectory "$PWD/artifacts/store/dev-candidate"
python tests/verify_store_package.py artifacts/store/dev-candidate --source-payload artifacts/release-payload-rtx

# 用实际 Product identity 填好 JSON；需干净已提交源码
# 默认重建完整 RTX 载荷，不把旧二进制标记为新提交
./scripts/Build-Store.ps1 -ForStore -IdentityPath artifacts/store-identity.json -OutputDirectory "$PWD/artifacts/store/submission"
python tests/verify_store_package.py artifacts/store/submission --source-payload artifacts/release-payload-rtx --require-store-identity

python scripts/Prepare-StoreAssets.py
```

`-ForStore` 的含义是生成真实身份的**提交候选**，不是已经通过审核。MSIX 仍未签名：商店会在认证发布时签名；自行侧载测试要另外用匹配身份的开发证书签名并在测试机信任。不能要求普通用户安装开发证书来替代正式商店发布。

## 云端出包

已有 NVIDIA 私有 SDK 配置保持不变；增加非敏感仓库 Variables：

| Variable | 来源 |
| --- | --- |
| `STORE_PACKAGE_NAME` | Product identity 的 Name |
| `STORE_PUBLISHER` | Product identity 的 Publisher（完整 `CN=...`） |
| `STORE_PUBLISHER_DISPLAY_NAME` | PublisherDisplayName |
| `STORE_DISPLAY_NAME` | 实际预留的名称 |

操作位置为 GitHub 仓库 **Settings → Secrets and variables → Actions → Variables → New repository variable**。这四项是产品身份，不是密码，不需要填进 Secrets。NVIDIA 私有 SDK 的 Secret 保持原样，不复制出来。

后续更新时，在 **Actions → Prepare Microsoft Store MSIX (no submission) → Run workflow** 手动运行并选择目标版本标签，下载 `Store-MSIX-<提交>` 产物。首版使用 `v0.1.31`；若页面只提供分支，可使用 `gh workflow run store.yml --ref <目标标签>` 指定标签。验收后由维护者上传到同一产品的新提交。当前无需 Azure 凭据、商业签名证书或 Store API 密钥。

**推送 Release 标签只会构建 EXE / ZIP，不会顺带构建 MSIX。** Store 工作流是独立的手动出包，未配置真实身份时会停止；即使出包成功，也不会自动上传商店、提交认证或公开。以后更新商店版仍需构建更高版本 MSIX，并在同一产品里新建提交；不要为每次更新新建产品或更换包身份。

## 尚不能省略的验收

正式身份侧载基础流程已完成，详见本次验证记录；测试 `.0 → .1` 仅改变本地副本的包修订号，应用二进制仍为 0.1.31，不是新的公开版本。当前机器观察到默认图库写入共享 `%LOCALAPPDATA%\Luma`，不把单机结果推广为所有系统的迁移保证。上述实屏 RTX HDR / VSR、Live HDR 和声音体验已由维护者反馈补验通过；自动化用例的远程环境限制仍保留，后续继续覆盖干净用户和其他 Windows 环境。首版原始 `.msix` 已由维护者上传并通过后台包校验，随后提交审核。

商店至少需要一张合规桌面截图，建议准备 4–5 张：PNG、建议 1366×768 或更高、单张不超过 50 MB，使用有授权的照片和真实界面，不能用私人截图或产品示意图替代。[官方图片要求](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/screenshots-and-images)。

后续更新继续按 [提交资料](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/create-app-submission)、上传实际身份 MSIX、附加运行库许可并说明 `runFullTrust` 的用途。微软当前文档已将 **WACK 标记为停止维护的可选本地检查**，不再把安装或通过 WACK 列作本项目的硬性提交门槛；如使用仍可保存报告，正式认证由 Partner Center 处理。[微软说明](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps#validate-your-app-package-locally)。本机基础验收不代表微软已认证；账户协议与最终提交仍由维护者确认。
