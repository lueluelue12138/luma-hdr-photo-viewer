# 微软商店上架准备

2026-10-19 通过已登录的 Partner Center 复核：**Luma HDR Photo Viewer 仍是 Submission 1 草稿，尚未提交审核或发布。** GitHub 正式版现为 0.1.31，与商店发布独立。

## 当前后台进度与下一步

- 属性已完成，项目网址已更新为上线官网；隐私政策与 Issues 支持地址保留。电话 / 地址位于 **Support info (optional)**，不是本页必填项，不需要为了消除草稿状态公开私人住址。
- 简体中文描述、简短描述、6 项功能、7 个关键词与附加许可已存在；本次补上相册置顶 / 排序和多选筛选，重新打开确认已保存。桌面截图仍为 **0 张**，因此商店介绍仍未完成。
- 年龄问卷停在 3+ / Everyone 预览；**IARC 条款及本人法定成年声明未勾选，保存按钮禁用**。这一项由维护者阅读并确认。
- 定价是 **CNY 0、全球所有市场（当前 240 个）**；提交选项仍为 **除非我选择“立即发布”，否则不发布此提交**，审核通过后也不会自动公开。
- 正式包身份与本地忽略文件 `artifacts/store-identity.json` 一致。已配置四项 GitHub Variables，并从固定标签 **v0.1.31 / 34fce1f** 成功运行 [MSIX 构建 39221698142](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/39221698142)。云端完成完整 RTX 构建和源载荷 / MSIX 校验；已下载到 `artifacts/store-progress-20261019/candidate/Luma_0.1.31.0_x64.msix`，87,509,471 字节（83.46 MiB），本地身份 / SHA256 / 包完整性与 3,444 个内容块哈希核对通过。它仍是未签名候选，不是商店上传或认证。
- 真实商店身份的侧载安装、文件激活、更新 / 卸载、EXE 共存和数据位置仍未验证，候选未上传；不能拿 EXE 版安装验证或云端出包成功替代上述验收。原始未编辑看图 PNG 已准备到 `artifacts/store-assets/Luma-Viewer-SDR.png`，未上传。

## 维护者可直接打开的页面

| 页面 | 当前需要处理的内容 |
| --- | --- |
| [提交概述](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/overview) | 查看总体进度，全部就绪后由维护者提交认证 |
| [年龄分级](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/ageratings) | 核对分级，亲自确认 IARC 条款及成年声明，再保存 |
| [中文商店介绍与截图](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/listings?languageid=262&languagecode=zh-hans) | 至少上传一张获准公开的真实 PNG 界面截图；介绍已填 |
| [程序包](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/packages) | 候选实际验收后上传 .msix，不能上传 Actions 下载容器 .zip |
| [属性与可选联系方式](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/properties) | 检查官网 / 隐私 / 支持地址；电话和地址可选 |
| [定价和可用性](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/availability) | 核对免费、销售地区和可见性，现有设置未改 |
| [提交选项](https://partner.microsoft.com/zh-cn/dashboard/products/9NLL2F0S28TQ/submissions/1152921505702145177/options) | 维持审核后手动发布 |

本轮只保存草稿与准备候选，没有接受 IARC 协议、填写个人声明、上传照片 / 程序包、提交认证或公开商店。后台截图和配置 / 构建记录位于忽略目录 `artifacts/store-progress-20261019/`。

## 推荐路径

GitHub 继续提供完整 RTX EXE 安装包与便携 ZIP；微软商店采用 **MSIX**。两者使用相同应用能力，首轮保留自包含运行库，不为了包小引入额外首次下载或裁剪风险。

MSIX 可由商店负责签名、托管和更新，不需要先购买商业代码签名证书。商店也接受传统 EXE/MSI，但该路径需自行提供受信任的 Authenticode 签名和安装包下载地址；当前 GitHub 未签名 EXE 不能直接作为合格的 EXE 商店提交。[微软包要求](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)。

## 需要维护者先完成

1. 开发者账户已由维护者注册；如后台另有身份验证 / 开发者协议待办，按账户提示完成即可，不提供密码或验证码。
2. 应用名称与产品身份已就绪，后续无需再次新建产品；预留并不等于上架。
3. 本地 `artifacts/store-identity.json` 已记录 Product identity 的四项真实值。它们不是登录密码；发布者个人资料由维护者确认，不能猜测 Publisher GUID 或改用示例值。
4. 确认发布者展示名称、联系渠道、免费定价、销售地区和年龄分级答案，以及有权公开的示例照片与真实截图。

微软要求 [名称预留](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name) 与实际身份匹配。当前仓库中的 `Luma.PhotoViewer / CN=Luma Development` 仅用于本地开发，不能直接提交为该账户的 Store 身份。

## 已有准备

- `scripts/Build-Store.ps1`：从完整 RTX 载荷复制到独立 staging，用正式身份生成清单，MakeAppx 校验、MSIX、SHA256 和来源记录；不改写 GitHub 载荷、不安装证书、不上传。
- `tests/verify_store_package.py`：逐文件核对 MSIX 与原载荷，检查 13 种格式关联、原始 NVIDIA DLL/PDF/条款、包身份、资源与校验和；不能替代 WACK 或运行时验收。
- 0.1.27 已从云端消费者载荷实际生成开发身份 MSIX：87,451,022 字节（83.40 MiB），504 文件与 13 项格式关联核对通过。它仍是 `CN=Luma Development`，未签名、未安装，不能作为正式商店身份包上传；四项真实身份现已获取；0.1.31 正式身份候选已在上方工作流生成，实际身份安装 / 运行测试尚未执行。
- `.github/workflows/store.yml`：手动构建 Store 候选，只保存 MSIX / 校验和 / 来源三类 Actions 产物，不自动提交或公开。缺少真实 Store 身份时直接失败。
- [隐私说明](../PRIVACY.md)、[商店中文文案](store/LISTING.zh-CN.md)、[审核备注与测试清单](store/CERTIFICATION.md)。应用仍以简体中文为主，不把英文介绍等同于完整英文 UI。
- 现有透明原始 logo 位于 `branding/Luma.png`，包内已有启动 / 磁贴图标。商店 300×300 tile 可由 `scripts/Prepare-StoreAssets.py` 生成；真实界面截图仍需准备。

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

在 **Actions → Prepare Microsoft Store MSIX (no submission) → Run workflow** 手动运行，选择版本标签 `v0.1.31`，下载 `Store-MSIX-<提交>` 产物。若页面只提供分支，可使用 `gh workflow run store.yml --ref v0.1.31` 指定标签。完成实际安装 / 运行测试后再到 Partner Center 新建提交。当前无需 Azure 凭据、商业签名证书或 Store API 密钥。

**推送 Release 标签只会构建 EXE / ZIP，不会顺带构建 MSIX。** Store 工作流是独立的手动出包，未配置真实身份时会停止；即使出包成功，也不会自动上传商店、提交认证或公开。以后更新商店版仍需构建更高版本 MSIX，并在同一产品里新建提交；不要为每次更新新建产品或更换包身份。

## 尚不能省略的验收

开发身份包生成成功，只说明清单和包内容校验通过。必须在实际商店身份下验证 WindowsApps 只读安装目录、独立启动、从资源管理器打开照片、默认应用入口、HDR/VSR、缓存、更新及卸载。尤其 EXE→MSIX 的图库迁移 / 共存与数据虚拟化尚未实测，不承诺自动共用原图库，也不把 EXE 升级测试当成 MSIX 升级证明。

商店至少需要一张合规桌面截图，建议准备 4–5 张：PNG、建议 1366×768 或更高、单张不超过 50 MB，使用有授权的照片和真实界面，不能用私人截图或产品示意图替代。[官方图片要求](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/screenshots-and-images)。

之后填写 [提交资料](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/create-app-submission)、上传实际身份 MSIX、附加运行库许可并说明 `runFullTrust` 的用途。微软当前文档已将 **WACK 标记为停止维护的可选本地检查**，不再把安装或通过 WACK 列作本项目的硬性提交门槛；如使用仍可保存报告，正式认证由 Partner Center 处理。[微软说明](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps#validate-your-app-package-locally)。实际打包运行、关联、更新和数据保留验证仍需完成。账户协议及最终分级声明由维护者确认。
