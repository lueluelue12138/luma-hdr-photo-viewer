# Luma 本机验证记录

## 转换保真与绝对亮度修正（2026-10-29，转换器 0.1.1 / Luma 0.1.33）

维护者提供同一张 JXR 与旧转换 JPG，确认原 JXR 在 Luma 与 Windows 照片观感一致、旧 JPG 在 Luma 差异明显；在小米 14 Ultra / HyperOS 3 的新相册中也几乎未见 HDR 效果。维护者随后明确要求以原片数据为准，不针对手机品牌适配，不扩展原片峰值。

`tests/compare_hdr_conversion.py` 分别核对解码的绝对 scRGB、实际 GPU FP16 自动显示与统一参考白下的完整增益重建。该屏幕本轮报告峰值 1600 nits、SDR 白色 480 nits。旧文件完整重建的亮度比例中位数约 0.9925，但默认 gain capacity 为 49.261（10000 / 203）；自动显示的比例中位数变为 1.5035，RGB 通道平均绝对误差约 48.09 nits，高光亮度 99 分位从原图约 930.45 变成 667.27 nits。主因是 JXR 绝对亮度与增益图屏幕相对亮度两条显示路径不同；少量平色端点和完整重建检查无法覆盖此问题。证据 `artifacts/hdr-converter-fidelity/before.json`。

转换器不读取屏幕亮度，仍按 scRGB 80 nits 与库的 203 nits 参考换算。目标容量改为原片实际 Rec.2020 RGB 通道峰值（本图约 1007.99 nits，即 capacity 约 4.96547），不再默认 10000 nits。新路径先形成质量 100 / 4:4:4 的 Display P3 SDR JPEG，再由原 HDR 和实际压缩底图生成质量 100 / 全尺寸 RGB 增益图，避免 HDR-only API 强制 realtime 与压缩前底图比值的误差；XMP + ISO 格式继续保留。超出 Rec.2020 或 10000 nits 的输入拒绝输出，不能静默裁切后宣称保真。JPEG / 8 位增益量化仍有误差。

EXIF ImageDescription 以精确的 `Luma Absolute HDR v1; ReferenceWhiteNits=203; Source=scRGB80` 声明来源，Luma 只对这个有界、完整的标记以模型 3 保留绝对亮度。标准完整增益重建与 SDR 对照不变，未标记的手机 / 相机照片继续使用原自适应模式。它是可选来源扩展，普通 Android 等读取器仍遵循标准增益图自行适配，不能承诺其他软件或手机必然与 JXR 观感相同。规范依据：[Android Ultra HDR 显示适配](https://developer.android.com/media/platform/hdr-image-format)。

修正后同图、相同自动显示环境，真实 FP16 读回：亮度比例 5 / 50 / 95 分位约 0.98998 / 1.00502 / 1.02084；RGB 通道平均绝对误差约 1.669 nits、相对误差中位数 / 95 分位约 1.014% / 2.584%。`--require-close` 的亮度 5–95 分位在 ±3%、RGB 相对 95 分位低于 4%、平均通道误差低于 2 nits 检查通过。这是 960 像素宽实际预览的量化结果；全分辨率抽样 RGB 相对 95 分位约 6.69%，不声称逐像素无损或手机实际观感验收。证据 `artifacts/hdr-converter-fidelity/after.json`、`comparison.log`。

原生 RTX 候选来自本轮 `Build-Native.ps1 -RequireNvidia -TestFixtures`；原有 HDR 峰值及原生手动调节回归通过，不因本修改运行 NVIDIA 增强。主应用增量构建 0 警告 / 0 错误。转换器合成端点、奇数尺寸、EXIF 方向、取消 / 同名安全、普通 SDR / 损坏拒绝、宽色域、拒绝静默裁切以及来源标记完整 / 截断 / 无效边界检查通过；Pillow / NumPy 独立公式、4:4:4 采样与实际源峰值容量检查通过。证据 `artifacts/hdr-converter-fidelity-build.log`、`peak-regression.log`、`tuning-regression.log`。只读原片与旧输出，用户照片不进入提交或软件载荷。

交付载荷从已提交实现 `041f30e9418b438cb09a4dcf05306269fa08360c` 重新构建，build-info 保留工作区仍有无关商店文档修改的真实 dirty 状态。固定转换器 `artifacts/HdrJpeg/Luma.HdrConverter.exe` 0.1.1，1,485,824 字节；ZIP `artifacts/Luma-HDR-Converter-0.1.1-windows-x64.zip` 641,929 字节，SHA256 `d29581b965e94a5f704ce70c6fad6a9386f60d5f4392671c9a6a125a274fcd64`，完整 ZIP 与 8 个消费者文件逐字节核对通过。0.1.0 完整载荷保留在 `artifacts/HdrJpeg-previous`。在用户原有 `Ultra HDR` 文件夹另存 `DSC06718_保真版_0.1.1.jpg`（5,266,607 字节），没有覆盖原片或旧输出。

Luma 固定预览同步 0.1.33，上一 0.1.32 完整预览保留在 `artifacts/Luma-previous`。本机正常关闭两个旧预览窗口后，同目录安装退出码 0，注册版号与 EXE 均为 0.1.33，503 个消费者文件匹配新载荷；NVIDIA 两个运行时、原始 PDF 与条款核对通过。升级前后 SQLite backup API 一致性副本的完整 integrity_check 均为 ok，14 相册 / 539 照片 / 374 链接 / 3 设置全行相同。安装后的实际原生 DLL 对新转换照片再次通过 `--require-close` 实际 FP16 数值检查。证据 `artifacts/local-install-0.1.33/installed-upgrade-verification.json` 与 `artifacts/hdr-converter-fidelity/installed-comparison.json`。未推送发布 tag、建立公开 Release 或更改商店提交。

## 独立 HDR 转换器 0.1.0 开发便携版（2026-10-29）

维护者要求 Windows HDR JXR → Android / 小米相册可用的 Ultra HDR JPG 简单便携工具，随后明确要求自行测试，代理停止继续 UI 验收。实现位于 `tools/HdrJpeg/`；独立 C++ / Win32 程序，静态链接 libultrahdr 2.0.2、libjpeg-turbo 3.2.0 和 MSVC CRT。未改动 Luma 主程序版本、固定预览和本机安装，未触碰已有 `docs/MICROSOFT-STORE.md` 的未提交修改；未公开 Release。

`scripts/Build-HdrConverter.ps1 -Test` 构建通过；编码器使用独立 `uhdr-converter-build` 并显式开启 XMP + ISO 21496-1，Luma 解码器原有目录不变。`tests/HdrConverterTests.cpp` 通过实际 WIC JXR → 编码 → FP16 解码回读，0 / 4 / 40 / 203 / 406 / 800 / 1600 / 4000 nits 期望值对应约 0 / 3.974 / 39.574 / 203.396 / 400.449 / 794.951 / 1581.18 / 3936.30 nits。这些是解码数值，不是屏幕亮度计读数；不涉及 NVIDIA SDK 或 HDR 显示呈现。

额外覆盖：独立 WIC 普通 RGB JPEG 读取、全分辨率 RGB 增益图、XMP / ISO 存在、不覆盖已有文件、自动编号、预先取消不输出、拒绝损坏及普通整数 SDR JXR、129×65 奇数尺寸、EXIF 2–8 旋转 / 翻转的尺寸及四角像素、负 scRGB 先转 Rec.2020 后处理、10000 nits 以上裁切计数。验证中发现并修正 WIC 组合翻转次序，重新检查后通过。`tests/verify_hdr_converter.py` 使用 Pillow 解两份 JPEG、NumPy 独立实现 Android XMP 增益公式，核对全部灰阶端点和两组颜色；没有调用本程序或 libultrahdr 解码器。证据：`artifacts/hdr-converter-build.log`、`artifacts/hdr-converter-tests/independent-verification.json`、隔离夹具目录指针 `artifacts/hdr-converter-tests/latest.txt`。

6 张现存 Windows 实际 HDR 截图在隔离目录转换并通过 libultrahdr 回读：3840×2160 两张，1838×1264 三张，3840×2088 一张；输出 917,466–5,840,556 字节，转换加回读约 719–2547 ms（单次热文件缓存，含回读，不作为通用耗时承诺），未触发色值裁切。证据：`artifacts/hdr-converter-real-tests.log`。照片和输出只在忽略的 artifacts 中，不随源码或程序发行。

Computer Use 检查了实际深色初始窗口、可访问的原生按钮 / 文本及支持多选的文件选择框。自动化在文件框赋值时失败，未据此声称完成 GUI 批量转换。维护者要求自行测试后，浅色 / 窄布局、拖放、实际点击转换 / 取消及选择输出目录交由维护者验收；没有更改系统主题或辅助设置。手机 / 小米 HyperOS 相册识别与高光显示未实测，继续保留 TODO。

本轮开发载荷：`artifacts/HdrJpeg/Luma.HdrConverter.exe`，版本 0.1.0，1,476,096 字节；`artifacts/Luma-HDR-Converter-0.1.0-windows-x64.zip`，638,054 字节，SHA256 `828C6DC74BB89CF8B6CA73AD57B916691747DE2AA5F226E925C2E43E0760453D`。ZIP 内只有 EXE、build-info、说明和五份许可；没有测试程序、照片、SDK、运行库安装器。dumpbin 仅列 Windows 系统 DLL（ole32、USER32、GDI32、SHELL32、COMCTL32、dwmapi、gdiplus、ADVAPI32、KERNEL32）。构建信息保留真实 `dirty=true` 开发来源，属于本地手测候选，未冒充干净源码正式发行。用户要求停止追加测试，本轮不为精确正式来源重建或重跑长检查。

## 0.1.32 正式附件与商店更新包（2026-10-21）

维护者要求小改动沿用上版 Release 正文，并优先交付文件、减少重复测试。正式标签 `v0.1.32` 固定到干净源码 `881361c2a3002cd8bdefa4922fc8ae18eb2d0a28`，主 CI `39456297679` 与完整 RTX Release 构建 `39457822287` 成功。下载并核对四个实际附件、GitHub digest、SHA256 与 build-info：sourceDirty=false，RTX HDR / VSR 完整，504 个便携文件逐项建立清单，原始 NVIDIA 组件和许可匹配。Release 已公开、非预发布并设为最新；功能正文与 0.1.31 相同，仅替换版本 / 文件名并加入按既有要求保留的夸克入口。

使用这份正式载荷在同一干净源码工作区运行 `Build-Store.ps1 -SkipBuild -ForStore`，沿用既有 Partner Center 身份；生成未签名 `Luma_0.1.32.0_x64.msix`。`verify_store_package.py --require-store-identity` 通过 505 个包文件与源载荷 / staging 字节比对、ZIP 完整性、13 个文件关联、runFullTrust、Logo 和原始 NVIDIA 文件检查；Name / Publisher 与上一原始 0.1.31.0 MSIX 完全一致，版本增加。本轮没有安装 / 签名 MSIX、修改证书信任或操作 Partner Center；不能把内容校验写成商店认证或本次实际包身份运行验收。

EXE 为 61,191,538 字节（58.36 MiB），ZIP 为 85,840,146 字节（81.86 MiB），MSIX 为 87,529,690 字节（83.47 MiB）。三份文件集中在 `artifacts/release-0.1.32/`，分别核对 Release 的 SHA256SUMS 与商店 MSIX-SHA256SUMS；紧凑证据包括 `release-validation.json`、`consumer-manifest.json`、`store-build-info.json`、`workflow-runs.json`、`published.json`。维护者手动上传夸克 / 商店尚未确认，继续保留待办。

本机正常关闭旧安装窗口后，同目录从开发 0.1.32（源 `6805cb7`）覆盖为实际下载的正式 0.1.32（源 `881361c`），安装退出码 0，503 个消费者文件 hash 相同；SQLite 一致性副本的前后完整 integrity_check=ok，图库 / 收藏 / 历史 / 设置四表全行保留。固定预览同步 504 文件，上一开发预览保留，随后重开生产图库。证据为同目录的 `installed-upgrade-verification.json`、`profile-before-official.db`、`profile-after-official.db`、`preview-promotion.json`。按照维护者本轮明确要求，没有追加 HDR / VSR / Live、长 UI 或性能回归；此前相关实机验收仍为历史证据，不虚构本次新测结果。

## 图库索引持续观察（2026-10-20）

复核“随手记”后，三项准备与交付均已完成；紧随其后的未完成事项是继续观察此前图库索引损坏是否复发。本轮只对生产数据库使用只读连接和 SQLite backup API 保存一致性副本，在副本运行完整 `PRAGMA integrity_check`，结果为 `ok`。确认 `ix_photos_opened` 定义仍是 `photos(opened)`；374 条照片的 `rowid / opened` 通过强制索引查询与禁止索引的全表扫描，按相同排序逐行比较一致。副本包含 12 相册 / 374 照片 / 139 链接 / 3 设置，已安装版本为 0.1.32、应用源 `6805cb7`。

本时点未发现索引损坏；不据此判断最初原因，也不把持续观察标记为永久完成。没有向生产库写数据、重建索引、清空图库或改变偏好。紧凑报告和一致性副本保留在忽略目录 `artifacts/index-followup-20261020/index-check.json`、`library-snapshot.db`；公开文档只保存数量与结论，不提交数据库或照片路径。本轮仅补充验证记录，不改变应用版本、不构建、不安装或创建 Release。

## 官网推荐区位置与换行调整（2026-10-20）

根据维护者反馈，将 Sharp GUI 推荐区放到“下载 Luma”整个区块之后、页脚之前；保留独立卡片和原版 Logo，标题上限从 34 缩到 26 px，Logo 从 80 缩到 64 px。旧版说明有 46ch 限宽，右侧按钮另占一列，造成生硬断行；此次移除限宽，按钮放到说明下方，精简文案并让段落按剩余完整列宽自然换行，没有插入手动换行或针对某个屏幕硬凑行数。

实际浏览器 1280 / 768 / 390 / 320 px 宽度检查均通过：推荐区在下载区之后，说明宽度等于文字列宽，按钮在段落下方，页面 scrollWidth 与 clientWidth 相等。四个宽度说明高度依次为 27 / 54 / 81 / 108 px，随屏幕自然重排；原版 96×96 Logo 正常加载，目标 URI 正确，链接可通过键盘聚焦，未发现控制台警告 / 错误。报告与桌面 / 手机截图在 `artifacts/companion-layout-20261020/`。本轮仅调整官网与相关说明，不改应用版本、不重新出包或安装，本机仍为已验证的 0.1.32。

## Sharp GUI 推荐入口（2026-10-20）

应用本地开发版本 0.1.32 在设置末尾增加独立推荐卡片，放在“关于 Luma”卡片下方；README 与官网下载区前同步加入入口。三处链接均指向维护者的 Sharp GUI 仓库，说明它是作者的另一个开源项目，需要另行下载。直接复用维护者提供的 96×96 PNG Logo：应用与网站各 6,673 字节，README 复用应用素材；两份 SHA256 与原文件一致，未重绘图标，也未引入 1.46 MB 的 SVG 位图封装。

增量构建与候选 publish 成功。实际候选 `--verify-settings` 在 Light / Dark × 1160 / 600 窗口下通过：卡片独立于 About、原版 Logo 正常解码、目标 URI 正确、键盘可达，所有文案 / 按钮无横向溢出；滚动到卡片后整体可见。宽卡片 820×196.67 DIP、窄卡片 480×218 DIP。证据：`artifacts/settings-20261020-161137/settings.json` 与四张 `settings-companion-*.png`。截图通过 WinUI RenderTargetBitmap 导出，不代表桌面合成器或 HDR 效果验收。

官网实际浏览器 1280 / 390 px 检查无横向溢出，Logo 为 96×96，静态链接可键盘访问，未发现坏图 / 控制台错误；手机按钮焦点清晰可见。证据：`artifacts/companion-20261020/browser-check.json`、`website-desktop.png`、`website-mobile.png`。高对比度资源、强制颜色与减少动态效果样式已提供；未切换系统辅助模式作人工验收。原生代码 / 解码与 HDR 路径未变，复用已核验 0.1.31 正式载荷中的原生组件及 NVIDIA 运行时，来源与 hash 保存在 `artifacts/local-install-0.1.32/native-provenance.json`。本轮不替换已提交认证的 MSIX，不新建正式 Release。

应用源码提交 `6805cb7` 后重新 publish，实际候选设置检查再次通过（`artifacts/settings-20261020-162420/settings.json`），随后提升为固定 `artifacts/Luma`，上一份预览保留在 `artifacts/Luma-previous-0.1.31-20261020`。build-info 保留原生组件原始来源，应用 sourceCommit 对应本次提交；因原有 `docs/MICROSOFT-STORE.md` 本地旧稿仍未提交，整体 sourceDirty=true，但 uncommittedApplicationSource=false，未将其旧状态混入提交。许可校验发现源码工作区 RTF 与旧载荷仅换行字节不同，停止出包并恢复候选中的已核验原始许可副本后再继续，没有跳过 hash 断言。

复用固定 RTX 载荷编译普通 ProductId 的本机安装器，未生成重复便携包。EXE 61,207,088 字节，SHA256 `67dfc6517f65679285096ee1eab14b2568e7264366f9af23e25935c8808ab3fd`。从注册的同一目录完成 0.1.31 → 0.1.32 覆盖升级，退出码 0；503 个消费者文件 hash 全部匹配，数据库使用 SQLite backup API 保存一致性副本，前后完整 integrity_check=ok，12 相册 / 374 照片 / 139 链接 / 3 设置全行相同。实际已安装 EXE 的设置检查再次完成，包含四种主题 / 宽度的推荐卡片；随后正常重开生产图库。证据：`artifacts/local-install-0.1.32/installed-upgrade-verification.json`、`installed-upgrade.log`、`payload-manifest.json`、`profile-before-upgrade.db`、`profile-after-upgrade.db`、`reopened.json`，以及 `artifacts/settings-20261020-163420/settings.json`。生产数据库和报告均不提交。

## 文档与随手记一致性核对（2026-10-20）

审查工作区的两份未提交文档：`MICROSOFT-STORE.md` 内容与 `52e642d` 前的旧稿完全一致，会将“已提交审核”退回“尚未提交”。先将该本地副本保存在忽略目录，再恢复已提交版本，不把旧状态重新推送。`TODO.md` 的独立随手记予以保留，其中 README 未勾选项按 `08bfbd7` 的实际素材和 `1e22890` 的验收授权记录更新为完成，原始私人路径不公开。

README 所引用的 WebP 为 679,712 字节，SHA256 `924d838f110e0d72854ccb8248c1443fd0edcd63e4e09bba309919cc4dde6520`，与已提交素材相同；此项只核对历史验收和文件一致性，没有重新生成图片或增加 HDR 效果验证。“随手记”三项均已完成，没有下一项待开发任务；图文准备完成不代表已经代发文章。

上一轮 `52e642d` 的主 CI `39287050470` 与 Pages `39287050864` 均成功，线上主页 HTML 与该提交逐字节一致；报告在 `artifacts/delivery-20261020/`。本轮仅整理文档，检查相关提交 / 素材、Markdown 引用、状态一致性和公开源边界，不递增应用版本、不重新构建或安装、不创建 Release 或提交商店。

## 商店提交记录与夸克下载入口（2026-10-20）

维护者反馈首版商店提交已完成，文档、README 与官网据此更新为“已提交审核，尚未上架”；本轮没有重新操作 Partner Center，也没有声称认证通过。维护者提供固定夸克地址及 0.1.31 EXE / ZIP 上传截图，当前同步状态按该反馈记录，未下载网盘文件重新验算哈希。

- 官网下载区新增与现有按钮同样式的“夸克网盘下载”，固定指向 `https://pan.quark.cn/s/109763c10614`。Windows 本机浏览器在 1280 / 390 px 宽度下确认三个入口、目标地址和尺寸；文档宽度分别为 1265 / 375 px，与可用客户区相同，无横向溢出，无坏图，检查时无控制台错误或警告。桌面同排、手机自动换行。入口直接写在 HTML 中、不带动态附件选择器或事件处理；静态检查确认不需要 JavaScript，也不会被 GitHub 版本填充逻辑改写。没有新增图片或第三方请求。
- 解析更新后的 `release.yml`，对实际草稿步骤做 PowerShell 语法检查，并在隔离目录执行说明生成部分：缺少夸克链接时只补一个入口；已有链接时原文保留；重复执行不追加第二次；现有 0.1.31 说明与中文内容保留。没有执行发布步骤、编辑既有公开 Release、推送新 tag 或创建新版本。
- `skill-creator` 的 `quick_validate.py` 通过；交付 skill 已要求每次正式发布后明确提醒维护者手动上传 EXE / ZIP 并更新固定分享目录，确认前不得把网盘同步记为完成。商店用途说明实测 377/500 字符。

本地证据保留在忽略目录 `artifacts/delivery-20261020/`：`browser-check.json`、`download-desktop.png`、`download-mobile.png`、`static-download-check.json`、`release-notes-check.json` 与 `justification-check.json`。本轮不改应用版本、不重新出包或安装；暂存范围核对与公开源边界检查通过，未包含维护者原有随手记、生成输出或私有文件。推送后的 CI / Pages 结果另行核对，不将本地预览当成已部署。

## 维护者实机补验确认（2026-10-19）

维护者在准备提交商店审核时明确反馈：此前列为待补验的实屏 RTX HDR / VSR、Live HDR 和声音综合体验均已验证通过。此项记为维护者人工实机验收，不改写下方代理远程环境的超时 / 未完成结果，也不新增 SDK 计数、FP16 读回或性能测量结论。尚未开展的干净用户 / 其他 Windows 环境迁移覆盖继续保留为后续回归。

重新核对三份 0.1.31 发布文件的实际大小及 SHA256，均匹配既有正式校验清单：EXE 61,254,605 字节，便携 ZIP 85,820,460 字节，商店 MSIX 87,509,471 字节。MSIX 来自固定 34fce1f 的正式商店身份原始未签名候选，未使用 `.test-signed.msix` 或测试修订 0.1.31.1。交付位置见 `docs/MICROSOFT-STORE.md`，代理未执行商店上传或提交。

## 微软商店身份运行验收（2026-10-19）

维护者确认临时测试签名 / 信任，并提供公开支持邮箱。邮箱已在属性页保存，重开读回一致，电话 / 地址为空。分级结果页已显示 IARC / PEGI 3+、ESRB Everyone；独立重新打开的中文介绍有 7 张桌面截图。上述年龄声明和素材上传由维护者完成；代理未提交认证或发布。

原始候选 `Luma_0.1.31.0_x64.msix` 仍为 87,509,471 字节，SHA256 `71f971a533f4a43502d4a113045cb0158a91492de06e5fbf9fd3b6809096f4e4`。直接安装因缺少签名被 Windows 拒绝（0x800B0100）；随后只签测试副本。匹配 Store Publisher 的短期证书经维护者响应管理员提示后临时加入 TrustedPeople；安全超时自动撤销过一次，补测再次授权提示后使用同一测试证书，结束后已移除信任、证书和私钥。没有导出私钥，也没有更改 Developer Mode 或降低签名检查。

- 实际注册包 `DDDD2501.LumaHDRPhotoViewer_0.1.31.0_x64__n77bhhbbe9ttc` 安装成功。通过 AUMID 激活后，从运行进程查询 PackageFullName，与商店身份一致；实际 WindowsApps 载荷的 viewer 首帧、ready、idle 冒烟通过。更早的提取目录冒烟单独记录为无包身份，不混算安装验收。
- 文件打开使用 `Windows.System.Launcher.LaunchFileAsync` 指定 PackageFamilyName，成功启动 WindowsApps 内 EXE 并显示 PNG 夹具。没有修改默认关联。通用 `IApplicationActivationManager.ActivateForFile` 在此 FullTrust 应用返回不支持合约；不把测试 API 选用错误当作应用文件关联缺陷。
- 升级演练仅将副本 manifest 改为 **0.1.31.1**，应用二进制仍是固定 0.1.31，身份 / 签名一致；实际 `.0 → .1` 升级并重启成功。隔离库的 settings / albums 全行摘要相同，声音偏好、置顶 / 排序和既有收藏保留；图库启动重新扫描，照片 / 链接由 1 增至 9，未将正常扫描误报为全库逐字节不变。测试修订包不能上传为新的正式应用版本。
- 实际包的 `--verify-hdr-formats` 完成 **12 个夹具、19 条结果**：XMP / ISO / Apple 增益图 JPEG、Apple / ISO / PQ / SDR HEIC、损坏可选元数据回退、控件、原图对照、混合翻图及 HEIC 重开通过；报告确认 FP16=1、原生 HDR 不调用 RTX 增强。当前远程输出 HDR active=0，因此没有声称本轮验证过实屏亮度、JXR 打包预览或 Windows 校准峰值。
- 实际包 Live SDR 性能 / 生命周期用例通过，3 次各收集 12 帧，首帧约 **845 / 221 / 205 ms**，提交 p95 **2.83 / 3.52 / 2.51 ms**；这些是该小米样本与本机环境的数据，不是帧率承诺。准备 / 释放在 UI 线程外，取消、自然结束、重播和关闭检查完成。Live HDR 用例在要求 HDR 生效处超时，同源 EXE 对照同样超时；声音/UI 综合用例播放已出现帧，但在后续 UI 验证阶段未生成完成报告，正常关闭尝试也超时，最后仅终止匹配隔离参数的测试 PID。该用例保持未通过，未更改应用源码掩盖失败。

数据保护过程也保留失败证据：开发工具子进程虽然查询不到包身份，仍继承宿主的文件系统重定向；两次目录隔离在哈希断言处停止，只移动了宿主 overlay，均按精确物理路径恢复。文件关联试验因此曾向真实图库新增一条夹具历史。专用管理员清理步骤先保存一致性备份，并验证四表除这一条测试路径外与静止备份逐行一致，才删除该唯一夹具行；最终原有 **12 相册 / 373 照片 / 139 链接 / 3 设置** 全行一致、完整 integrity_check=ok。此机观察到包写入共享 `%LOCALAPPDATA%\Luma`；不据此承诺所有系统自动迁移或卸载行为。没有移动、删除用户原照片。

测试修订包已实际卸载，EXE 安装仍在原目录，版本 0.1.31.0；重新启动原安装路径并核对进程。四个证书存储均无此次 thumbprint，CurrentUser 私钥随证书删除；原始上传候选 hash 不变。测试包未上传、未提交认证，原始候选和测试签名副本严格分开。证据：`artifacts/store-runtime-20261019/` 下 `installed-package.json`、`packaged-activation.json`、`file-launcher-result.json`、`upgrade-activation.json`、`upgrade-data-after.json`、`packaged-hdr-formats/hdr-format-windows.json`、`packaged-live-sdr/live-performance.json`、`production-final.json`、`uninstall-result.json`、`cleanup-complete.json`；宿主重定向与测试失败记录也保留。

仍需在能枚举真实 HDR 显示器的交互会话验收打包版 RTX HDR / VSR、Live HDR，并补完 Live 声音综合用例。当前不是“所有验证均通过”或“已获商店认证”。

## 微软商店草稿首次复核（2026-10-19，运行验收前）

通过现有 Chrome 登录检查当前产品与 Submission 1：属性完成、程序包未启动，桌面截图 0 张；年龄分级停在 3+ / Everyone 预览且 IARC / 成年声明未确认。免费 CNY 0、全球 240 市场、审核后由维护者“立即发布”的设置已读取。支持信息明确标为 optional，电话 / 地址不是本页必填。补充中文介绍和第 5 项功能中的相册置顶 / 排序、多选筛选，保存并重开核对一致；官网更新为已部署的 Pages 地址，重开字段值一致。原始看图 PNG 复制后 hash 一致，2118×1255、3,269,066 字节，未用 README 合成图或上传素材。

从后台复核实际包身份与本地忽略文件一致，四项 GitHub Store Variables 已配置并读回匹配。固定 v0.1.31 / 34fce1f 的 Store 工作流 `39221698142` 成功，完整 RTX 构建、Core、源边界及 `verify_store_package.py --require-store-identity` 在云端通过。Actions 产物容器为 86,952,172 字节；不把容器大小当 MSIX 安装体积。实际下载的 MSIX 为 87,509,471 字节（83.46 MiB），SHA256 `71f971a533f4a43502d4a113045cb0158a91492de06e5fbf9fd3b6809096f4e4`，正式 Name / Publisher / 显示名称、源提交 / sourceDirty=false / RTX 标志、ZIP CRC、3,444 个 block map SHA256、13 个关联、仅 runFullTrust 和原始 NVIDIA 组件哈希均核对通过，报告 `msix-local-verification.json`。未签名候选尚未做正式身份侧载 / 激活 / 更新与数据虚拟化验收，没有安装信任证书，没有上传 Partner Center、接受 IARC 条款、提交认证或公开发布。截图、重开字段记录、Variables 匹配结果和工作流 / 产物索引见 `artifacts/store-progress-20261019/`。

## README 展示标签（2026-10-19，已验收）

按维护者要求保留 SDR 预览照片的颜色，在 HTML 合成源的看图窗口上叠加既有 HDR 捕获标签；没有制作将真实 HDR 状态伪报为生效的应用版本。覆盖层按原截图四边裁掉 2 px 的几何映射，原截图与主页增益图不变。最终 1280×720 PNG → 无损 WebP 像素逐字节一致，679,712 字节，SHA256 `924d838f110e0d72854ccb8248c1443fd0edcd63e4e09bba309919cc4dde6520`。与上一稿的像素变化仅位于 x=1167…1231、y=233…256，共 1,529 像素；标签以外的照片颜色与布局一致。输入 hash、浏览器导出源、PNG 和 `badge-compression.json` 在 `artifacts/readme-real-20261019/`。维护者于 2026-10-19 验收并授权提交 / 推送，解除素材暂缓公开限制；再次解码复核尺寸 / 679,712 字节与上述 SHA256 一致，公开边界检查通过。采用 `08bfbd7` 的新图按确认合入主分支；主页修正已独立推送，原草稿 `dece052` 不改写。本轮仅更新文档与图片，不递增应用版本或重新安装。

## 主页展示标签（2026-10-19）

主页使用既有透明无损 `hero-hdr-badge.webp`，在 HTML 中常驻加载；SDR 单图覆盖 RTX HDR 展示标签。浏览器支持 HDR 对比时移入 `.cmp` 并与 HDR 图共用裁剪，左侧露出底图的 SDR 标签，右侧为 RTX HDR。1280 px 对比的 Home / End 两端已目视核对，390 px SDR 单图与 HDR 对比的标签 / 画面矩形一致、无横向溢出 / 坏图，页面无错误 / 警告。SDR 媒体条件使用忽略目录的独立夹具模拟，HDR 分屏使用浏览器报告的 HDR 能力；这不是新的实屏亮度 / GPU HDR 验证。两个 AVIF、WebP 底图和既有标签的 SHA256 与修改前相同，没有照片色调调整或重新压缩；SDR 访问新增一次约 11 KiB 标签请求，HDR 对比复用同一元素。截图、输入 hash 和布局记录位于 `artifacts/readme-real-20261019/`。不引入应用开发开关、版本变化或本机重装。

## 宣传文稿与实机图初稿记录（2026-10-19）

图文稿 `docs/marketing/launch-article.md` 使用维护者已提供的创作动机，以第一人称起草，配图指向现有 `site/assets/hero-1100.webp` 和 `card-albums.webp`（合计 250,230 字节）。两处相对链接实际存在，当前版本 / 包大小与正式附件一致；说明 Windows HDR、RTX 条件和 HEIC / HEVC 扩展，截图不作为实屏 HDR 效果证明。正文不带未验证的速度或峰值承诺，没有代发酷安 / 小黑盒。

初稿制作时，README 新图在独立本地分支 `codex/readme-real-screenshots` 提交 `dece052`，当时未推送、等待维护者确认；后续标签调整及验收状态见上节。输入为维护者选择的相册、SDR 雪山预览截图和已有品牌图标，窗口内部未重画或虚构；HTML 仅组合背景 / 标题 / 真实截图。输入转换成无损 WebP 后 RGBA 像素核对一致，原文件不变。浏览器 SVG / foreignObject 与 Canvas 直接导出无损 PNG，未采用早期工具 JPEG 截屏作为最终来源；最终 1280×720 WebP 与该 PNG 解码像素逐字节一致。新文件 679,550 字节（约 663.62 KiB），旧图 1,556,588 字节，减少 56.34%；布局包含等比缩放，不声称合成图保留原始大截图的全部分辨率，也不声称 Git 历史已经缩小。

新图 SHA256 `28628633969694167a46b940d925a5419d361864ef3a1884b6577c702db65fe3`。原生窗口、标题与版式已目视核对，源公开边界检查通过；当时主分支不包含此图片改动。素材生成记录和预览位于 `artifacts/readme-real-20261019/`，本轮为文档 / 图片工作，不重新构建或升级应用。

## GitHub Pages 与 v0.1.31 正式消费者包（2026-10-19）

维护者在调度会话明确要求按 TODO“随手记”继续官网部署与最新修复版正式发布。拉取现有主页提交后，确认旧 Pages 运行 `39147680831` 在仓库未启用 Pages 时失败；通过 API 启用 `build_type=workflow`，手动运行现有流程 `39153789167` 成功，HTTPS 已强制启用。实际站点 https://lueluelue12138.github.io/luma-hdr-photo-viewer/ 首屏已在本机浏览器查看，HTML / 11 个资源逐字节匹配提交内容，总计 1,171,194 字节；这不是单次访问流量或大陆速度测量。仓库 homepage 字段已指向该地址；证据 `artifacts/pages-20261019/`，截图、HTTP 清单与配置保留，未改变页面设计。

`v0.1.31` 固定在 `34fce1f18c1b7f9d577841c9a7777ca14b20a613`；创建标签时将维护者未提交的 TODO 随手记保存在可恢复 stash，标签推送后恢复，未混入发行或丢弃。主 CI `39154819634` 与 RTX 构建 / 草稿 `39155506146` 成功。草稿的四附件已下载，Release digest / SHA256SUMS 与实际字节一致，build-info 的源码、sourceDirty=false、RTX 能力及 NVIDIA 输入 / DLL / 原始许可哈希符合固定输入。便携 ZIP 完整性与全部 503 文件清单核对，未含私人样本、数据库或 SDK 构建输入。

正式安装器 61,254,605 字节（58.42 MiB），SHA256 `021eb0a15b7e7aae7f6c0dafb9d79cfe9946d1c7d6cd0fd5321dc85fd875c457`；便携 ZIP 85,820,460 字节（81.84 MiB），SHA256 `bf7b03316d68bc17e6be83b97a824aa73f49e7448651c77eee4bddf31b2b9d2b`。实际下载的消费者包通过坏索引 / 元数据失败 / 真实缺失文件的八场景回归。正式 EXE 覆盖本机同版本开发包，从源 `55ce055` 同步到正式源 `34fce1f`，退出 0，同一安装目录的 502 消费者文件全部一致；前后 SQLite 一致性副本完整检查为 ok、原有四表全部逻辑行不变。已安装 EXE 的筛选偏好、相册置顶 / 排序重开及本次错误分类再次通过，正常重开生产图库。

正式包原生库在 RTX 5070 Ti Laptop GPU 上实际执行 HDR / VSR 各一次 SDK evaluation，离屏 FP16 GPU 读回原图白色为 2.5、增强后约 12.5，原图 / 增强切换精确恢复缓存像素且不新增 SDK 调用。当前远程显示的 DXGI 输出无法查询，真实显示器查询检查返回 DXGI_ERROR_NOT_FOUND；上述测试显式注入 1000 nits / SDR white=2.5 到独立测试渲染器，没有修改系统 HDR。因此该结果验证真实 SDK 与 FP16 渲染，不作为实屏呈现 / Windows 校准峰值或亮度观感验收；原生源码未变，先前实屏记录继续保留。证据 `artifacts/release-0.1.31/consumer-gpu.json`、`installed-upgrade-verification.json`、`installed-filter/`、`installed-albums/` 及 `installed-library-regression/`。

消费者验收后将 Release `416635492` 公开为非预发布并设为最新，匿名公开页面 HTTP 200、latest 指向 v0.1.31、四附件保持不变。固定预览的 503 文件同步正式便携载荷，原本地 0.1.31 保留回退。本次未上传 / 提交微软商店；README 更新正式版链接和实测包体积，真实截图替换另行准备且等待维护者验收。

## 0.1.31 图库记录错误与照片解码隔离（2026-10-19）

用户报告照片已经显示且增强状态有效，却覆盖“无法打开照片”；实际日志堆栈位于 `LibraryStore.RecordOpenAsync`，在帧已呈现后写浏览记录时触发 SQLite 11。在线一致性副本的完整检查发现 `ix_photos_opened` 项数错误及 rowid 321 / 322 / 323 / 324 / 337 缺失，其余表可完整读取。升级 0.1.30 前备份有完全相同的坏索引；升级 0.1.29 前及更早备份完整。尚不能确定最初损坏的操作或环境原因，不能将它直接归因于 0.1.30。此前升级仅比较表行，未检查索引完整性，确实未发现这一既有问题；维护流程已补上升级前后完整 `PRAGMA integrity_check`，不以 quick_check 替代。

先在保全副本定向重建最近打开索引，完整检查返回 `ok`、四表全部逻辑行及 rowid 不变，对五条受影响记录更新历史后仍完整。随后正常关闭生产安装的全部窗口，取得新的 SQLite 一致性恢复副本，在事务内对生产库执行相同的 `REINDEX ix_photos_opened`，在提交前确认完整性和所有表行不变，提交后另开只读连接复验。12 相册、358 照片记录、124 相册链接和 3 设置原样保留。未清空图库、恢复过期备份或改变原照片；报告 `artifacts/database-20261019/inspection.json`、`repair-rehearsal.json`、`production-repair.json`，私人库不入 Git。

修复程序错误分类：已完成的帧不会因为收藏读取 / 浏览记录 / 收藏写入异常而变为开图失败；仅出现可关闭、随主题变化的原生 InfoBar，保留完整诊断日志。关闭 / 换图后的异步结果有代际检查；新选图清除旧提示。实际解码、渲染和文件不存在仍使用真正的加载错误。原生渲染 / HDR / VSR 未改，本轮不声称新增 SDK / FP16 效果验证。

`tests/verify_library_failure.py --app <候选EXE> --control <旧EXE>` 创建全新的隔离库，仅将其自建索引页变为空索引，不接触生产库或原图。旧版 0.1.30 确认先呈现再因 SQLite 11 失败；新候选八项场景通过：真实坏索引、可关闭提示与导航、触发器拒绝收藏写入、表临时不可读、修复后保存历史、八次重叠选图、真正缺失文件和恢复到有效图。浅 / 深 × 1160 / 600 DIP 目标宽度共四个实际 InfoBar 布局及导出图检查通过。首次测试的相册在线扫描把故意缺失照片标记为 missing，夹具数量断言失败；调整隔离相册为离线路径后重跑通过，不将夹具修正称作产品修复。最终合成样本报告 `artifacts/database-20261019/ui/candidate-yig4vlrm/library-failure.json`；额外通过 `--photo` 只读使用用户 DSC06997-4 原图，报告 `real-photo/candidate-wp4ej49k/library-failure.json`。

88 项 Core 检查通过，新增并发写入 / 索引一致性检查：三份 LibraryStore、400 照片、96 次历史写入与扫描并发、24 收藏写入，完整检查及重开后的照片 / 历史数量一致。测试验证现有正常写入没有复现索引损坏，并不能证明此前损坏的根因已查明。新测试不会全库扫描用户的启动或开图路径。

提交 `55ce055e77a980d240807df0520f2cd4c42b4c65` 后重建，真实原图重跑八场景通过（`committed/candidate-6ofl28lx/`）。固定预览 503 文件全部 SHA256 一致，上一可用 0.1.30 保留；本机安装器 61,215,684 字节、SHA256 `d3a42c3ba7ffc00effd25d7143c44f265ea9659eccbf1366fcdd46aa4d4dca07`，同目录从 0.1.30 升级至 0.1.31，退出 0。502 消费者文件校验一致，升级前后的一致性副本完整检查均为 `ok`；两份副本当时的 12 相册 / 370 照片 / 136 链接 / 3 设置全部逻辑行一致。该计数与更早修复时的计数属于不同时间点，不把期间正常图库刷新当作安装器改写数据。记录 `artifacts/local-install-0.1.31/installed-upgrade-verification.json`。

已安装 EXE 对用户 DSC06746-3、DSC06997-4、DSC06998、DSC07063 四张原图逐张只读复验，每张均通过完整八场景；隔离库位于 `artifacts/database-20261019/installed/` 的四份 `library-failure.json`，随后正常打开生产图库。该功能验证不修改用户照片。本机载荷如实标记 `sourceDirty=true` / `uncommittedPaths=[docs/TODO.md]` / `uncommittedApplicationSource=false`：保留维护者独立编辑的随手记，应用源码已提交，未将无关记录混入提交或把整个工作区谎称干净。原生 DLL 沿用已核验来源、源码未变。本轮没有推送发布 tag、公开 Release 或商店提交。

生产图库正常重开后的新一致性副本完整检查也返回 `ok`，记录 `artifacts/local-install-0.1.31/production-reopen.json`；实际运行路径和产品版本确认是已安装的 0.1.31 / `55ce055`。

## 迭代交付 skill 与补装 0.1.30（2026-10-18）

新增项目内 `.agents/skills/commit-and-release/`，入口说明按改动选择验证、共同署名与推送；两份引用分别覆盖本机覆盖升级和显式授权的 Release / MSIX。根 AGENTS 记录维护者已授权常规迭代推送、每次应用版本更新后同步本机安装，后续暂缓指令优先；发布 tag / 正式 Release、Store 上传和提交仍需对应授权。skill-creator 的 `quick_validate.py` 在 UTF-8 模式通过；引用文件、UI 元数据、实际脚本入口、tag 仅生成草稿 / Store 仅手动构建的描述核对通过，报告 `artifacts/local-install-0.1.30/skill-validation.json`。本轮未改应用源码或递增版本，不重跑无关的原生 / Core 全量测试。

补装此前已验证的 0.1.30，来源 `57e20530806be4ac7986d31b76a3e697218e3bd3`，直接复用固定预览完整 503 文件生成本机 RTX 安装器，没有新增完整便携目录。安装器 61,217,557 字节，SHA256 `e1dc1107b60451a11addae600202bb56910de0ad8d89e1be4824b4aedf72f336`；原生库和 NVIDIA 组件沿用该载荷的已验证来源。安装展示与随包 runtime terms 使用相同文件；仓库该 RTF 与载荷文本相同，原始字节的差别仅为换行形式，未改写载荷来源或许可内容。

旧进程同时有多个窗口，最初两次正常关闭只关闭其中一个窗口，因此尚未开始安装；继续正常关闭剩余窗口后进程退出，没有强制终止。SQLite backup API 保留私有一致性恢复副本后，安装器退出 0、同目录从 0.1.29 升级到 0.1.30。逐个核对 502 个消费者文件 SHA256 一致，另一个开发符号按 Inno 配置排除；注册版本与实际程序集版本均正确，产品版本含 `57e2053`。安装前后的 albums / photos / album_photos / settings 逻辑行数和内容摘要全部相同。

已安装 EXE 另用隔离测试库重开：`--verify-photo-filter --verify-filter-read` 返回 0、报告 complete=true，HDR 筛选偏好及匹配相册 / 四张结果正确，随后打开生产图库。证据 `artifacts/local-install-0.1.30/installed-upgrade-verification.json`、`verification/filter-reopen.json`；数据库恢复副本不入 Git。固定预览仍为同源 0.1.30，上一可用版保留。本轮属于 skill / 文档和本机安装交付，不声称新增 HDR / VSR 效果验证，也未创建 tag / 公开 Release 或上传商店。

## 0.1.30 照片类型多选筛选（2026-10-18）

相册首页 / 相册照片、收藏、全部照片及最近打开使用同一个原生筛选弹层。HDR、实况、普通静态照片多选取并集，HDR Live 只出现一次；未选或全部选中保留全部照片，不启动类型扫描。HDR 依据文件自带元数据 / 已支持的浮点格式，RTX HDR 增强不把 SDR 原图变成 HDR 类型。相册首页仅显示含匹配照片的相册，显示匹配数量并临时选择匹配封面；原封面、置顶、照片排序独立保留。

筛选在后台读取元数据；JPEG / HEIC 复用现有有界解析，JXR / TIFF 浮点检查调用 WIC 的格式查询，不执行全图像素重建。最多两路任务，共享可见角标读取限流；会话缓存按路径 / 大小 / 修改时间复用，最多 16,384 项，不持久化照片分类。文件 / MOV 伴随视频变更通知和手动刷新使缓存失效，包括已移出相册但仍收藏的照片。读取失败单独计数，不能归为普通静态；清除、换页和关闭取消旧请求，不发布部分或过期结果。筛选选择独立保存，后台识别不阻塞首次窗口揭示。

85 项 Core 检查通过，其中新增 16 项覆盖多选并集 / 去重、普通照片与未知区分、文件戳、缓存复用、并发上限、取消 / 重试、读失败与相册链接。最终 `artifacts/photo-filter-20261018/final/photo-filter.json` 完整通过：真实 HDR JPEG、ISO HEIC、线性 JXR、SDR Live、HDR Live、普通 PNG 共七张样本；实际筛选 / 清除 / 刷新控件、收藏 / 相册范围、搜索和日期排序、空结果、缺失收藏恢复、匹配封面 / 置顶、原封面恢复及无变化卡片保留。打开真实预览窗口验证两张筛选结果的顺序及下一张；该检查关闭可选 RTX / VSR，只验证导航，不声称新的 SDK 效果验收。第二进程的 `filter-reopen.json` 验证选择恢复与四张 HDR 结果。

深 / 浅 × 1160 / 600 / 480 DIP 外窗目标共六种实际 WinUI 布局，弹层继承主题；窄相册页另验排序与筛选同时可见，标题宽约 57 DIP。XAML 导出已目视核对漏斗、状态 / 清除、卡片与角标；系统材质在此导出方式中可能透明，不将其当成完整桌面材质截图。最初排序测试过早读取了尚未完成的异步状态，改为等待当前排序请求完成，原失败报告保留，不将夹具时序问题称作产品修复。

3043 张相机照片在独立测试库中只读索引，没有复制该图库原图。最终首轮识别 4676.24 ms / 3043 次元数据探测，HDR 0、普通静态 3043；改选类型 17.76 ms、新增探测 0，只实现 21 张卡片。324 个 UI 队列回调的 P95 1.71 ms / 最大 8.45 ms；不是实际显示帧率、输入延迟或磁盘吞吐量。此前运行首轮分别 16.18 / 16.29 秒、热切换约 15.42 / 32.27 ms；系统缓存、进程热身、窗口尺寸均可能影响结果，未隔离变量，因此不声称从 16 秒到 4.7 秒是确定优化。一次 Reader 长度快照尝试无明显收益，已撤回，不扩大本轮解析器变更。

复核模式：`--test-library <隔离配置目录> --verify-photo-filter`；父目录 `fixtures.json` 指向既有样本副本和可选大相册，配置 / 私人路径不入 Git。第二次使用同一库增加 `--verify-filter-read`。功能完成时仅更新开发版；维护者随后要求后续版本同步本机安装，已补装 0.1.30，见上方记录。未创建 Release 或上传微软商店；正式 GitHub 版仍为 0.1.28。

## 0.1.29 开发版相册置顶与排序（2026-10-18）

维护者随后要求本地安装，使用已提交 `1d18ae8d9d1f2cbc8888383e6c5bd0e1699189b4` 重新构建托管载荷，并复用实际正式 0.1.28 安装内的原生库（SHA256 `ba21e4c7fdcefc06caaf13f62e02d484245fd97e1c3aca14ff511dbb10b45d33`）；`src/Luma.Native` 在这两个提交间无变化。检查发现旧开发目录部分第三方许可证仅 1 字节，安装准备时从正式载荷恢复并重新收集完整项目 / NuGet / .NET 许可，完整候选的 15 项 WinUI 场景再次通过。该完整候选也已同步固定预览，503 文件 SHA256 一致，保留原 0.1.28 回退。后续快捷 publish 不能省略许可证收集。

本地安装器 61,166,571 字节，SHA256 `0554434170969ad9405a9f52f4db33ceb50f02206c1ecbf3fe6b2f0d85dbaf0b`。首次因运行中的 Luma 未被 RestartManager 关闭而在修改文件前退出 5；正常退出相关窗口后重试退出 0，同一目录升级 0.1.28 → 0.1.29。最初校验误将明确排除的 PDB 计入安装文件，已按安装清单修正：502 个消费者文件逐个 SHA256 一致，1 个开发符号不分发。升级前 SQLite 一致性副本与升级后四表逻辑行 / 数量完全一致；正式安装路径的 0.1.29 EXE（产品版本带 `1d18ae8`）在隔离库重开验证置顶与排序成功，随后重新打开生产图库。证据 `artifacts/local-install-0.1.29/installed-upgrade-verification.json`、`verification/album-reopen.json` 和 `fixed-preview.json`，私有数据库不入 Git。本轮为本机安装，未新建公开 Release、未上传商店，也不作为新增 RTX / HDR 效果验证。

相册卡片和侧边栏共用原生上下文菜单，支持置顶 / 取消，卡片主题色图钉和侧边栏顺序同步。相册页提供名称 A–Z / Z–A、最近 / 最早添加、照片最多 / 最少；每档均先排置顶组，组内按所选方式排序，同名 / 同数量使用稳定次序。添加顺序依据现有相册 ID，重复导入不改变顺序。总览偏好单独保存，不改相册内照片排序；快速连选串行保存，防止旧写入覆盖最后选择。排序只查询已有 SQLite 索引，不启动文件扫描或原图解码。

数据库新增 `pinned`，旧库采用加列迁移，未重建表或移动原图。69 项 Core 检查通过，新增 16 项覆盖 0.1.28 表结构迁移、重复初始化、封面 / 收藏 / 历史 / 偏好保留、重开后 pin / 排序、六种稳定排序、重复导入、空相册、离线保留与缺失链接计数。`tests/Luma.Tests/AlbumOrganizationTests.cs` 为独立隔离夹具。

实际 WinUI 验证使用生成的图标照片，不读生产库或私人照片。最终 `artifacts/album-organization-20261018/final-pass/album-organization.json` 通过：菜单 UI Automation 调用、置顶 / 取消 / 再置顶、六档下拉框、快速连选、过滤与回到相册、独立照片排序；非默认排序下无变化刷新保留 ItemsSource，缩略图新增加载为 0。以 1160 / 600 / 480 DIP 为外窗目标、按系统缩放转换为实际像素，深 / 浅主题共六种布局的排序框边界、标题避让、图钉主题与名称 / 数量 / 封面实际绑定通过。外窗边框导致实际 XAML 宽分别约 1145.33 / 585.33 / 465.33 DIP。第二独立进程的 `album-reopen.json` 验证启动即恢复“照片最多”、置顶和相册内“最早优先”。

新增编译绑定后曾发现与旧 Binding 混用使相册内容未绑定，已统一相册模板并补充实际控件断言。另一次候选误用 `src/Luma.Native/bin` 的旧 DLL，缺少 `LumaFrameWarning` 入口，封面检查未通过；最终复用固定已验证 0.1.28 的原生库（SHA256 `412d20c88d2f0f84a7941305e5aaedbd0fd56bc26cc910742206c274030ef6ad`），完整重跑通过。两次中间结果不作为最终证据，也未覆盖固定预览。原生源码未变，本轮不声称新增 HDR / VSR 输出验证。

复核：启动开发 EXE，传入 `--test-library <新的隔离目录> --verify-album-organization`；第二进程使用同一目录，加 `--verify-album-read`。XAML 导出图已目视核对名称、长标题截断、图钉、排序入口与封面；系统背景 / 部分原生组合材质在此导出方式中可能透明，不作为桌面材质截图验收。公开正式版仍为 0.1.28，本轮没有创建 Release 或更新已安装版本。

## 0.1.28 全屏顶部白边修复（2026-10-17）

本机控制组复现：3840×2160、150% 缩放，FullScreenPresenter 的客户区已覆盖屏幕，但扩展标题栏开启时 XAML 根高仅 1439.333374 DIP，即约 2159 个物理像素。证据 `artifacts/fullscreen-0.1.27-control/fullscreen.json`。仅修改 DWM 描边未解决根布局缺口，未采用该方案；对 DWM 仅写属性的查询也不作为成功证据。

实际修复仅在进入全屏前设置 `ExtendsContentIntoTitleBar=false`，退出后恢复扩展标题栏与拖动区域。没有放大 / 裁切照片来遮边，也没有新增逐帧工作。`artifacts/fullscreen-verified/fullscreen.json`：12 次进出（亮 / 暗 × 普通 / 最大化 × 3 次），客户区与 XAML 根均完整覆盖 3840×2160，标题行高度 0；切换焦点 / 开合详情不会重新引入标题栏，退出后 Win32 样式、窗口位置 / 大小 / 最大化状态与 44 DIP 标题行恢复。其他 DPI / 显示拓扑仍待实机验证；本轮不把布局检查当作新增 HDR / VSR 效果验证。

复核命令：`python tests/verify_fullscreen.py --app <exe>`。使用单独 `--test-library` 与合成暗色边缘图片，不访问生产图库、不修改显示设置。中间编译复用 `artifacts/Luma-candidate`，没有为每个调整新建程序目录。

版本号更新后的候选再次完成全部 12 次检查，报告 `artifacts/fullscreen-0.1.28-final/fullscreen.json`；Win32 全屏样式为 `0x14000000`，没有 caption / sizing frame。53 项 Core 与 949 个已提交公开对象扫描通过，新增文件随提交再次扫描。窗口工具对独立候选返回同一所属进程不匹配错误，刷新后仍失败，因此未将额外截图检查记为通过；用户原预览进程不被关闭。

[v0.1.28 正式 Release](https://github.com/lueluelue12138/luma-hdr-photo-viewer/releases/tag/v0.1.28) 已公开并设为最新，非预发布；固定源提交 `86b85f5752ee37b6a57b11712feaac2e75e33169`。主 CI `39017206967` 和 RTX 构建 `39017256402` 均成功，构建来源 `sourceDirty=false`、完整 RTX。四附件名称及来源核对通过，安装器 61,186,429 字节（58.35 MiB），SHA256 `34fcfa95bb4212181600eb059c360305974120db48dc654ce2bbf777e888f4fa` 同时匹配 Release digest 与 SHA256SUMS；便携 ZIP 85,770,025 字节（81.80 MiB），本轮未下载 ZIP，不声称本机逐文件检查过该 ZIP。

维护者随后明确授权替换本机已安装版。使用上述已校验正式安装器覆盖升级同一安装目录，安装器正常关闭旧版本、退出码 0、无需重启；升级前对 SQLite 做一致性恢复副本，升级后 albums / photos / album_photos / settings 的逻辑行内容哈希和行数完全相同。注册版本与实际 EXE 均为 0.1.28，产品版本携带同一源提交，NVIDIA DLL / 原始许可 SHA256 与构建来源一致；本地记录 `artifacts/release-0.1.28/installed-upgrade-verification.json`，私人恢复数据库不会上传。**已安装的云端消费者版本**再次通过全部 12 次全屏进出，`artifacts/fullscreen-0.1.28-installed/fullscreen.json`；随后重新打开生产图库。固定开发入口已更新 0.1.28，上一版保存于 `artifacts/Luma-previous-0.1.26-20261017`。

本地 0.1.28 原生渲染回归另验证 SDR / WIC、缩放 / 旋转、透明度、原生 JXR 与玻璃 FP16 读回，线性 JXR 保留 4.0；日志 `artifacts/fullscreen-0.1.28-committed/native-rendering.log`。本轮 Windows HDR 关闭，RTX HDR 调用为 0，因此不声称重新验证了实际 RTX HDR 生效，沿用此前已记录的验证范围。本次生产改动不涉及原生渲染代码。新增提交及标签后的公开源码边界检查通过 967 个可达对象。

## 0.1.27 正式附件与 HDR 补验（2026-10-16）

[正式 Release](https://github.com/lueluelue12138/luma-hdr-photo-viewer/releases/tag/v0.1.27) 已公开，非草稿、非预发布；源提交 `ee3cfc617440336bcb1716249fb55dee1d06da62`，没有移动旧标签。[完整 RTX 构建 38855410902](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38855410902) 与主 CI 均通过。Actions 产物下载后整体 SHA256 为 `9ef7449d83948a85dee7140b332c61a4885be52b29481dafe7eb6bc992c2d651`，匹配 GitHub digest；四附件也逐个匹配 digest 与 SHA256SUMS，503 个便携文件逐个与 ZIP 相同。实际 EXE 产品版本携带同一提交，构建元数据 `sourceDirty=false`。消费者目录 203,595,515 字节，安装器 61,183,335 字节（58.35 MiB），ZIP 85,764,381 字节（81.79 MiB）。

维护者随后手动开启 Windows HDR，在 RTX 5070 Ti Laptop 上补验**同一云端消费者包**。`artifacts/release-0.1.27/consumer-native-hdr.log`：实际 TrueHDR、VSR-only、组合 / 缓存对照、40 次原图切换及 FP16 读回通过；测试 JPEG 的 TrueHDR 高光约 7.793 scRGB，原生线性 JXR 保留 4.0。以上是实际 SDK 与浮点呈现读回，不是显示器物理亮度测量。

`artifacts/live-photo-20261016-142423/` 小米与 `...142435/` iPhone 均完整通过实际 HDR、原图同时间轴对照、准备取消 / 自然结束 / 重播 / 关闭。TrueHDR 调用分别为 117 / 123，VSR 为 0；变化的 FP16 峰值约 7.801 / 7.555，自有纹理循环前后固定为 80065440 / 99234720 字节。首帧约 2152 / 1303 ms，提交 P95 2.63 / 2.36 ms；合并通知不等于实测丢帧率，不能由提交计时推导物理 FPS。`artifacts/viewer-lifecycle-20261016-143157/` 的 49 个真实窗口、两次进程启动、HDR/VSR 独立 / 并发 / 关闭、快速切图、弹层、滚动和重启偏好通过。此前单张照片测试目录在第 47 窗口因没有滚动范围失败；改用真实多图只读目录后通过，保留原报告，不把测试夹具不足称为应用回归。

`artifacts/release-0.1.27/upgrade-verification.json` 记录 0.1.23 → 0.1.27：复用旧自定义安装目录，旧 / 新进程均能打开保留的图库、收藏、历史和偏好，原文件与缓存不变；重复安装、卸载与其他副本注册保护通过。使用专用隔离安装器，生产库与默认关联未变。NVIDIA DLL / PDF 与原件严格一致；包装 RTF 在云端 Git 检出为 5939 字节 CRLF、本地为 5904 字节 LF，正文仅换行不同。校验工具只对该文本接受 LF / CRLF 等价，实际分发条款 hash 仍绑定构建记录，不放宽二进制许可校验。

0.1.27 开发身份 MSIX 为 87,451,022 字节（83.40 MiB），504 文件 / 13 格式关联 / 原始 NVIDIA 组件 / 清单 / 图标 / SHA256 校验通过，记录 `artifacts/store/0.1.27-development/`。未签名、未安装、没有真实 Partner Center 身份、未上传或认证；不能将其当作正式商店提交包。固定 `artifacts/Luma` 的旧进程仍在运行，按规则未覆盖；正式包测试入口为 `artifacts/release-payload-rtx/Luma.App.exe`。没有为了记录文档再生成新程序目录。

## 0.1.27 发布前复核（2026-10-16）

本轮应用改动仅为版本号与设置页实况 / 缩放帮助；原生 DLL 与 0.1.26 均为 SHA256 `412d20c88d2f0f84a7941305e5aaedbd0fd56bc26cc910742206c274030ef6ad`，不把文档更新算作一次运行时性能优化。53 项 Core、907 个可达公开对象边界检查、三个 workflow 的 actionlint 与差异空白检查通过。

`artifacts/settings-20261016-124735/` 的深浅主题 / 1160 和 600 DIP、实时显示信息、移动去抖、隐藏不查询、校准应用路由通过，关于页渲染已目视检查。`artifacts/live-preferences-20261016-124742/` 的声音跨进程保存、36 组详情布局、填满窗口与控制栏几何通过；`artifacts/photo-features-20261016-125721/` 的 38 张 HDR / Live / SDR 卡片、主题和回收检查通过。

`artifacts/live-performance-release-0.1.27-20261016-125655/`：小米、Apple、Google 各首播与两次重播，均使用 SDR 模式；首帧分别约 771 / 380 / 707 ms，重播约 192–290 ms。媒体准备与关闭不占 XAML，三轮各自纹理预算固定为 61264800 / 66057120 / 58143648 字节，取消 / 自然结束 / 重播 / 关闭通过。本次提交 P95 约 2.3–7.4 ms，与之前约 62–63 ms 不同，但未改媒体渲染代码、未隔离显示 / 系统状态，不将差异宣传为优化收益，也不换算成物理 FPS。

`artifacts/gallery-performance-20261016-131300/` 覆盖 3043 张真实相册、约 58.9 GB 原文件，只读源文件并核验抽样 hash / 全部大小和修改时间。最多实现 22 张卡片、45 次缩略图加载、未变化刷新新增 0；UI 回调 P95 8.82 ms / 最大 31.97 ms，进程 private 峰值 201342976 字节。文件系统缓存未清除，单轮结果不保证所有滚动均无尖峰。另一次 119 张精选子目录检查通过，不混称为 3043 张压力测试。

原生 HDR 亮度 / 峰值 / 自动恢复及 GPU 内存检查覆盖 5 个来源；HEIC 经 QA 专用 libheif / numpy 独立解码与公式核对，FP16 / SDR 回退通过。XMP 的 7 类参数 / 命名空间和 12 类可读 SDR 回退通过；维护者增益图原图只读校验、独立参考和实际 FP16 通过。日志位于 `artifacts/release-0.1.27/` 与既有 `artifacts/heic-verification/`、`artifacts/native-hdr-tuning/`；这些工具依赖不进入用户包。

当前 Windows HDR 关闭。`artifacts/live-photo-20261016-124835/` 的严格 RTX HDR 用例等待 `HdrEffective` 超时，日志已有 388 个普通视频帧；`artifacts/viewer-lifecycle-20261016-125738/` 明确因 HDR 未开启而拒绝成功。保留失败记录，不降低 SDK / FP16 断言，不把本轮 SDR 检查写成新 RTX HDR 验收；旧实际 SDK / FP16 证据见后文。GitHub 消费者包核对和商店正式身份运行检查另记，开发身份 MSIX 出包不等同上架认证。

## 居中避让与实况后台准备（0.1.26，2026-10-16）

`artifacts/live-preferences-20261016-021552/` 两进程声音 / 控件检查通过；3 / 9 / 24 张照片 × 6 种窗口宽度 × 详情开关共 36 种布局，14 个原本无碰撞的状态在开关详情时保持相同左边界与宽度；有碰撞时不重叠、滚动正常，第二次布局不振荡。窗口请求尺寸受实际桌面约束，报告记录实际边界，未用请求值代替测量。填满窗口、声音记忆与图标状态回归同时通过。

实况诊断使用只读小米、iPhone HEIC+MOV 与 Google JPEG，在各自新进程里首播和两次重播；`artifacts/live-performance-final-20261016-021356/` 确认媒体准备 / 关闭都在 XAML 线程之外，视频持续提交、取消准备 / 自然结束 / 重播 / 关闭通过。相同视频的自有 GPU 纹理在三轮分别固定为 61264800 / 66057120 / 58143648 字节，未计媒体引擎与驱动全部分配。没有修改原生渲染器，FP16、SDK 路径和单个在途帧保留。

| SDR 首播样本 | 原顺序准备 ms | 最终后台并行 ms |
| --- | ---: | ---: |
| 小米 | 948.5 | 556.0 |
| Apple | 463.8 | 392.8 |
| Google | 652.4 | 669.8 |

顺序基线为 `artifacts/live-performance-baseline-20261016-020049/`，中间后台候选为 `artifacts/live-performance-worker-20261016-020612/`（对应首播 731.0 / 363.5 / 660.2 ms）。这些是不同时间、文件系统缓存未清理的单次新进程样本，系统媒体组件存在波动，不能据此承诺固定比例收益或所有格式首播变快。重播仍约 0.20–0.23 秒，视频提交 P95 仍约 62–63 ms；没有把本轮优化说成更高 FPS。主要确定改变是文件 / 播放器创建、媒体资源释放不再占用 XAML 线程，且两个独立准备不再串行等待。

本轮系统 HDR 关闭，以上测试显式不请求 RTX HDR；真实 SDK / 变化 FP16 的旧检查未删减，待 HDR 环境恢复后补验托管线程变化。SDR 播放、资源计时与合成状态不能替代 HDR 成功或物理帧率证据。微软关于 MediaPlayer 的 [使用与释放说明](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)作为实现参考。

清理盘点与手动执行清单留在 `artifacts/cleanup-20261016/`；自动审批拒绝后按维护者要求未执行删除，只验证脚本语法、路径边界与 96 张测试副本的原始 SHA256，一份原件将保留。清理不是一次新的应用构建。

## 实况声音、标签、界面与填满窗口（0.1.25，2026-10-16）

本轮为源码 / 本地开发版；公开下载仍为 0.1.23，没有创建 Release / 标签。

- Core 53 项通过，新增 5 项覆盖 HDR 与 Live 独立、文件名 / 假命名空间不误判、HEIF 主图 PQ 与未绑定色彩属性的反例。特性读取是有界元数据检查，浮点 JXR / TIFF 仅通过 WIC 查询像素格式；不启动 GPU / SDK，不据此证明真实 HDR 输出。SDK-free 原生编译通过，原生描述符添加后的 `verify_native.py` 在系统 HDR 开启时完成实际 TrueHDR / VSR / FP16、40 次缓存对照、线性 JXR 与高亮玻璃读回，见 `artifacts/live-photos/features-native.log` / `features-standard-build.log`。
- `artifacts/live-preferences-20261016-012943/`：真实内嵌小米视频、隔离数据库、两个新进程；旧 JSON 缺字段默认静音，开原声后重播 / 换图 / HDR 参数保存 / 重启均保留。Live 标签与视频信息、1240 / 900 / 740 / 600 DIP 开合详情布局、缩略图滚动与不重叠通过。播放按钮与声音按钮的实际坐标在所有状态相同，工具栏宽度一直为 622 DIP；图标三圈共圆心，36 个圆点的相反点成对，播放的旋转 / 呼吸启动与停止复位通过。系统动画偏好参与判定；本轮未更改系统开关，不能把代码检查当作实际关闭系统动画的人工验收。
- 同报告 `fill-window` 验证“填满窗口”在两种窗口宽度 / 四个旋转方向下保持比例，两边均覆盖窗口且至少一个维度刚好贴边；归中、随窗口变化、滚轮退出及适应按钮还原通过。100% 的键盘 / 双击入口未删除。
- `artifacts/photo-features-20261016-011624/`：38 张真实 Apple / Android / JXR / SDR 样本，实际卡片的 HDR / 实况标记与独立预期一致，亮暗主题、卡片回收、原生 AcrylicBrush 与相反字色检查通过，读取 38 次 / 命中 59 次。RenderTargetBitmap 仅用于 XAML 排版 / 图形检查，不用于物理 HDR、实际合成模糊或连续动画观感结论。
- `artifacts/photo-details-20261016-005915/`：EXIF / 拍摄参数、无 EXIF、HDR、延迟读取、取消换图、缓存与亮暗 / 窄窗回归通过，详情读取不新增 renderer jobs / HDR / VSR 调用。

3043 张、58875811079 字节的真实高清相册只读回归最终报告 `artifacts/gallery-performance-20261016-013142/`：最多 26 张实现卡片，45 次 WIC 缩略图加载、未变化刷新新增 0 次；特性元数据读取 21 次 / 命中 21 次，没有遍历全部图片解码。private 峰值 202276864 字节，UI 回调中位 0.35 / P95 9.98 / 最大 23.96 ms；初始可视缩略图约 1035 ms，后续回到首屏约 32 ms。目录成员 / 长度 / 修改时间与 32 个抽样原件 SHA256 均未变化。

这些是 UI 调度探针，文件系统缓存未清理。较早候选 P95 10.65 / 最大 25.67 ms；一次对称图标构建在并行手动实例存在时为 22.54 / 106.48 ms，接着旧 0.1.24 为 20.87 / 58.62 ms。不同时间 / 进程负载不构成受控性能对照，不能归因于图标或据最终单轮宣称无尖峰，继续跟踪已有滚动延迟待办。新增元数据与界面对象带来少量开销，不声称这轮让图库更快。

Windows HDR 在后续界面验证时关闭，强制要求实际 HDR 的完整 49 窗口套件因此失败（`artifacts/viewer-lifecycle-20261016-001417/`）；未降低断言或自动打开系统 HDR，保留较早本轮原生 SDK / FP16 成功记录并区分环境。Computer Use 列表可见窗口，但两次绑定均报 `window id … no longer belongs to …; current owner is …`，未能完成录屏 / 点击验收；实况声音的听感、精确音画同步与物理显示观感仍待人工验收。

## Live / 动态 RTX HDR 首轮（0.1.24，2026-10-15）

首轮实现并在本机 RTX 5070 Ti Laptop / 已开启 Windows HDR 的显示器验证。0.1.24 为本地候选，公开下载仍为 0.1.23，本轮不创建新标签或 Release。格式、公开样本来源和实现边界见 [LIVE-PHOTOS.md](LIVE-PHOTOS.md)。

- Core 48 项检查通过，其中 14 项新检查覆盖有边界的内嵌视频、现代 / 旧字段优先级、跨包显式关闭、截断 MP4、XML DTD、原生 PQ 标志、Samsung raw SEF 与 sefd、Apple 同名误配 / 重命名正确配对以及取消。JPEG / HEIC / MOV 真实资源另做集成测试，不用构造容器冒充可解码视频。
- 9 个真实动态资源通过实际 MediaPlayer + TrueHDR + FP16：Google JPEG / HEIC、Pixel 6 Pro / 8a、Samsung One UI 5 / 6 JPEG、One UI 6 HEIC、Apple HEIC + HEVC MOV、维护者小米 HDR + Live JPEG。每项均有多次 SDK 求值、不同时间的 FP16 图像指纹与大于 1 的高亮值；VSR 调用为 0。
- 维护者小米原件 14,202,351 字节，原始 3072×4096、增益图 nativeHdr=2 实际解码成功，静态画面真实 FP16 读回峰值 18.0（1024 预览，非屏幕实测 nits）；视频 1088×1440。只读前后 SHA256 一致。私人样本与公开测试资源均未加入 Git 或消费者包。

| 样本 | 视频尺寸 | 首帧 ms | 提交 P95 ms | SDK HDR 调用 | 实际 FP16 峰值 |
| --- | --- | ---: | ---: | ---: | ---: |
| 小米 | 1088×1440 | 1649.5 | 4.29 | 101 | 7.801 |
| Apple | 1440×1920 | 1525.2 | 2.48 | 103 | 7.488 |
| Google JPEG | 1024×768 | 1890.7 | 2.42 | 194 | 7.801 |
| Google HEIC | 1365×768 | 1427.2 | 2.15 | 200 | 7.801 |
| Pixel 6 Pro | 1440×1088 | 1680.6 | 2.67 | 88 | 7.801 |
| Pixel 8a | 1088×1440 | 1214.9 | 3.03 | 97 | 7.773 |
| Samsung One UI 5 JPEG | 1440×1080 | 1871.0 | 2.48 | 107 | 4.145 |
| Samsung One UI 6 JPEG | 1088×1440 | 1797.5 | 3.16 | 107 | 4.355 |
| Samsung One UI 6 HEIC | 1088×1440 | 1283.8 | 2.45 | 63 | 6.863 |

上表是单次诊断运行，首帧从开始创建播放实例到首次提交完成，包含媒体 / GPU / SDK 初始化；文件系统缓存未清理。提交耗时包含 CopyFrame / SDK / Present 的 CPU 等待，并非 GPU 全部耗时、物理输入延迟或稳定帧率。SDK 计数不是视频帧率；被合并的 VideoFrameAvailable 通知不作为丢帧率。

所有样本的循环前后自有 GPU 纹理字节相同。小米 / Apple 分别为 80,065,440 / 99,234,720 字节；这不包含播放器、SDK 内部和驱动的全部分配。两者 UI 定时回调超额延迟 P95 约 0.88 / 0.83 ms。原图对照时视频继续前进且新增 SDK 调用为 0；松开恢复增强，默认静音 / 声音开关状态通过。准备中取消、自然结束、重播、播放中关闭、静态 lease 恢复和隐藏 still renderer 不重绘检查通过。

普通看图回归：`verify_native.py` 的 SDR gamma / 色彩、ICC / EXIF、缩放 / 旋转、JXR 线性 FP16、真实 HDR-only / VSR-only / 组合、40 次缓存对照、毛玻璃读回通过。SDK-free 原生构建通过。49 个真实窗口、两次启动与持久 HDR-only 回归通过（`artifacts/viewer-lifecycle-20261015-212118/`）。先前一次用了含原生 HDR / 损坏文件的 native 测试目录，触发仅适用 SDR 夹具的 rapid-navigation 断言；换用明确的全 SDR 夹具后通过，未降低断言。

3043 张、约 58.9 GB 的真实高清相册只读回归通过（`artifacts/gallery-performance-20261015-210707/`）：最多实现 22 张卡片，44 次 WIC 缩略图加载、未变化刷新新增 0 次加载；峰值 private 192,471,040 字节，UI 回调 P95 8.80 ms / 最大 22.23 ms。首次可视缩略图约 1052 ms；这是内部探针且包含共享 UI 调度，不是物理显示测量。全目录成员 / 长度 / 修改时间与 32 个抽样原件 hash 保持不变。

本地完整 RTX 候选构建通过，EXE **61,128,398 字节（58.30 MiB）**，ZIP **85,705,657 字节（81.74 MiB）**，503 个载荷文件逐个解包 SHA256 一致。相比公开 0.1.23 安装器多 18,898 字节，属于成品比较，非功能代码净增量。没有新增运行时包，系统 HEVC / HEIF 需求不变。固定开发入口 `artifacts/Luma/Luma.App.exe` 已同步 0.1.24，501 文件与最终验证目录一致（安装包另含构建来源 / 运行条款文件）。构建保留脏源码候选标记；未创建 Release 或上传安装包。

主要报告：`artifacts/live-photos/matrix.json`、`core-final.log`、`native-regression.log`、`lifecycle-final.log`、`gallery-regression.log`。小米原件解码记录在私有忽略目录。Computer Use 实际尝试两次，窗口列表有结果但窗口绑定报错，未完成真实点击 / 录屏验收；代码级真实 WinUI 窗口检查不替代听感、精确音画同步和视觉过渡验收。这些项目、冷启动播放延迟及原生 HDR 视频 / .livp 已保留在 TODO。

## 公开 0.1.23 与商店技术准备（2026-10-15）

标签 `v0.1.23` 保持指向已验证的 `f688afc`。完整 [云端发布流程](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38715417474) 成功，消费者四文件与 API digest / SHA256SUMS 一致，503 文件安全解包，原始 NVIDIA DLL/PDF 与输入 SDK SHA256 核对通过。云端消费者包的安装器 61,109,500 字节、ZIP 85,692,027 字节；这是实际公开文件，不与上轮本地构建的精确字节数混用。

实际云端 `Luma.Native.dll` 在本机完成 SDK HDR-only、独立 / 组合 VSR、40 次缓存对照、线性原生 JXR、FP16 玻璃 / 读回回归；3840×2160 SDR 夹具仅 TrueHDR 输出高光 7.793 scRGB，VSR 调用为 0。云端 EXE 完成 49 个真实窗口 / 两次启动、工具栏交互及持久 HDR-only，报告 `artifacts/viewer-lifecycle-20261015-181017/`，约 38 秒是整轮验证耗时，不是启动延迟。正式 Release 已公开为非草稿、非预发布。

商店路线本轮只准备：独立 staging 的开发身份 MSIX，MakeAppx 架构 / 清单校验与 504 文件逐字节核对通过（87,358,265 字节），13 个关联、logo、原 NVIDIA DLL/PDF/条款、来源和 SHA256 一致。原 EXE 发布载荷不改动；占位身份 / 脏源码的提交防护、PowerShell 语法与三份工作流 actionlint 校验通过。300×300 透明商店 tile 从既有 master 生成并读回一致。真实 Store 身份、包的安装与应用激活、迁移、WACK 和商店认证未执行，不能以静态包检查代替上述结论。

## 安装包适度精简与覆盖升级（0.1.23，2026-10-15）

完整 RTX 包同轮测量：EXE **60.97 → 58.26 MiB**、ZIP **84.19 → 81.70 MiB**、程序目录 **194.07 → 193.93 MiB**。只调整 LZMA2 / ZIP 无损压缩与主程序 PDB 发布策略，完整 .NET / WinUI / NVIDIA / 语言 / 原生 HDR 能力保留；503 份 ZIP 文件、原 NVIDIA DLL / PDF / 条款、许可及 SHA256 比对通过。已有 245 份运行库 DLL 与基线逐字节一致，其余变化包括应用编译 / 链接元数据与构建信息，未手工剔除框架文件。新增每轮本地 `package-size.json`，不改变四份 Release 上传白名单。详见 [体积拆分、解码器依赖与实验边界](PACKAGE-SIZE.md)。

隔离安装 / 重复安装 / 卸载 / 13 格式关联 / 其他副本归属检查通过。升级补充用旧 0.1.17 载荷与新 0.1.23 的隔离安装身份，新安装调用不传 `/DIR`，正确找到旧自定义目录；保留独立 SQLite 图库、收藏 / 最近记录 / 主题 / 增强偏好、原图与缓存标记，真实图库和默认程序快照未变。验证脚本可通过 `--previous-installer` 重用这项检查，具体报告见体积审计。

最终消费者 DLL `verify_native.py` 的 SDK HDR-only / VSR-only / 组合、40 次缓存原图对照和 FP16 / JXR / 玻璃回归通过；最终 EXE 完成 49 个真实窗口、两次启动与偏好恢复，报告 `artifacts/viewer-lifecycle-20261015-161644/`。首次测试夹具含原生 HDR、或相册不足 / 位于末尾时不符合原检查前提，修正独立 SDR 测试相册后通过；不把那些失败误记为产品 HDR / 滚动缺陷。未采用的裁剪实验只完成局部验证，保留警告和未完成边界；不计入正式候选成功项。

这些是本地待验收候选，源码未清理提交前 `sourceDirty=true`；正式发布须从干净提交重新构建，并以新包的 `package-size.json` / SHA256 为准。本轮不创建标签或正式 Release。

## 关于链接与 README 精简（0.1.23 验收补充，2026-10-15）

“关于 Luma”加入原生项目 / 版本发布链接，使用默认浏览器打开，保留主题色、键盘操作与明确的辅助功能名称。项目首页及 `/releases` 的 HTTP HEAD 均为 200；没有将 Release 草稿或尚无正式公开下载的状态写成已发布。完整 RTX 消费者构建通过，现有设置页 WinUI 检查在浅 / 深色 × 1160 / 600 DIP 下通过，底部关于导出布局目视检查链接清楚、同一行且未裁切。报告 `artifacts/settings-20261015-125202/settings.json`，候选 `artifacts/Luma-about-links/Luma.App.exe`。本轮只改 XAML / 文档，没有重做与此改动无关的 HDR 观感结论。

README 删除维护者指定的首页图注、合并重复卖点，将开发命令折叠并精简 HDR 入门；保留真实创作动机、正式下载状态、硬件 / SDK / HEIC 条件和格式限制。相对链接目标、代码围栏与折叠标签完整性检查通过；全文字符数（含 Markdown / 链接 / 代码）4374 → 3338。图像本身未修改，素材来源记录仍保留在 `docs/images/README.md`。按最新验收安排仅本地提交，不推送。

## 用户说明、HDR 设置与校准入口（0.1.23，2026-10-15）

设置页改为跟随主题的原生卡片：开启 Windows HDR → 校准屏幕 → 欣赏照片，随后是外观、默认查看器与关于。格式和峰值说明折叠，README 按维护者实际的电影赏帧 / 自己照片的场景重写，新增 [HDR 屏幕准备指南](HDR-SETUP.md)。系统 HDR 通过公开显示设置入口由用户控制，不模拟系统快捷键。

- 最终 RTX 自包含消费者构建 `artifacts/Luma-hdr-setup-final/` 通过；版本 0.1.23，502 个文件共 **203491885 字节**。SDK-free 原生构建也通过；没有新增运行时包或依赖。
- `tests/verify_settings.py --app artifacts/Luma-hdr-setup-final/Luma.App.exe` 在独立 `--test-library` 中通过，报告 `artifacts/settings-20261015-123113/settings.json`：主页启动不构造设置页、不查询显示器；进入页面首次等待 **75.04 ms**（本次进程 / 桌面的异步等待，非冷启动或普遍性能承诺）；20 次原生窗口位置改变合并为 1 次查询，隐藏设置后的 12 次改变为 0，再次进入读取新数据。
- 真正运行 WinUI 以浅 / 深色 × 1160 / 600 DIP 宽度检查横向边界、滚动和按钮，导出四张页面及展开校准 / 底部关于的 XAML 图片，全部目视复核。注入 HDR 3675 / SDR / 不可用 / NaN / 无穷 / 负数状态，确认不将屏幕峰值截断到 RTX 上限、不在 HDR 关闭时宣称 HDR 峰值、不残留另一屏幕数据。注入状态不代表连接了对应亮度的物理屏幕。
- `tests/verify_display_info.py` 对最终 RTX 与 SDK-free DLL 均通过空指针 / 无效 HWND、实际显示器匹配、峰值与 SDR 白色的单位核对。读值与现有渲染器窗口路径相同：当前唯一屏幕 **NE160QDM-NZL / HDR 开启 / 峰值 624 nits / 全屏 624 nits / SDR 白色 480 nits**。最终 RTX 本次原生查询 8 次：首次 **13.43 ms**，中位 **0.273 ms**；它只测查询函数，不包括 UI / 启动，不代表实测面板亮度。报告 `artifacts/display-info-Luma-hdr-setup-final.json` 与 `artifacts/display-info-hdr-setup-standard.json`。设置查询不创建渲染器、D3D 设备、交换链或 NGX，不扫描图库。
- Computer Use 实际从隔离 Luma 点击“Windows HDR 设置”，确认进入 Windows“系统 → 屏幕”、可见 HDR 入口；实际点击“打开 HDR 校准”，确认已安装的 `MicrosoftCorporationII.WindowsHDRCalibration` 欢迎页，随后关闭本轮打开的校准与隔离 Luma。未修改 HDR 开关、安装软件或保存校准文件，原用户 Luma 继续运行。浅色 / 窄屏采用上述实际 WinUI 布局导出检查；桌面截图只证明页面与入口，不证明物理 HDR 观感。
- 校准路由的替身检查覆盖：已安装成功不再打开商店、未安装打开准确产品页、激活异常 / 商店不可用转网页、全部失败返回失败。实际安装应用的启动已验证；未安装机器的商店交互没有通过卸载来模拟，补验留在 TODO。商店产品与校准步骤已对照微软文档，来源见准备指南。
- 最终 RTX `verify_native.py` 真正完成 NVIDIA SDK HDR-only、VSR-only、组合、40 次原图缓存切换及 FP16 GPU 读回（3840×2160 合成照片高光约 7.793 scRGB），原生浮点 JXR / Gaussian 玻璃回归也通过。SDK-free 同一套基础渲染检查通过且 SDK 调用保持为 0；未把它算作 AI 成功。34 项核心 / 索引检查通过。

本轮不改变照片解码、HDR 映射和呈现逻辑；仅新增独立只读显示查询 ABI，现有 `Status` 不变。真实双屏 / 跨 DPI、未安装校准应用机器的商店交互尚需补验。候选：`artifacts/Luma-hdr-setup-final/Luma.App.exe`；完成后按既有授权提交并正常推送，不创建标签 / Release。

## 照片详情与后台元数据（0.1.22，2026-10-15）

顶部独立信息按钮 / I 快捷键打开可滚动详情；常用文件、日期、像素、相机 / 镜头 / 曝光、EXIF 色彩、实际 HDR 来源和有效 GPS 在前，其他参数分组搜索。复制路径、完整详情与原文件位置使用统一按钮。搜索输入不再误触发 I / R / 方向键等看图快捷键；普通控件的空格对照保留。具体范围及系统字段含义见 [照片详情说明](PHOTO-DETAILS.md)。文件创建时间与拍摄时间明确分开，系统每像素位数不作为 HDR 判断。

- 最终 RTX 自包含消费者构建通过，34 项核心 / 索引 / 格式化检查通过。`verify_photo_details.py` 使用实际 Windows 图像属性读取：合成带真实 EXIF / GPS 的 JPEG 返回 24 项，明确核对 1/4000 秒、f/3.5、ISO 160、0 EV、镜头、日期与坐标；维护者 Sony JPG 返回 28 项，Apple 增益 HEIC 返回 23 项。普通无 EXIF PNG / JPEG 不继承前图的拍摄字段，JPEG 增益图也可查看信息。
- 详情关闭时启动读取计数为 0；打开详情没有新增原生渲染处理任务，也没有新增 HDR / VSR 调用。后台并发限制 2，元数据缓存最多 64 条；返回照片复用缓存，生成图修改文件戳后重新读取。12 次并发切换仅显示最终照片信息；连续 5 次在发出读取请求后立即关闭窗口通过。原用户照片 SHA256、大小和修改时间未变，测试只更改自己生成的文件。
- 900×680、740×570 逻辑像素窗口中，详情和顶部入口都位于画布内，滚动视口仍可用；侧栏宽 340，较小窗口视口约 224 逻辑像素，缩略图栏让出右侧。亮 / 暗主题、摘要和滚到底部的 EXIF 区通过应用自身 `RenderTargetBitmap` 渲染及目视检查。图像使用 QA 临时中性底色，只有 XAML 文字 / 控件布局，不包含 FP16 照片，不能证明真实玻璃模糊或 HDR 物理观感。
- 最终六份样本后台读取约 28.23–100.63 ms，其中真实 Sony 约 33.78 ms；打开面板的等待约 0.26–113.94 ms。面板常在翻图期间提前完成读取，因此较小等待值不是一次完整读取时间。未清空系统文件缓存，也不代表其他存储或首次系统属性初始化的速度。清理收起侧栏时的取消源后完整复验通过，最终报告与三张渲染图在 `artifacts/photo-details-20261015-030351/`。
- Computer Use 找到实际窗口和顶部信息入口，随后捕获监视器失败 `IGraphicsCaptureItemInterop.CreateForMonitor ... 0x80070057`，重新选择 / 激活仍失败，未继续盲点坐标。另试完整 HDR/VSR 生命周期时，首项因 Windows 对窗口报告 **HDR 未开启** 而失败，保留在 `artifacts/viewer-lifecycle-20261015-024148/`，未计入成功。当前不宣称新的物理 HDR、真实键盘输入或 SDK 硬件复验通过；正常 HDR 桌面恢复后补验已记录 TODO。

本轮没有修改原生解码 / NVIDIA / FP16 / Gaussian 的 C++ 源码；重新链接的 DLL 整体哈希会变化，但可执行 `.text` 与 0.1.21 构建逐字节一致（SHA256 `e318ac0b9319a4b75fdba366181bd04dbe9f21a7ca35fe03df5ceb5052235e5d`）。这项比较不代替硬件复验。未新增运行时依赖、图像元数据数据库或导入时全相册扫描。

最终候选 `artifacts/Luma-photo-details-final/Luma.App.exe`。源码、说明、TODO 和验证一起提交，并按维护者授权推送；本轮不生成新标签 / Release，私有原片、截图及测试库不进入 Git 或消费者目录。

## RGB XMP 增益图兼容与可读 JPEG 回退（0.1.21，2026-10-15）

维护者下载的 JPG 未损坏：SDR 主图 1662×2493，Display P3 / sRGB 曲线，附加 RGB 增益图；三通道 GainMapMin / Max / Gamma 用 `rdf:Seq` 表达。上游仅支持部分单值 XMP 属性，错误地报缺少 GainMapMax。现已在自主代码用 XmlLite 处理命名空间、属性 / 元素、单值 / 三通道数组；上游 JPEG 容器与 gain 运算不改动，ISO 优先，Apple 曲线独立。

- 最终 RTX 自包含消费者构建与 SDK-free 原生构建通过。`verify_xmp_gainmaps.py` 对两版完成 7 种合法表达、12 种异常元数据、不可读主图、缩小预览和 ISO 优先检查。每种合法表达使用独立逐通道公式，实际 FP16 输出误差小于 0.02 scRGB；每种 20 次底图 / HDR 缓存切换恢复相同输出，无 AI 调用。异常 XMP 显示可读底图并返回明确警告；真正损坏主图仍失败。恶意 DTD 样本会产生上游预期解析拒绝日志，随后兼容回退通过，不是测试失败。
- 用户原图在最终 RTX 核心完整解码约 508.85 ms，SDK-free 约 472.63 ms；未清空系统文件缓存，不含 UI / 上传，不能当作普遍或冷启动速度。按 Pillow JPEG 独立像素、XML 元数据、sRGB 反变换和 Display P3→709 数学对照，抽样平均误差 **0.0007215 scRGB**、最大 **0.02714**；HDR 源抽样峰值 **12.25915 scRGB**，真实 FP16 读回与缓存像素的中心误差 **0.00003174**。测试输出强制 203 nits 白色 / 256 倍 headroom 以单独验证完整重建，不表示物理显示器测得该峰值。
- 原文件 SHA256 始终为 `56527fcaa59a3f04b5efe9e0db29e32e7bbb194d80c69436ff71139809087e55`，大小和修改时间未变；图片没有进入 Git 或消费者包。报告 `artifacts/xmp-gainmaps/xmp-Luma-xmp-arrays.json` 与 `xmp-xmp-standard-native.json`。
- 两版 `verify_hdr_formats.py` 的既有 scalar XMP、ISO、Apple JPEG 独立参考 / FP16 / 底图对照、方向、大图预算和缩略图检查通过；更新破损增益图的预期为明确 SDR 兼容预览。两版 `verify_native_hdr_tuning.py` 的 5 种 HDR 来源 / 数学显示参数 / Gaussian 场景缓存 / 原片恢复仍通过。
- 最终 WinUI 在隔离库完成 **20 项记录**，包括真实用户 JPG、RGB 与 Gamma 数组、Apple / ISO / PQ HEIC、普通 SDR、异常数组兼容预览、原生 HDR 调参与重新打开 HEIC。兼容预览没有错误遮罩，增强控件明确禁用，即使请求 HDR / VSR 也不新增处理任务；原生 HDR 全部跳过 SDK。报告 `artifacts/xmp-windows-final-heic/hdr-format-windows.json`。
- 最终消费者程序通过 **49 个真实预览窗口、两次新进程**，覆盖实际 SDK HDR-only / VSR-only / 组合、忙时关闭、偏好恢复和浮层玻璃；报告 `artifacts/viewer-lifecycle-20261015-012723/`。28 项核心 / 索引检查通过。
- Computer Use 实际打开同一张用户 JPG，完整适配正常、无加载错误，显示“原生 HDR · 增益图 JPEG”，增强面板正确识别自带增益图，提供“按住查看 SDR 底图”。重新观察打开动画完成后的状态；截图只用于检查图像 / 布局和控件，不用于判断屏幕 HDR 亮度。维护者原应用没有关闭；只关闭本轮隔离测试窗口。

候选：`artifacts/Luma-xmp-arrays/Luma.App.exe`，502 个文件共 **203320783 字节**，原生 DLL **1254912 字节**。本轮 CI 新增合成 RGB XMP / 兼容回退检查，云端不读取私有照片。完成验证后按维护者新授权提交并正常推送；不创建新标签或 Release。

## 原生 HDR 显示控制与悬浮界面（0.1.20，2026-10-15）

原生 HDR 新增独立保存的曝光 / 增益参考与峰值设置，自动模式保留之前的映射；手动参数仅改 GPU 呈现，不改缓存帧、不重新解码、不运行 NVIDIA。具体公式、范围与 PQ / JXR 无 SDR 底图的含义见 [HDR 格式说明](HDR-FORMATS.md)。状态改为右上角主题毛玻璃，窄窗口避免与工具栏 / 信息面板重叠；增强面板统一操作行高、圆角、展开内容宽度及数值；工具栏新增一键适应窗口。旧 popup 背景在尺寸变化期间按原照片坐标放置，不先拉伸再替换；快速调整取消过时的读回任务，并在取得渲染锁后才分配像素缓冲。原生 HDR 打开增强面板无需初始化 NVIDIA SDK。

- 最终 RTX 自包含构建和 SDK-free 原生构建通过。`tests/verify_native_hdr_tuning.py` 对增益 JPEG、浮点 JXR、Apple / ISO HEIC、PQ HEIC，每种 5 组参数，通过实际 FP16 目标检查增益 / 曝光 / 峰值数学、自动恢复、按住原片及松开恢复、无新增 SDK 调用或 GPU 图片分配。开启真实 Gaussian 场景缓存后再检查对照，覆盖绝对 HDR 在纹理不变时仍须刷新显示调整的情形。最终两版报告独立保存在 `artifacts/hdr-controls-verification/tuning-rtx.json` 与 `tuning-standard.json`。
- 最终 `verify_heic.py` 对维护者 iPhone 15、公开相机、iPadOS ISO 与 Apple PQ 的独立 libheif / 数学 / FP16 对照通过；原始文件大小、修改时间和哈希未变，SDR HEIC、别名及三种破损文件拒绝仍通过。源报告 `artifacts/heic-verification/heic.json`，最终副本在本轮 verification 目录。此检查仍是 512 长边，不代替 2048 的性能测量。
- `artifacts/hdr-controls-windows-final/hdr-format-windows.json` 完整通过，共 15 项记录，含三类 HEIC 的手动数值、零新增处理任务、亮 / 暗主题、右上角玻璃、专用适应窗口按钮、原生 HDR 请求增强仍跳过 SDK，以及关闭 / 重新打开。
- `viewer-lifecycle-20261015-001522/` 的最终消费者程序通过 49 个真实预览窗口、两次新进程，含实际 HDR-only / VSR-only / 组合、忙时关闭、重叠请求、偏好恢复与 5 次展开 popup。增加风格下拉打开时父背景对象不重建、展开时旧图像刷按坐标对齐的检查。
- 最终 `viewer-interaction-20261015-001602/viewer-interaction.json` 完整通过。真实 7008×4672 JPG 所在相册：6 次混合缓存随机首图中位 118.5 ms、最大 247 ms；10 次预读首图中位 5.5 ms、最大 6 ms。40 次缓存原图 / HDR 选择中位 0.05 ms、最大 0.10 ms，无新增处理任务或 AI。24 次图库尺寸更新到两次 Composition 回调中位 31.1 ms、最大 68.4 ms；这些不是物理屏幕帧间隔，原有调度峰值继续待办。
- Computer Use 实际打开公开 Apple 相机 HEIC，检查深色右上角状态、适应窗口图标、统一圆角 / 行高的增强面板、展开后数值与手动开关。最初抓取到展开动画进行中的裁切帧，随后重新观察完成状态，内容与圆角正常；没有把中间动画帧当作最终布局，也没有用 SDR 截图亮度验证 HDR 观感。测试进程已关闭，维护者原来的 Luma 窗口未关闭。

Apple 官方 PQ 的慢加载确认存在：10 张、2048 长边各解码三次，中位 1113.53–2030.51 ms，报告 `artifacts/native-hdr-tuning/apple-pq-decode.json`。未清空系统文件缓存，不含窗口 / 上传，也不清楚所有硬件的表现。codec float 缩小接口 A/B 对首张约 1965.62 / 2090.72 ms，81 个 RGB 抽样点一致但无收益，已撤回，试验报告保留。此项未声称解决。交互脚本第一次误用了不足 12 张的 native-tests 小目录，校验明确失败；换用真实 Screenshots 相册后完成，失败证据保留，不计入成功验证。

28 项核心 / 索引检查通过。最终候选 `artifacts/Luma-hdr-controls/Luma.App.exe`，502 个文件共 203292956 字节，原生 DLL 1234432 字节；相对 0.1.19 的 DLL 增加 2048 字节。样本、数据库、日志、测试 exe / oracle 未进入消费者目录，NVIDIA 运行库和许可仍保持原始字节。此轮仅本地提交，不推送、不生成新公开 Release 或替换已安装版本。

## iPhone HEIC 与校准峰值（0.1.19，2026-10-14）

本轮完成维护者要求的 HEIC 原片支持，详见 [HDR 格式说明](HDR-FORMATS.md)。使用系统 WIC HEIF/HEVC，未增加消费者 HEVC/libheif 依赖。实际支持 Apple 增益图 HEIC、ISO tmap HEIC、浮点 PQ HEIC 和普通 SDR HEIC；同步更新索引、文件激活、选择器及安装包清单。修正 Apple 线性 XMP headroom 被通用解析器当成 log2、Apple 增益被当作 ISO 的问题；下面 0.1.18 的 Apple 数字是当时对通用参考库的比较，不能继续作为正确 Apple 映射的证据。

`tests/verify_heic.py` 使用 QA 专用 pillow-heif/libheif 与 numpy 独立解码、ST.2084 / Apple / ISO 公式，实际 GPU FP16 读回、SDR 选择和缓存切换；所有测试文件 hash 与修改时间前后一致。报告 `artifacts/heic-verification/heic.json`。样本下载地址与 SHA256 固定在 `scripts/Get-HdrSamples.ps1`，第三方样本与测试依赖只保留在忽略的 artifacts，不进入应用或仓库。

| 输入 | 验证结果（512 长边，单次解码，不等于界面首帧） |
| --- | --- |
| 维护者 iPhone 15，24 MP / iOS 17.6.1 | 原生 HDR 类型 3；headroom 5.154915；约 430–500 ms；独立参照平均 / 最大通道差约 0.0772 / 1.498 scRGB |
| 公开相机 Apple HEIC，1512×850 | 原生 HDR 类型 3；headroom 7.371721；约 68–137 ms；平均 / 最大差 0.0976 / 7.175 |
| iPadOS 26 ISO HEIC，2752×2064 | 原生 HDR 类型 4；headroom 10；约 144–153 ms；平均 / 最大差 0.1509 / 3.645 |
| Apple 官方 PQ HEIC，2960×3948 | 原生 HDR 类型 1；ST.2084 绝对亮度与 Rec.2020→709 验证；约 206–372 ms；平均 / 最大差 0.0302 / 1.786 |

两套 HEVC 解码、色度及缩放滤波存在差异，边缘最大值明显高于平均值，未声称逐像素一致或全部 iPhone/iOS 认证。三种 HEIC 高光实际保留到 FP16；原生 HDR 下即使请求 HDR 与 VSR，SDK 两个调用计数仍为 0。40 次纹理选择无重解码或 AI；普通 SDR HEIC、.heif 别名、三种破损输入拒绝、SDR 增益底图缩略图通过。首次 HEIC 仍由系统 HEVC 处理，未声称全部照片瞬时打开。Apple 8-bit 曲线使用查找表，预算包含 HDR、SDR 和原始色域底图。

亮度适配与原始数值校验分开：原始 HDR 缓存以 203 nits 参考白表示；显示增益图时以 Windows SDR 白色为参考并按 peak/white 比应用对数权重。PQ / 原始 scRGB 保持绝对亮度。`tests/verify_hdr_peak.py` 在真实 FP16 GPU 目标检查 203 / 406 / 812 / 1600 nits 端点和当前 Windows 峰值。Windows 本次报告 **3675.7439 nits、SDR 白色 480 nits**，足够 headroom 的白色测试读回约 **3675 nits**；不足 headroom 的测试保持自己的范围，不强行填满屏幕。误差在 FP16 量化范围内。该结果证明数值表达和映射，未使用亮度计测量物理面板。峰值来源沿用 RTX 的 DXGI 输出查询，系统 MHC 校准可覆盖该信息。TrueHDR 的参数仍遵守 SDK 规定的 400–2000 nits，原生增益图没有这个 SDK 上限。

最终隔离 WinUI 报告 `artifacts/heic-windows-reopen/hdr-format-windows.json`：9 个 JPEG/HEIC 混合输入、原生来源文字、FP16、SDK 跳过及无新处理任务的空格对照全部通过；再关闭并重新打开 Apple / ISO / PQ HEIC，3 次均实际复用渲染设备并维持正确 HDR 类型。Computer Use 实际打开维护者原片，观察到完整比例适配、Apple HEIC 来源、两个禁用的增强开关和 SDR 底图对照。截图只检查布局/控件，不用其过曝推断 HDR 物理观感。

Release WinUI 与 SDK-free native 构建通过；28 项核心索引/激活/离线/设置检查、SDR 缩略图 ICC/EXIF/alpha 检查，以及 TrueHDR-only（VSR 0）、独立 VSR、组合 VSR、40 次缓存对照、真实照片玻璃、FP16、mip/预算回归通过。SDK-free 构建也通过 JPEG、HEIC 和 GPU 峰值检查。交互报告 `artifacts/viewer-interaction-20261014-201729/viewer-interaction.json`：小 JPEG 随机/预读首帧中位 9/3 ms；40 次缓存 HDR 对照调用中位 0.03 ms、最大 0.07 ms，无额外 worker/AI。这些调用时间不是显示器扫描延迟。24 次图库尺寸合成回调中位 27.5 ms、最大 70.7 ms，既有复杂图库布局性能待办仍未全部解决。

可运行候选为 `artifacts/Luma-heic/Luma.App.exe`，0.1.19，自包含 x64。原生 DLL 1232384 字节，比 0.1.18 增加 45056 字节（44 KiB）；HEIC 没有增加分发解码运行库。没有替换正式安装、改变默认关联、生成新安装包或推送发布。HLG 的完整验证、更多相机 ISO 原片、高位深增益底图、复杂 ICC/逆向 HDR 基底、PQ/JXR 的低峰值色调映射仍在 TODO。

## 原生 HDR 照片格式（0.1.18，2026-10-14）

调研及范围见 [HDR 格式说明](HDR-FORMATS.md)。本轮新增 Google 参考库解析的 XMP / ISO / Apple 增益图 JPEG，经按显示尺寸的 WIC 双图解码、官方 gain-map 运算、203/80 参考白转换与 P3/Rec.2020→线性 Rec.709 后进入现有 FP16 / scRGB。兼容 SDR 底图同时保留并计入预算，用于 SDR 屏幕和缓存空格对照；原生 HDR 不运行 NVIDIA SDR→HDR 或 VSR。图库小卡片另有 SDR 底图解码入口，浮点 JXR 缩略图继续交由 Windows，避免为缩略图做完整 HDR 解码。

最终依赖为固定 libultrahdr 2.0.2 提交与当前稳定 libjpeg-turbo 3.2.0 官方发行源码；输入 SHA256 和 NASM SIMD 构建工具均固定，未修改上游源码。standard 与 RTX 都可显示原生 HDR，后者仍保留原有增强能力；没有安装系统编解码扩展、增加未支持格式关联、修改默认查看器或推送任何发布。原生 DLL 由既有 349696 字节增加到约 1.2 MB，约增加 0.8 MiB；消费者包保留四份原始 codec 许可/声明，不含源代码、构建工具、参考 EXE 或照片样本。

`tests/verify_hdr_formats.py` 使用独立参考 EXE 的完整 C API 解码作为比较，同时用恒定 4 倍增益灰色测试验证解析和亮度比例。XMP 测试有 MPF 与 Container Directory，ISO 测试由官方编码器生成；两份 Apple JPEG 保留在忽略的工具目录，没有加入公开源代码。两个 native 构建均通过，报告在 `artifacts/hdr-formats/hdr-formats-standard.json` 和 `hdr-formats-rtx.json`。

| 样本 | 本机观测 / 结果 |
| --- | --- |
| XMP-only JPEG | HDR 2 类型，高光 4.6314 scRGB；采样与独立参考一致，符合解析增益 × 203/80 的解析公式 |
| ISO 21496-1 JPEG | 高光 10.1302；与参考采样通道平均误差 0.00739，最大 0.05947 scRGB |
| 两份 Apple JPEG | 高光约 9.627 / 18.964；平均通道误差 0.00616 / 0.00759，最大约 0.563 / 0.660 scRGB，集中于彩色边缘。WIC RGB 与参考 YCbCr 采用不同解码/色度插值路径，未声称逐像素一致或所有 ICC 均已认证 |
| 48 MP 合成 JPEG | 8000×6000 → 2048×1536，约 253–290 ms，HDR + SDR 帧 62914560 字节；没有先创建 48 MP HDR 输出。不是真实相机随机纹理的性能基线 |
| 6 张用户 Windows JXR | 原生浮点高光约 6.0–12.68，真实 FP16 读回差异小于 0.03；首张 4K 文件 2048 长边约 439–459 ms，其他文件约 282–1016 ms。实际包括长文件名和不同窗口尺寸，没有复制/修改原文件 |

四种 JPEG 分别完成 40 次真实 GPU HDR / SDR 底图切换，回到 HDR 后 FP16 像素保持一致，没有额外 SDK 调用。缩小预览保留原始尺寸；缩略图返回 SDR BGRA；EXIF 旋转、普通 JPEG 不误认 HDR、截断增益图错误返回及空句柄检查通过。使用无 NVIDIA 初始化的真实 D3D11/FP16 渲染器验证，与 RTX 模型无关。

最终 WinUI `--verify-hdr-formats` 在独立数据目录实际翻过 XMP、ISO、两份 Apple、普通 JPEG 与用户 JXR，状态与帧类型一致、FP16 有效；即使请求两个增强开关，原生 HDR 的两种调用增量仍为 0，缓存对照没有新处理任务。报告 `artifacts/hdr-formats/windows-final/hdr-format-windows.json`。Computer Use 实际打开 JXR，确认完整适配、原生 HDR 来源文字和禁用的两个增强开关；截图只用于窗口/控件观察，不将 SDR 截图的过曝作为屏幕 HDR 亮度证据。

Release 构建、27 项图库/索引核心检查、常见缩略图像素与透明度/ICC/EXIF 检查通过。原有真实 TrueHDR-only、独立与组合 VSR、40 次缓存对照、FP16 高光和 SDK-free 路径也通过。最终应用的交互回归 `viewer-interaction-20261014-152518/viewer-interaction.json` 完整通过：40 次缓存 HDR 对照没有新增 AI/处理任务，调用中位约 0.04 ms、最大 0.13 ms；普通小 JPEG 的随机 / 预读首帧中位约 8.5 / 4 ms。24 次图库尺寸合成回调最大仍约 80.7 ms，这不是本轮格式解码的性能证明，也不代表已有图库布局待办已解决。

最终候选在 `artifacts/Luma-hdr-formats/Luma.App.exe`，502 个文件共 203210468 字节；原生 DLL 1187328 字节，对旧 349696 字节增加 837632 字节（约 0.80 MiB）。所有 codec 许可/声明来自原始输入，消费者目录无参考 EXE、源码、静态库或照片。本轮未重新生成 EXE 安装包，也没有推送 / 发布。仍未覆盖所有手机源文件、HEIC / AVIF、全部 HDR 曲线/ICC、低峰值显示器自适应与完整安装升级；这些范围保留在 TODO，不作能力承诺。设置页版本文字改为读取程序集，并准确显示新增格式范围。

## 主页启动期间的窗口响应（2026-10-14）

用户反馈主页启动后短时间拖动卡顿、随后恢复，预览窗口正常。此前图库压力测试在初始加载及自动刷新后才开始探测，不能覆盖该时段。本轮新增 `--verify-gallery-startup` 和只读 SQLite backup 的脚本，在构造窗口之前启动 UI 回调探测，区分隐藏的初始布局、已显示窗口的自动刷新与稳定阶段；每次进程执行 100 次原生 AppWindow 移动。正式数据库、照片、缓存和默认关联没有被写入，测试数据库和缩略图缓存均位于独立 artifacts 目录。

实际发现启动会触发两次相册页面加载；窗口原先在相册卡片创建之前显示，之后再为可见页面准备控件和扫描进度；每次完整刷新即使内容不变，也执行每张照片的 SQL 操作。调整为相册初始布局及进度控件准备后显示窗口，只加载一次初始页；设置页用首次创建的原生 UserControl 保留主题与按钮行为，未使用 Window 根下不兼容的 FindName/x:Load 路径（[微软说明](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/x-load-attribute)）。扫描服务使用后台续接，完整且未变化的在线相册跳过数据库事务，离线恢复、增删改和不完整扫描仍保留既有行为；页面无变化时不重跑 Reload。自动刷新入口和 SDR 缩略图的 WinUI 位图创建/上传排到低优先级，等待期间仍受原有四槽及像素池预算约束。

样本为本机 4 个相册、4483 条已存索引（含完整 3043 张与 1299 张相机相册），原照片只读，不清空 Windows 文件缓存。基线包含相同探针；最终应用为 `artifacts/Luma-startup-fixed/Luma.App.exe`，两轮报告 `gallery-startup-final-priority/run-*/gallery-startup.json`。紧邻其后的基线保存在 `gallery-startup-baseline-last/run-*/gallery-startup.json`：

| 指标 | 基线两轮 | 最终两轮 |
| --- | --- | --- |
| 探针至主页显示及准备完成 | 465 / 470 ms | 480 / 516 ms |
| 可见窗口自动刷新阶段 UI 回调 P95 | 9.40 / 8.56 ms | 6.80 / 7.33 ms |
| 同阶段 UI 回调最大 | 44.80 / 33.36 ms | 12.01 / 19.24 ms |
| 启动至测量结束的 managed 分配 | 15.08 / 14.01 MB | 9.17 / 10.17 MB |

优先输入没有让后台扫描更快：最终刷新结束为约 1.54 / 1.62 秒，基线约 1.48 / 1.48 秒；窗口显示也增加了准备实际内容的几十毫秒。数字是当前负载下的少量本机样本，不是所有设备保证、物理鼠标至屏幕延迟或严格重启冷启动。早期尚未定稿的样本曾出现 7–9 ms 峰值，后续负载下有更大峰值，因此以上只列最终实现并保留所有中间报告。隐藏布局的 65–71 ms 回调峰值没有被算成已可见窗口的拖动延迟，也没有删除这些原始样本。

验证通过：Release 自包含构建；27 项核心检查，包括持有独立 SQLite 连接观察 `PRAGMA data_version` 证明未变化刷新不写数据库，以及使用旧 Album 对象从离线恢复；最终两次启动均验证设置页确实延迟创建、首次/重复打开和主题选择；96 张 BMP 的真实虚拟化、过时任务取消、刷新合并、未变化刷新读取 0；`viewer-interaction-20261014-120939` 的真实窗口开图、缩放/平移、24 次布局缩放、损坏封面回退和删除条目刷新通过，40 次缓存 HDR 原图对照中位 0.04 ms、最大 0.08 ms，没有新增 AI 调用或 worker 任务。此回归实际完成 HDR SDK 处理和 FP16 原图对照，但没有宣称本轮重新测量 VSR 观感。

Computer Use 实际移动隔离主页，窗口位置从 (419,164) 到 (494,224)，拖动后相册布局完整；最终按需设置页的主题、默认应用按钮、许可说明和滚动控件已实际打开并观察。单次拖动后截图不能证明启动拖动全过程不存在黑块。快滚检查仍出现 64 ms 级别的回调峰值、窗口尺寸检查最大约 68 ms，相关连续帧/跨屏和长相册性能待办保留，未把启动优化写成整个图库性能已全部解决。本轮仅本地构建与提交，不推送或重新发布现有标签。

## GitHub 产品展示图（2026-10-13）

使用内置 imagegen 生成横屏深色产品展示，并通过同一图像工具校准官方山脊 logo、缩略图滚动条和计数位置。仅参考当前 XAML 布局、控件结构和仓库 logo；风景、卡片和相册信息均为全新虚构，没有读取或上传本地相册照片。最终原生 PNG 1672×941（约 16:9）、2238450 字节，两轮结果已目视检查；PNG 完整解码和 README 相对引用检查通过。README 明确说明产品示意性质，不将静态宣传图作为真实 HDR 显示、SDK 生效或性能证据。[原图及提示词记录](images/README.md) 保留生成来源。该变更只涉及展示素材与文档，应用行为没有改变。

## 0.1.17 完整 RTX 自动出包修复

用户要求实际解决标签无法生成完整 RTX 安装包的问题。采用维护者已取得的官方 SDK 原包，保存在独立私有构建仓库；一次性配置只读 deploy key、精确提交与 SDK SHA256，私钥在内存生成并直接通过 GitHub 公钥加密到 Actions secret，没有保存到本机文件或公开仓库。实际 API 返回私有仓库和 read-only 公钥，三个变量及 secret 已存在。

将源码/SDK 编译、完整安装包与私有 Release 草稿的生成从正式公开发行决定中分开。编译仍要求真实 SDK 和校验值，RTX 包必须包含独立运行库条款与原始许可 PDF；公开上传白名单限定为 EXE、ZIP、校验值和构建信息。安装器提供接受前读取原始 PDF 的按钮，MIT 不扩展至 NVIDIA 组件。此工作未代发 NVIDIA 通知或取得商标批准，也不代表 Microsoft Store 认证。

本地候选编译成功，500 个便携文件、完整 RTX 能力、原始 DLL/PDF、独立条款和 SHA256 核对通过。使用该候选包的原生 DLL 实际运行于 RTX 5070 Ti Laptop / HDR 开启屏幕：TrueHDR 成功结果 `0x00000001`，3840×2160 JPG 高光 12.656 scRGB，HDR-only 的 VSR 调用为 0；独立 VSR、组合、缓存与 40 次原图对照、浮点 JPEG XR、FP16 毛玻璃、色彩/方向检查通过。云端标签与消费者包下载验证随后按实际结果补充，不把本地结果当作云端完成。

首次消费者窗口检查把 `native-tests/highlights.jpg` 作为相册入口，目录同时包含浮点 `linear-hdr.jxr` 等混合夹具；快速翻页最终落到原生 HDR 图片，既有 SDR 增强生命周期测试却要求 `HdrEffective=1` 而失败。保留 `viewer-lifecycle-20261013-232106` 的失败报告，未修改产品或削弱 HDR 断言；改用独立的 16 张自有 SDR JPG 夹具验证该场景，原生 HDR 的正确旁路仍由独立 FP16 检查覆盖。

正确 SDR 夹具的 `viewer-lifecycle-20261013-233242` 完整通过：48 个真实预览窗口、HDR-only/VSR-only/组合、多个窗口、忙时关闭与快速翻页，再启动一个窗口确认保存 HDR-only，合计 49 个窗口。完整 RTX 安装、重复安装、卸载、另一副本注册保留及真实图库/默认关联未改动检查通过；两个非法 SDK SHA256 输入均在解压前拒绝，原始运行库字节未改变。actionlint 与公共源码边界校验通过。

### 实际云端标签和消费者包（2026-10-14 完成）

标签 `v0.1.17` 固定源码 `ccd3473e5e00ac9ef28199e977a15588afcda74c`。[完整 RTX 发布运行 38424366673](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38424366673) 的 package / draft 两个任务均成功，私有 SDK 取得、精确输入 SHA256、SDK 链接原生检查、500 个便携文件、11 种关联、隔离安装/重复安装/卸载及另一副本注册保护全部通过。独立 [standard CI 38424361806](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38424361806) 也通过。Release 草稿 `411848447` 自动创建，4 个资产上传完成；没有 SDK ZIP、开发工具、头文件、库、密钥或原照片。

实际从 Release 下载 EXE、ZIP、构建信息和校验值，不以本机候选替代云端包。与 GitHub asset digest、SHA256SUMS 和包内构建信息一致，源码干净、能力为完整 RTX，SDK 输入固定 `5ac1070927e9a89c15ffe09a1232a802b65a9a87`。500 个文件全部完整提取，NVIDIA DLL/PDF 与官方原件逐字节相同，运行库条款哈希和新图标一致。EXE 63629654 字节（60.68 MiB）、ZIP 87815862 字节（83.75 MiB）、展开约 192.92 MiB。下载及解压验证报告在 `artifacts/release-preparation/cloud-consumer-verification.json`。

| 云端资产 | SHA256 |
| --- | --- |
| `Luma-0.1.17-windows-x64-rtx-setup.exe` | `290b1ab932fc91ee2cb4258f19cd0e712982ca2e458f395e81e1f69a00f66802` |
| `Luma-0.1.17-windows-x64-rtx-portable.zip` | `85f4f716c80c9c20ef57db36fc4328deeb254ef1e6998d218e02c44ec25cce8a` |

云端包的原生 DLL 在实际 RTX 5070 Ti Laptop / HDR 开启屏幕重新运行：SDK 成功结果 `0x00000001`，4K JPG 高光 12.656 scRGB，HDR-only 的 VSR 调用为 0；VSR-only、组合、40 次缓存对照、原生 HDR 旁路及 FP16 像素检查通过。云端 EXE 的 `viewer-lifecycle-20261013-235834` 完整通过 49 个真实窗口、关闭重开、并发/忙时关闭、偏好恢复；`viewer-interaction-20261014-000935` 的导航、缩放平移、全屏、40 次原图对照及图库刷新检查也通过。对照提交中位 0.06 ms、最大 0.16 ms，没有新增处理任务或 AI 调用；这是提交计时，不是屏幕端到端延迟。24 次图库尺寸/Composition 采样最大仍出现 120.2 ms，不将其归为性能已解决，既有性能待办保留。

Computer Use 实际观察云端消费者窗口，状态为“RTX HDR 已生效”，控件和缩略图可显示；截图仅用于布局，不衡量 HDR 亮度。用户于 2026-10-14 确认 HDR 正常生效，重复检查停止。当前包仍未签名，Release 保持草稿，正式公开下载、商标/商业发行事项和 Store 上架没有自动宣称完成。

## v0.1.16 标签发布试跑（2026-10-13）

用户明确要求发布当前版本标签进行验证。附注标签 `v0.1.16` 已推送，标签对象 `3e434bee863479abcd91cddc7099ff631004181a`，剥离后指向 `d07199c1ba9c6ad42fba081da1cbfe945eb3f219`；本地与远程一致，Tagger 为 OpenAI Codex，附注保留已确认的人类共同作者。标签对应版本为项目 `0.1.16` / MSIX `0.1.16.0`，对应源码的 [standard CI](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38408480614) 已完整通过。

[标签工作流 38411915122](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38411915122) 实际触发并完成：精确检出源码成功，RTX 分发前置检查失败，后续 SDK 下载、RTX 构建、安装验证与 Release 草稿任务均跳过。只读检查确认仓库 Actions 的 secrets / variables 列表为空，源码未包含 `packaging/RTX-EULA.rtf`；没有自动打开分发门槛或改用基础版冒充 RTX。远程为 1 个标签、0 个 Release。本次证明标签触发与前置阻断有效，不代表 RTX 安装包的云端发布已完成。后续发布配置继续记录于 TODO，保留本标签指向。

## 开源与安装流水线准备（2026-10-13）

公开名称确定为 Luma HDR Photo Viewer；应用短名、EXE 与图库路径仍为 Luma。公开源码、正式下载版和宣传分开执行；下方历史记录中的“无远程、未公开”描述的是当时状态。GitHub 云端检查及仓库状态随后按真实结果补充。

源码已推送至 `lueluelue12138/luma-hdr-photo-viewer`，默认分支 main，工作流默认权限为只读。首次云端运行 `38399187963` 成功通过检出、源码边界、.NET SDK 和 25 项图库检查；原生编译与部分渲染检查执行后，在输出中文状态时遇到英文 Windows 的 cp1252 编码错误而失败。没有跳过断言，改为对两条工作流显式设置 Python UTF-8 后重新运行。许可范围补充说明保留于第三方声明，根 LICENSE 使用标准 MIT 正文以便 GitHub 正确识别。

第二次运行 `38400641761` 已通过 UTF-8 输出、基本 SDR 渲染和原生 HDR JPEG XR 的 FP16 读回；随后发现原测试把 SDR 照片毛玻璃的数值固定要求为 >1，仅在本机提高的 HDR SDR 白电平下成立。修正 SDR 色差断言的亮度单位，并另用真实浮点 HDR 数据检查毛玻璃始终保留 >1 的 FP16 值及不改变面板外照片。没有据此改动产品渲染或把虚拟构建机当作 NVIDIA 效果验证。

第三次运行 `38403555325` 通过 SDK-free 原生渲染、完整 WinUI 构建、496 个便携文件核对、EXE 生成与首次安装/重复安装/卸载；随后立即重新安装的测试遇到卸载清理竞态。实际日志显示原卸载进程退出后还在删除自己的文件，新安装选用 `unins001.exe`，而测试固定调用 `unins000.exe`。改为读取隔离卸载注册路径并验证它仍在测试目录内，等待真实程序/卸载文件和测试卸载键删除完成后再安装；未改动正式安装器或跳过卸载断言。保留失败日志与诊断产物。

[第四次云端运行 38406378664](https://github.com/lueluelue12138/luma-hdr-photo-viewer/actions/runs/38406378664) 对应源码 `9630add93450aa0af24decd133c1377023e5d712`，全部步骤通过：25 项图库检查、SDK-free WIC / D3D11 / FP16 / 真实浮点 JPEG XR / 面板模糊、完整 WinUI 构建、496 个便携文件与许可/校验值、EXE 生成、隔离安装/重复安装/卸载及其他副本的注册归属保留。Actions 产物 `Luma-standard-no-RTX-4` 上传成功，保留 14 天；没有 NVIDIA SDK、原照片或真实图库数据，也不具备 RTX HDR/VSR 能力。正式 RTX 标签流程仅完成配置、静态校验与本地候选验证，私有 SDK、许可门槛和标签到草稿的云端端到端检查继续待办，没有创建 Release 或标签。

- 本地 SDK-free WinUI / 原生发布成功，standard 包 496 个文件逐字节核对、EXE 头、能力标签和 SHA256 通过；隔离身份安装、重复安装、11 种关联和卸载通过。另模拟便携副本重新注册，卸载保留它的注册；实际图库数据库 SHA256 和默认关联快照前后相同。
- 本地 RTX 发布成功，499 个文件逐字节核对，原始 NVIDIA DLL / PDF 保留，候选 EXE 约 60.7 MiB、ZIP 约 83.7 MiB。只留本机，不代表完成完整 RTX 分发审批或代码签名。
- 25 项图库检查、SDK-free WIC / D3D11 / FP16 / 原生 HDR / 模糊检查通过。另在实际 RTX 5070 Ti Laptop 与启用 HDR 的显示器上重新检查真实 TrueHDR-only、独立/组合 VSR、缓存原图对照与 FP16：3840×2160 JPG 高光读回约 12.656 scRGB，HDR-only 的 VSR 调用为 0。此为本机验证，托管 CI 不具备 RTX 硬件。
- 四项无效标签、版本不一致、互斥构建模式和未完成分发门槛的负向检查通过；actionlint 1.7.12 校验流程，gitleaks 8.30.1 检查此前完整 22 次历史未发现匹配的凭据；源码边界检查未发现提交 SDK、程序包或原照片。后续新提交仍需要按同一流程检查。
- 本轮 logo 采用维护者最新透明 PNG，源文件 SHA256 `c163aa36e0be7a2395a8f5e935f52864e41e7678593e3c147c6802b848bae9a1`，1254×1254；从 1076238 → 767429 字节，RGBA / alpha 完全相同。派生四种 PNG 与十种 ICO，不对原图做有损量化或改画，也不改写下载文件。尺寸转换使用高质量重采样，不将缩小尺寸称为像素不变。
- 更新常用 `artifacts/Luma` 完整 RTX 便携目录；所有图标资源与源码逐字节一致，独立启动检查完成。Computer Use 实际启动应用并确认标题栏的新山脊图标正常，3 个真实相册（3043 / 98 / 36 张）仍可显示。没有把这些私人照片加入公开截图或仓库。26 次历史的 gitleaks 8.30.1 检查未发现匹配凭据，actionlint 再次通过。

验证日期：2026-10-12 至 2026-10-13。此记录描述开发版实际检查结果，不代表 Microsoft Store 认证。

## 0.1.16 无损缩略图磁盘缓存与计时更正

**更正先前图库等待定义：**旧 GalleryPerformanceVerification 的 `visibleWait` 在缩略图完成后仍继续运行，报告写入前又执行了完整刷新。因此 0.1.14/0.1.15 的“回到首屏 814–854 ms”包含刷新，不能解释为独立缩略图等待，也不是首次显示。新计时器完成后立即停止；首次显示、快滚返回、扫描及所有已实现卡片的数量分开记录。原始历史报告保留，不将计时修正算作产品加速收益。初次缓存快时可能先实现 17 张而解码慢时实现 21 张，比较必须同时看数量，不能以相同工作量声称固定倍数加速。

先用与原生解码流程相同的独立 WIC 探针分段读取三张 7008×4672 相机 JPG（相册首/中/尾）。480 边长：从文件总计约 150–175 ms；JPEG 原生缩小 CopyPixels 约 136–149 ms、初始化/色彩设置约 4–6 ms、最后像素复制约 3 ms。预先读取完整文件约 11–14 ms，内存流解码仍约 125–135 ms，总计约 150–160 ms，没有稳定净收益。GetThumbnail 仅 160×120，GetPreview 不支持，不能用它永久替代 480 缩略图。探针输出 `artifacts/decode-investigation/probes.json`，没有清文件系统缓存，抽样并非全相册。探针最初缺少 WinRT include / NOMINMAX，修正工具编译设置后测量，生产解码器未因此改动。

另用 Windows ReturnOnlyIfCached 抽样 24 张、480/168 请求，仅一张满足非“小尺寸缓存”条件，其余返回需放大的结果；查询通常约 2–11 ms。微软 [ThumbnailOptions](https://learn.microsoft.com/en-us/uwp/api/windows.storage.fileproperties.thumbnailoptions?view=winrt-26100) 与 [StorageItemThumbnail](https://learn.microsoft.com/en-us/uwp/api/windows.storage.fileproperties.storageitemthumbnail?view=winrt-26100) 说明此路径可返回缓存/内嵌图，应检查 ReturnedSmallerCachedSize；本版未把小图放大作为高质量最终缩略图。`shell-cache-143436` 保留实际结果；更早使用混合路径分隔符导致探针路径失败的报告也保留，规范化后正常。

### 实现与校验

缓存保留 WIC 已完成色彩/EXIF 的预乘 BGRA，ZLib Fastest 无损压缩，SHA256 校验像素和尺寸元数据；不存完整原图、不处理 NVIDIA 或 GPU 纹理。键包含规范路径、文件大小/修改时间和物理解码边长。浮点原生 HDR 拒绝写为 SDR；其 Windows 提供程序路径保留。读取长度与解压像素限制不超过 1024×1024×4，校验失败退回原有解码。暂存数组仍使用有预算的像素池，没有克隆完整数组排队写入；写入在已有四个后台槽内完成，因此首次有小额额外成本，不能保证每次首遍更快。

数据目录 `thumbnail-cache-v1` 的派生文件预算为 256 MiB / 4096 项，独立于内存缓存；读命中保存访问时间，写入时淘汰最老条目。临时文件完成后原子替换；跨进程独占索引锁及代次重建协调容量，忙时跳过可选写入，不阻止看图。只有缓存目录中符合本服务命名规则的 `.lth` 会参与淘汰，不删除源照片。尺寸或像素损坏、修改失效、不同边长拒绝、原生 HDR 拒绝、最近使用淘汰与小预算实际磁盘占用/临时文件检查共 12 项通过。

### 四次进程启动与完整相册

`tests/benchmark_thumbnail_cache.py` 使用独立数据库/缓存，完整只读 3043 张、约 54.8 GiB 相册；全目录成员、大小/修改时间和均匀抽样 32 张 SHA256 前后相同，没有复制或写入原图，没有清文件系统缓存。首次和重开使用相同缓存目录，关闭缓存对照使用独立库；以下首屏计时从页面 Reload 完成后开始，不包含进程启动/索引。

| 报告 `thumbnail-cache-20261013-173313` | 首屏已实现数量 / 完整等待 | 快滚后回到起点 | WIC / 磁盘命中 | UI 回调 P95 / 最大 |
| --- | --- | --- | --- | --- |
| 首次无缓存 | 21 / 1204 ms | 24 ms | 45 / 0 | 8.85 / 18.92 ms |
| 新进程重开 | 17 / 58 ms | 25 ms | 21 / 28 | 9.85 / 29.99 ms |
| 再次新进程 | 17 / 56 ms | 26 ms | 20 / 42 | 8.78 / 24.17 ms |
| 关闭磁盘缓存 | 21 / 989 ms | 25 ms | 45 / 0 | 8.52 / 18.66 ms |

没有把数量不同的重开与首遍当作严格速度倍数。较早同为 21 张的 `decode-investigation/cache-corrected-170817`：关闭缓存 1071 ms、首次写入 1100 ms、重开 87 ms；之后 17 张为 62 ms。最终前一轮 `thumbnail-cache-20261013-172143`：首次 21 张 1141 ms、关闭 21 张 1078 ms、重开 17 张 65/62 ms。这些本机重复样本证明跨进程复用减少重复 JPEG 解码，但首遍多次有约 3–22% 额外等待/负载差异，不能声称首次未缓存也已变快。最终派生缓存约 25.7 MiB，远低于 256 MiB；没有预生成整个 3043 张相册，快滚仍跳过过时请求。

混合格式 `gallery-performance-20261013-172210`：120 张 SDR/浮点 HDR JXR，WIC 120 次、磁盘复用 339 次、仅 HDR 系统提供程序一次，缓存 119 文件/34719 B。HDR 没有被降为 SDR 磁盘数据，未变化刷新新增解码 0。`viewer-interaction-20261013-172214`：40 次缓存空格对照提交中位 0.04 ms、最大 0.09 ms、无新增 AI/任务；真实 HDR、删除刷新、封面回退、缩放平移和全屏恢复通过。`viewer-lifecycle-20261013-172232` 的 49 个窗口/两次进程启动、HDR/VSR 独立与组合、忙时关闭、弹窗及偏好恢复通过。

最终 `viewer-performance-20261013-173337`：首图及三个保存 HDR 的重开均无缺少玻璃的照片呈现；20 次静态浮层呈现只重算一次模糊，缓存重开新增解码 0。SDR / HDR 处理期间 UI 回调 P95 1.41 / 1.64 ms，最大 3.13 / 4.16 ms。Computer Use 在隔离图库实际导入完整相册、打开/翻图、观察缩略图与比例、正常关闭并返回 3043 张图库；HDR 截图偏差仅用于布局观察。源码 Release 构建 0 警告、0 错误，0.1.16 自包含目录约 192.8 MiB、ZIP 81.4 MiB、未签名 MSIX 82.8 MiB；499 个文件逐字节匹配，11 种格式关联、NVIDIA 原始组件/许可及无用户照片/数据库/缓存检查通过。首次 JPEG、首次 SDK、历史约 120 ms 峰值根因和连续屏幕帧仍保留待办。本目录没有 Git 远程，未创建、发布或推送仓库。

## 0.1.15 缩略图导航按尺寸解码与请求复用

预览的缩略图为 88×62 逻辑像素，却仍按图库的 320 逻辑像素边长解码。改为导航请求 112 逻辑像素边长，乘实际 DPI，范围 64–1024 物理像素；本机 150% DPI 为 168 边长，图库仍为 480。较大的缓存直接供小控件显示，较大的在途请求也可共享；小图不满足图库的大尺寸请求。加载请求按文件路径分组，仅比较同文件的尺寸，不扫描快滚时积累的所有文件。原生 HDR/失败回退仍使用 Windows 提供程序，使用明确物理尺寸，避免再叠加系统 DPI。

与 0.1.14 相同的只读 3043 张相册、40 次跨位置随机/12 次预读/十个重叠请求；下表为监测节点采样，包括本机 UI/原生/临时缓冲，不包含完整 GPU/驱动内部资源：

| gallery-performance-20261013- | 路径 | 随机首图中位 / 最大 | 邻图首图中位 / 最大 | 最后缩略图缓存 | 进程私有提交峰值 / 稳定等待后 |
| --- | --- | --- | --- | --- | --- |
| 125353 | 0.1.14 对照 | 359 / 429 ms | 5 / 8 ms | 31.64 MiB | 608.35 / 517.86 MiB |
| 125321 | 紧凑导航原型 | 336 / 415 ms | 5 / 6 ms | 6.89 MiB | 565.08 / 481.35 MiB |
| 125753 | 0.1.15 重复 | 368.5 / 423 ms | 5 / 10 ms | 6.89 MiB | 559.20 / 483.15 MiB |
| 130352 | 最终路径索引与请求合并检查 | 359 / 427 ms | 5.5 / 14 ms | 6.89 MiB | 555.06 / 470.21 MiB |

缓存尺寸下降复现，随机开图速度差异没有稳定复现，因此不声称本轮把随机开图变成瞬间响应。解码照片缓存仍为 16 帧、约 170.75 MiB；缩略图计数 96，较大缓存复用命中四次。源目录的成员/大小/修改时间及 32 张抽样哈希前后相同，没有清文件系统缓存。最终 `130423` 的完整相册滚动：最多实现 26 张卡片、首屏 21 张缩略图完整等待 854 ms；UI 回调 P95/最大 8.25/24.90 ms，无变化刷新新增读取 0。导航的内存改善不代表图库未缓存等待已消失。

请求复用另用一张自有 2048×1024 PNG，在独立数据目录生成 8539 B 夹具。暂停大尺寸请求后，再把同路径的小控件加入真实 WinUI 树，然后恢复解码；确认实际只调用一次解码、两个控件引用同一 WriteableBitmap，尺寸仍满足图库需求。此检查在性能资源采样之后执行，不混入上表缓存统计；生产运行不启用暂停钩子。原图从未写入，成功后只清理生成的 PNG。实际原生缩略图像素在 168、448、480 边长均保持 ICC 颜色与 EXIF 方向，两种带 SDK/无 SDK DLL 均通过；原生 HDR 仍拒绝降为 8-bit。

`125823` 的 120 张混合 SDR + 浮点 HDR JXR 夹具：Windows HDR 缩略图提供程序实际读取一次，可见缩略图全部完成 41 ms，未变化刷新读取 0。`viewer-interaction-20261013-125827`：40 次缓存原图/HDR 对照提交中位 0.04 ms、最大 0.08 ms，新增 AI/后台任务为 0；真实懒加载 HDR、并发窗口、损坏封面、刷新、放大平移、浮层选中状态和全屏恢复通过。`viewer-performance-20261013-125846`：首图缺少玻璃的呈现计数为 0，三次记住 HDR 重新打开也为 0；20 次静态浮层呈现只重算一次模糊。SDR / HDR 处理期间 UI 回调 P95 为 1.50 / 1.59 ms，最大 4.75 / 3.49 ms；这些是提交/回调时间，非屏幕端到端延迟。

最终 `viewer-lifecycle-20261013-130430` 的 49 个真实窗口/两次进程启动通过，含仅 HDR、仅 VSR、组合、在处理时关闭、弹窗及重启后记住 HDR；源码 Release 构建 0 警告、0 错误。Computer Use 在隔离图库实际导入完整相册、开图、方向键翻图、滚动至未访问的导航区域并正常返回；紧凑缩略图、完整选中框及浮层可见，浏览列表后当前照片仍为 5 / 3043，偏移 0 → 2592。HDR 截图仅用于布局检查，未作为物理亮度或连续屏幕帧证明。

本版仍保留未缓存首屏、随机大图、首次 SDK 初始化、历史约 120 ms 峰值根因及跨屏/连续帧检查的待办。0.1.15 自包含目录约 192.8 MiB、便携 ZIP 81.4 MiB、未签名 MSIX 82.8 MiB，499 个文件逐字节匹配；11 种格式关联、原始 NVIDIA DLL/许可、当前验证文档和无用户照片/数据库检查通过。没有公开发布、推送或安装签名包。

## 0.1.14 缩略图分配与完整高清相册压力验证

先增加分阶段的界面调度、GC 和操作耗时记录，再调整缩略图路径。旧版直接 WIC 仅覆盖 JPG；本版覆盖已有解码器支持的 SDR 格式，避免 Shell RPC、编码数组和第二次图片解码。浮点原生 HDR 仍回退 Windows 缩略图提供程序，实际照片始终保留独立 FP16 路径。暂存像素采用专用 ArrayPool：最大单块 4 MiB、每个桶最多四块，总保留量小于 32 MiB；四个加载槽继续限制工作并发。仅复制有效像素长度，创建/复制失败也归还缓冲。32 MiB / 96 项图片缓存预算不包含这个池、原生临时像素、当前控件引用及整个进程的 UI 内存。缓存只在超预算时寻找最老条目，取消每次插入时的排序数组。

### GC 对照与未解决的调度峰值

本机已有 dotnet-trace 使用 gc-verbose / CPU 采样；分析器仅在忽略的 artifacts 中，未增加应用依赖。600 张 128×96 BMP、48 次快速滚动的 GC 追踪对照如下，追踪本身有开销：

| 报告目录（gallery-performance-20261013-） | 路径 | GC 次数 | GC 暂停总和 / 单次最大 | 采样 byte[] 分配估计 | UI 回调 P95 / 最大 |
| --- | --- | --- | --- | --- | --- |
| 110850 | 0.1.13，BMP 走 Shell | 25 | 110.72 / 7.28 ms | 91,379,144 B | 8.23 / 16.55 ms |
| 111422 | 0.1.14，SDR WIC + 专用池 | 2 | 23.39 / 12.01 ms | 1,816,624 B | 8.88 / 17.92 ms |

旧路径滚动后读取 146 次 Shell，新路径直接 WIC 读取 604 次，后者完成更多缩略图；不能把这两行当作同等工作量的严格速度比较。缓存分别为约 31.8 / 4.5 MiB，区别也包括 Windows 提供程序返回的像素大小。采样分配是 EventPipe AllocationTick 的估计，不是精确计数；新路径另记录监测阶段总托管分配约 10.2 MB。先前的 103520/104043、105541 样本同样显示分配与回收减少，但 UI 最大延迟没有稳定同步降低。

调查旧峰值的 101338（JPG）/101343（BMP）记录约 120/121 ms 的界面回调等待，真实 GC 暂停最大仅约 10.86/7.91 ms；BMP 峰值附近没有 GC。101642 的托管 CPU 采样主要落在 WinUI 消息泵的 unmanaged 时间与 WinRT 终结器，消息泵包含等待，不能据此判定实际 CPU 消耗位置。证据不支持把 120 ms 全部归因于 GC，也不能归因给外部浮层。较晚的新旧路径均未重现该峰值，因此本版只声称减少明确测到的分配/回收开销，峰值根因继续待办。CPU/GC 追踪及分阶段报告保留。

### 3043 张只读相机照片

用户新增相册包含 3043 张 JPG，共 58,875,811,079 B（约 54.8 GiB）。测试递归读取完整相册，在 artifacts 中建立独立数据库，不复制、改名、移动或删除原照片；RAW 和视频不在当前支持格式中。全目录照片成员、大小与修改时间，以及均匀抽样 32 张 SHA256 在前后相同。没有清除文件系统缓存，抽样哈希读取也会预热其中少量文件。测试只遍历已实现的卡片，不再对 3043 个索引逐个查询控件，避免测试本身加入与相册长度成比例的界面开销。

| 报告目录（gallery-performance-20261013-） | 数量 / 最大实现卡片 | 回到首屏 21 张完整缩略图 | UI 回调 P95 / 最大 | 采样进程工作集 / 私有提交峰值 |
| --- | --- | --- | --- | --- |
| 121121 | 3043 / 26 | 817 ms | 8.94 / 15.12 ms | 246.9 / 177.6 MiB |
| 123905，最终便携构建 | 3043 / 22 | 814 ms | 9.15 / 24.53 ms | 240.0 / 179.7 MiB |

每次 48 个快速跨位置滚动，最终可见缩略图的实际路径必须对应卡片；首屏完整后未变化刷新新增解码为 0，扫描期间新增请求合并检查通过。第二组缩略图缓存为 27,648,000 B，跳过 951 次过时请求。`firstMilliseconds` 945 ms 包含索引、路线加载和固定 300 ms 等待，不代表首屏图片全部完成。没有逐张解码全部 3043 张，更不是屏幕逐帧延迟。

`121515/album-viewer-performance.json` 在同一完整相册内选择跨越全相册的 20 个位置，重复两轮共 40 次随机开图；随后 12 次邻图预读，再提交十个重叠请求。随机首图中位 368.5 ms、P95 411 ms、最大 420 ms；预读命中首图中位 5 ms、最大 7 ms。新窗口构造到准备完成约 670 ms，不含新进程启动。所有选择均校验实际请求路径、错误面板、SDK 未初始化以及不存在缺少玻璃的照片呈现，重叠请求最终保持最后一张。

解码缓存最多 16 帧、实测约 170.75 MiB（预算 256 MiB）；缩略图缓存约 31.6 MiB。进程采样工作集 / 私有提交峰值约 568.2 / 596.1 MiB，不能以缓存预算冒充整个进程内存。第一轮末私有提交约 572 MiB，第二轮末约 565 MiB，等待后约 509 MiB；这两轮没有持续增长迹象，但不是长期泄漏证明，也没有测量此阶段的全部 GPU/驱动内部占用。随机开图约 0.37 秒及未缓存首屏缩略图约 0.8 秒仍是明确的优化目标。

另有 Lightroom 的 94 个不同路径（92 个唯一内容）、多种尺寸 JPG 副本对照：110433 旧路径 UI 回调 P95/最大 10.07/20.40 ms、完整首屏等待 34 ms；110602 新路径 5.32/17.99 ms、35 ms。最大源图为 14048×9376，Pillow 仅读取尺寸时发出尺寸警告，原生解码继续按已有预算缩小；没有取消图像安全检查。原图哈希前后相同，不是清缓存的磁盘对照。

### 格式、呈现及实际操作

最终 `123914` 的 120 张 JPG/PNG/BMP/GIF/TIFF/ICO + 浮点 HDR JXR 混合夹具通过：Windows 提供程序实际读取 HDR 缩略图一次，首屏全部完成 36 ms，96 项缓存 4,577,280 B，无变化刷新新增读取 0。直接缩略图像素检查覆盖预乘 BGRA、透明度、短缓冲保护、ICC、EXIF、BMP/GIF/TIFF/PNG/32-bit RGBA ICO，以及动画 GIF 第一帧；浮点 HDR 接口拒绝降为 8-bit。测试最初使用 RGB PNG 嵌入 ICO / 默认多尺寸 ICO 的夹具与预期不符，改成明确的 32-bit RGBA、单尺寸夹具后通过；没有声称新增支持所有 ICO 编码变种。

`viewer-performance-20261013-111844`：首图有玻璃的原生呈现约 13 ms，缺少玻璃的照片呈现计数为 0。缓存重开 300–484 ms、新增解码 0；SDR / HDR 处理期间 UI 回调 P95 0.86 / 2.04 ms、最大 4.85 / 16.87 ms。21 次静态浮层呈现只重算一次模糊。三个记住 HDR 的窗口首图约 333/375/439 ms、真正增强完成 1058/421/488 ms，均复用设备且未呈现缺少玻璃的照片。`viewer-interaction-20261013-111638` 的 40 次缓存空格对照提交中位 0.03 ms、最大 0.07 ms，新增 AI/工作任务为 0；损坏封面、刷新、缩放/拖拽、全屏和两个 HDR 窗口通过。均为提交/回调时间，非物理屏幕端到端延迟。

Computer Use 实际打开用户的完整相册、滚动至后面的高清照片、开图、恢复浮层和浏览底部缩略图；缩略图滚动条位置由 1884 → 3612，当前照片仍为第 26 / 3043 张。实际观察到新位置缩略图逐步完成，没有将此等待称作瞬间加载；预览和图库均正常关闭。HDR 截图映射偏差不作为亮度证据；没有连续帧/录屏接口可证明快速拖动全程无黑块，仍保留此项。

25 项图库/激活检查、正式 SDK 的独立 TrueHDR/VSR/组合、7008×4672 原图、精确 FP16 原图对照、原生 HDR、照片采样玻璃通过；单独无 SDK 构建也通过，增强状态准确不可用。最终 `viewer-lifecycle-20261013-124125` 的 49 个真实预览窗口/两次进程启动全部通过，含独立增强、弹窗、并发窗口、忙时关闭和重启后 HDR 偏好；整组约 43 秒，并非开图延迟测量。源码 Release 构建 0 警告、0 错误，自包含便携构建完成。0.1.14 程序目录约 192.8 MiB、ZIP 81.4 MiB、未签名 MSIX 82.8 MiB；499 个文件逐字节匹配、11 种格式关联、NVIDIA 原始组件/许可及无用户照片/数据库检查通过。未签名安装、公开发布和推送均未执行。

## 0.1.13 大 JPG 缩略图与刷新调度

进一步检查长相册时发现，Windows Shell 缩略图 RPC 与后续编码位图解码使可见卡片等待过长。JPEG 改为四个后台加载槽内使用已有 WIC 缩小解码、ICC 色彩转换及 EXIF 方向处理，复制为预乘 BGRA 后直接交给 WinUI WriteableBitmap。仅缩略图使用这一 SDR 接口，不创建 D3D 设备、不调用 NVIDIA；接口拒绝原生 HDR 帧及过短输出缓冲。其他格式及解码失败继续走 Windows 缩略图提供程序。物理解码边长为 320 × DPI 比例，范围 320–1024；缓存键包含边长，最多 96 项 / 32 MiB，过时消费者与修改后失效保护保留。缓存预算仅统计缓存拥有的像素，不是整个进程、临时解码或当前控件引用的总内存。

`tests/verify_gallery_performance.py --large-photo ...` 建立同一张只读 7008×4672、约 21 MiB JPG 的 96 个独立路径副本。不是 96 张不同内容，也没有清除文件系统缓存；复制完成后才启动测试。每次 48 次快速滚动，回到首屏后等待全部已实现卡片显示其正确路径的实际缩略图，不能以全占位图通过。源图 SHA256 在测试前后相同；成功样本仅清理本次生成的副本，保留报告/数据库。初始的 `firstMilliseconds` 包含固定 300 ms 布局等待，不代表首屏缩略图均已完成。

同版本 `--shell-control` 只在隔离测试中切回原有 Shell 路径。两组对照及重复样本：

| 报告目录（artifacts 下） | 路径 | 回到首屏的完整缩略图等待 | UI 回调 P95 / 最大 |
| --- | --- | --- | --- |
| gallery-performance-20261013-085352 | Shell 对照 | 2708 ms | 3.17 / 24.10 ms |
| gallery-performance-20261013-085401 | 直接 WIC | 167 ms | 6.36 / 23.22 ms |
| gallery-performance-20261013-090106 | WIC + 确定性刷新检查 | 345 ms | 10.79 / 26.61 ms |
| gallery-performance-20261013-092807 | 最终居中裁切，WIC | 35 ms | 25.00 / 121.59 ms |
| gallery-performance-20261013-092917 | 最终居中裁切，Shell 对照 | 2791 ms | 8.00 / 117.60 ms |
| gallery-performance-20261013-092925 | 最终居中裁切，WIC | 268 ms | 17.05 / 116.90 ms |
| gallery-performance-20261013-094119 | 撤回上传实验后的最终重建，WIC | 45 ms | 30.27 / 118.89 ms |

其余 WIC 重复样本 `084759` / `085106` 为 301 / 41 ms。以上测量没有并行构建和 Computer Use 捕获；`092519` 的 540 ms 样本发生在隔离 GUI 仍打开时，仅用于功能回归，不作为独立性能基线。等待明显缩短，但新路径更快完成更多图片，其 XAML 回调 P95 并没有同步下降，后段两条路径都出现约 117–122 ms 峰值；没有证据证明其原因，继续列为性能优先待办，不能归因给游戏浮层或声称逐帧顺滑。最终样本最多实现 38 张卡片，首屏 21 张，缓存 23,347,200 字节，跳过 332 次过时加载，未变化刷新新增读取为 0。实际原生 480 长边缩小解码中位约 191 ms，33 MP 完整解码约 577 ms，说明每张尚未缓存的 JPEG 仍有熵解码成本。

继续试验了丢弃已完成、但消费者已离屏的缩略图以减少 WinUI 上传：`093844` 跳过 27 次位图创建，缓存约 12 MiB，但原生读取增加到 40 次，首屏等待 609 ms，UI 回调 P95 / 最大仍为 18.14 / 119.94 ms。没有稳定的响应收益，而且削弱回到原位置的缓存命中，因此未纳入最终代码；保留开始前的过时任务取消与完成像素缓存。撤回后重建的 `094119` 完整通过，首屏等待 45 ms、缓存约 19.3 MiB，但回调 P95 30.27 ms、最大 118.89 ms；这再次说明后段调度峰值没有解决。实验报告保留，不算最终版本收益。

测试还暴露旧 `if(scanning)return` 会丢掉新增文件夹后的刷新：早期 `083901` 得到 0 张索引，并非 JPEG 解码失败。现在同一 XAML 线程内合并全量/指定相册请求，当前扫描结束后继续下一轮，不并行扫描。确定性测试在空图库扫描取得快照后暂停，添加文件夹并请求刷新，确认请求不会直接完成，再解除暂停；`090106` / `090111` / `092925` 均完成全部索引。生产路径没有测试暂停。600 张 BMP 的最终 `090111`：最多实现 22 张卡片、首屏 21 张，完整缩略图等待 54 ms，跳过 799 次旧任务，缓存 33,377,280 字节（小于 32 MiB），未变化刷新新增读取 0；UI 回调 P95 9.51 ms、最大 35.29 ms。这是调度测量，不能替代屏幕帧间隔。

真实预览回归 `viewer-performance-20261013-085523`：SDR / HDR 处理期间 UI 回调 P95 1.16 / 1.21 ms，最大 3.00 / 2.90 ms；缓存重新开图 264–301 ms、新增解码 0；三个记住 HDR 的窗口到首图 307 / 294 / 356 ms、真正增强完成 983 / 343 / 415 ms，缺少玻璃的照片呈现计数均为 0。21 次静态浮层呈现只重算一次模糊。这不是新进程冷启动或屏幕端到端延迟。

最终缩略图裁切后的 `viewer-interaction-20261013-092812`：预读连续 10 张 SDR 首图中位 6 ms、最大 9 ms；混合缓存随机 6 张中位 265.5 ms、最大 321 ms；40 次缓存空格对照提交中位 0.03 ms、最大 0.07 ms，新增工作任务及 AI 调用为 0。损坏封面回退、已删照片刷新、24 次图库尺寸变化通过（到两次 Composition 回调中位 28.2 ms、最大 70.3 ms，非屏幕逐帧时间）。一次误用只有 3 张照片的 UI 夹具导致测试报“需要至少 12 张”，已改用原只读相册完成回归；不是应用崩溃。

`viewer-lifecycle-20261013-085758` 的 49 个真实窗口/两次启动、仅 HDR/仅 VSR/组合、忙时关闭及偏好恢复通过。0.1.13 原生带 SDK 与独立无 SDK 构建的 `verify_native.py` 均通过：真实独立/组合调用、缓存切换、FP16 高光、原生 HDR JXR、照片采样毛玻璃和降级状态；24 项图库/激活检查通过。`verify_thumbnail_pixels.py` 分别用两种 DLL 检查手工期望的 BGRA/预乘 alpha、短缓冲不写入、JPEG ICC、EXIF 旋转及原生 HDR 拒绝通过。相关机器报告位于 `artifacts/thumbnail-pixels`。

Computer Use 在隔离目录 `ui-thumbnail-0.1.13` 实际导入三张照片、打开竖图、方向键翻图并检查图库和底部导航。发现 Image 在有限父容器内偏向顶部裁切，设置原生 Image 水平/垂直居中后，EXIF 竖图夹具上下两种颜色均在缩略图中正确出现；完整预览按原始比例显示。暗色主题、卡片圆角、选中边框、毛玻璃与返回图库可见。HDR 屏幕截图的亮度偏差不作为物理观感证据，原照片未改变，测试窗口已正常关闭。

一次清理失败测试副本的 PowerShell 请求被自动审批拦截，返回仅为“blocked by policy”，没有换工具重试删除。约 2 GiB 副本保留在 `artifacts/gallery-performance-20261013-083901/source`，不包含在应用包或 Git 提交中。

源码 Release 构建 0 警告、0 错误，自包含发布与 MSIX 架构/清单校验通过。0.1.13 包为约 192.7 MiB 程序目录、81.4 MiB 便携 ZIP、82.8 MiB 未签名 MSIX；499 个文件逐字节匹配，11 种格式关联、原始 NVIDIA DLL/许可与当前验证文档均检查，未包含用户照片/数据库/日志。没有签名安装或公开发布，便携版仍为本机验收入口。

## 0.1.12 设备会话复用与 SDK 显存稳定性

先记录 0.1.11 的新设备路径：预解码后新建设备约 150 ms，NGX 初始化约 550–680 ms；只看上传速度会漏掉这段开图成本。当前版本在刚关闭的窗口之间独占移交完整渲染器，最多保留一个闲置槽、10 秒到期。移交前等待所有 CPU 工作退出、解绑窗口，清空照片、SDK 输出、模糊和交换链资源，重置视角、计数与生效状态。不同活动窗口不共享 immediate context。留存/重新绑定前查询实际 DXGI 进程显存预算；使用超过预算的 80% 或余量不足 256 MiB 时放弃留存，无法查询预算时也不留存。这个条件约束闲置缓存，不是整个进程的全局显存上限。

`artifacts/device-final-fresh/device-performance.json` 与 `device-final-reuse/device-performance.json` 使用最终 DLL，分别新建 6 次、复用 24 次，去掉前两次取中位数。两张 1280×853 自建颜色夹具交替；大图为只读 7008×4672 JPG。计时包括设备创建/绑定、所需 SDK 初始化、上传、真实增强和三个 FP16 像素读回，排除文件解码、WinUI 窗口及进程启动；不是屏幕延迟。不同夹具必须有不同指纹，每次新照片增加正确的独立/组合 SDK 调用，原图对照不增加调用。基线原始测量另保留在 `artifacts/device-baseline-0.1.11`。

| 路径 | 最终版新设备：完成并读回 | 最终版短期复用：完成并读回 |
| --- | ---: | ---: |
| 小图 SDR | 155.15 ms | 1.80 ms |
| 小图 TrueHDR-only | 755.57 ms | 5.32 ms |
| 小图 VSR-only | 782.36 ms | 9.82 ms |
| 小图 HDR + VSR | 901.90 ms | 14.66 ms |
| 大图 TrueHDR-only | 792.25 ms | 50.78 ms |

### 修复真正的 SDK 资源增长

最初保留功能实例的试验虽然更快，实际显存却随新图片缓冲增长：24 次小图重开，HDR 约 179→481 MiB、VSR 约 305→808 MiB、组合约 489→1797 MiB，大图 HDR 一度约 5.7 GiB。仅清空 NGX 参数中的资源指针不能解决，因此没有采用这个方案。最终保留设备/NGX 初始化会话，窗口关闭时释放功能实例；同一窗口尺寸、格式或模式导致 SDK 图片缓冲替换时，也释放旧实例。同尺寸兼容纹理、当前照片缓存对照与预设调参继续使用既有资源，照片位深、缩放策略和 HDR 参数未降低。

`tests/verify_device_residency.py` 使用 640×426、960×640、640×960 三种尺寸循环 36 张，分别真正执行 HDR、VSR 和组合，共 108 个输入。实际 DXGI 进程本地显存：旧功能实例保留行为的 HDR 151.5→396.2 MiB、VSR 125.4→593.3 MiB、组合 231.0→1327.6 MiB；最终分别稳定在约 199.9 / 273.8 / 499.2 MiB，最后 12 个样本差值均为 0。报告 `artifacts/device-residency-before` 和 `device-residency-final`。不同尺寸和颜色的 FP16 指纹逐次一致，没有用丢弃增强来获得稳定占用。24 次跨窗口复用也不再线性增长；大图的 SDK 分配器仍在约 989–1159 MiB 间波动，这不是应用自有纹理计数。

延长真实窗口测试又发现，在另一个 SDK 设备结束时，当前设备新建功能会失败为 `0xBAD00007 / FAIL_NotInitialized`。原生双设备最小测试证明本机 SDK 的 `Shutdown1(device)` 会影响其他已注册设备的功能创建。最终统一会话生命周期：有活动 SDK 使用者时，完成资源/上下文释放后的已注册设备可交给下一位使用者；最后一个使用者退出才调用全局关闭。生命周期租约最后析构，包含构造失败的回收；不在已注册设备尚在使用时执行全局关闭。原生测试中一个窗口存活、16 个设备依次建立/销毁，存活窗口每次以新尺寸重新执行增强，随后全部关闭和再次初始化均通过，实际显存后段稳定在约 893 MiB。失败的窗口 17 报告 `viewer-lifecycle-20261013-073318` / `073727` 保留，最终 `viewer-lifecycle-20261013-074848` 的 49 个真实窗口 / 两次启动完整通过。

“一个闲置槽”仅指完整渲染器；在其他 SDK 使用者仍活动时，会话协调器还可保留已注册的裸设备，下一次构造优先复用，最终全局关闭清理。不能声称整个进程只保留一个 SDK 设备，或将应用清空图片引用解释为全部 SDK/驱动显存已经归零。更广泛的多窗口、长时间使用和全局显存控制仍为待办。

### 真实窗口、交互及发布检查

释放 Computer Use 截图会话且无同时构建后，`artifacts/device-cache-20261013-081338/device-cache.json`：未缓存大图的新预览至首图 697 ms、至实际 HDR 完成 2253 ms，不含整个应用启动；短期复用三次首图 364/318/299 ms、完成 419/374/340 ms。每次 HDR 调用计数从 0 到 1，VSR 为 0，旧窗口结果/计数没有泄露。两个窗口可独立增强，闲置槽实际到期并清空；关闭后应用闲置照片/画布纹理为 0。两个窗口关闭后 DXGI 进程本地显存仍约 1378 MiB，到期后约 720 MiB，确认有所下降但并未归零。SDK/驱动/WinUI 缓存不能算作应用自有缓冲。较早 `device-cache-20261013-075250` 前段与一次构建重叠，不用它作为无其他负载的速度基线。

`artifacts/viewer-performance-20261013-075309`：缓存像素重开 286–316 ms、无新增解码；SDR / HDR 的 dispatcher 延迟 P95 1.31 / 1.84 ms、最大 3.50 / 4.28 ms；静态浮层 21 次呈现重算模糊一次，照片缺少玻璃的呈现计数为 0。保存 HDR 后三个窗口的完成时间为 1151/370/315 ms，第一张仍有未缓存的完整解码成本。`viewer-interaction-20261013-081223`：预读 SDR 首图中位 6 ms、最大 7 ms，混合随机跳转中位 250 ms、最大 298 ms；40 次缓存原图/HDR 对照提交中位 0.03 ms、最大 0.09 ms，新增任务/AI 调用 0；完整细节、平移/适配、过时结果保护、图库刷新/封面及 24 次尺寸变化均通过。这些是内部提交/回调时间，不是输入到屏幕的物理延迟。

Computer Use 在隔离图库 `artifacts/ui-device-session-0.1.12` 中实际打开大图、方向键换图、显示浮层和增强弹窗、独立开启 VSR，正确显示 HDR + VSR 已生效。关闭预览后图库仍正常、原照片未修改。弹窗中空格按下/松开日志的工作任务及 SDK 计数不变。观察到原生内容淡入、带完整选中框的缩略图和照片采样模糊；点时截图不能证明高速动画每帧都流畅，HDR 截图过曝不作为亮度结论。该截图会话在定量测量前释放，没有操作游戏加加或其他应用配置。上述原生测量进程均未加载 GPP64.dll。

最终原生回归包含真实 TrueHDR-only、独立/组合 VSR、40 次逐像素缓存对照、透明 PNG、原生 HDR JPEG XR、ICC/EXIF、线性缩小和实际高斯玻璃；尺寸/格式转换及预设/开关检查再次通过。无 NVIDIA 编译路径的普通 SDR、原生 HDR、玻璃及准确不可用状态通过。源码 Release 构建 0 警告、0 错误。首次新进程或缓存过期后的 SDK 初始化成本、长相册大 JPG 与网络源、真实换屏/高 DPI/休眠及连续屏幕帧仍需扩大验证。

自包含程序目录约 192.7 MiB，便携 ZIP 约 81.4 MiB，未签名 MSIX 约 82.8 MiB；499 个应用文件与 ZIP/MSIX 逐字节比对，11 种格式关联和 NVIDIA 原始组件/许可保持完整，不含照片、数据库和测试日志。测试数据继续只在本机忽略目录中保存；源码迭代保存为本地 Git 提交，没有远程推送或公开发行。

## 0.1.11 大图上传与纹理复用

先在 0.1.10 原生核心上增加独立分配/透明度扫描计数，保留成 `artifacts/texture-baseline/Luma.Native.dll`，然后实施优化。同一预览只保留每种用途的一个兼容纹理，尺寸/格式变化时替换；新照片清空增强结果与场景缓存，重新拷贝 SDK 输入并真正执行所请求的 HDR/VSR。SDK 写入前清除上下文绑定，避免将上一帧的读视图同时作为输出。透明度在 WIC 最终像素解码后计算一次，随不可变解码帧缓存，后续上传直接读取属性。初次解码仍有这次扫描成本，没有宣称冷解码也获得同等收益。

两轮按“基线→优化”交替运行 `tests/benchmark_textures.py`，每路径预热后 16 次上传，排除磁盘解码、窗口创建和首次模型创建。大图是只读的 7008×4672 JPG 完整像素；VSR/小图路径使用两张 1280×853 独立颜色夹具交替，检查原图与增强 FP16 指纹，逐次确认所需 SDK 调用增加、关闭的功能调用不增加。完成时间包含三个测试像素的 GPU 读回，不是物理屏幕响应。报告为 `artifacts/texture-baseline-readback`、`texture-reuse-readback` 及各自的 `-2` 目录，以下是两个样本各自中位数的范围。

| 路径 | 上传 CPU：基线 → 优化 | 原生流水线及读回：基线 → 优化 |
| --- | --- | --- |
| 大图 SDR | 31.16–33.38 → 10.41–10.78 ms | 52.32–54.68 → 21.10–21.31 ms |
| 大图 HDR | 27.22–27.31 → 10.53–10.69 ms | 48.42–54.63 → 26.24–26.42 ms |
| 小图 VSR | 0.78–0.91 → 0.36 ms | 5.61–5.91 → 4.21–4.22 ms |
| 小图 HDR | 0.76–0.88 → 0.33–0.35 ms | 2.88–2.98 → 1.75–1.76 ms |
| 小图 HDR + VSR | 0.84–0.89 → 0.35–0.40 ms | 6.93 → 4.92–4.96 ms |

80 次稳态上传原图/SDK 输入/输出的新增纹理分配总数从 224 降为 0，重复透明度扫描从 80 降为 0；大图原先每次扫描约 15 ms。初始纹理分配仍存在，尺寸/格式变化也仍需分配。大图 SDR / HDR 的应用自有 GPU 图片/测试画布分别为 176,021,012 / 568,917,524 字节，五条路径与基线完全相同；这次减少的是重复分配与扫描，不是稳定显存占用，没有缩小原图、降低位深或改变 HDR 参数。更新采用原来的 D3D11 默认资源路径；[微软 UpdateSubresource 文档](https://learn.microsoft.com/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-updatesubresource)说明资源竞争可能引入额外复制，因此收益以实际交替对照为准。

初次测试使用 GPU 事件查询，基线/优化均出现约 3.96–4.00 秒的异常等待（`artifacts/texture-baseline-run`、`texture-reuse-run`）；当时检测到 LockApp 进程。实际像素读回没有相同等待，8 项尺寸/格式转换读回为 0.22–1.74 ms，因此正式比较改用真实 FP16 读回确认完成，保留原异常样本，不将事件查询等待当成正常看图速度或优化收益。原生测试进程没有加载 GPP64.dll；本轮没有再次尝试游戏加加设置，没有改动全局配置。没有通过 Computer Use 做锁屏中的 UI 操作。

`artifacts/texture-transitions/texture-transitions.json` 验证同尺寸复用、竖图变化、透明 PNG、原生 HDR JPEG XR、恢复 SDR/HDR 能力、预设重新评估和缓存开关。新 SDR、透明与原生 HDR 图片仅保留自己的原图/画布，旧 AI 图片缓冲引用释放；当前照片按住原图或关闭再开启 HDR 不重复 AI。此处统计不含驱动/SDK 内部资源，物理显存回收仍由驱动管理。

最终便携 DLL 的真实 TrueHDR-only、VSR-only、组合、40 次精确缓存对照、ICC/EXIF、线性缩小、透明度及原生 HDR 读回通过；TrueHDR 3840×2160 高光仍为 FP16 12.656，VSR 调用 0。无 NVIDIA 编译路径通过独立检查，普通 SDR 与原生 HDR 继续可用。

实际 WinUI 回归 `artifacts/viewer-lifecycle-20261013-052919`：49 个窗口 / 两次启动通过，含独立/组合增强、并发、忙时关闭和保存设置后的重启。`artifacts/viewer-performance-20261013-053058`：首照片缺少玻璃的呈现次数 0，静态浮层 21 次呈现重算模糊 1 次，四次缓存重新打开 277–301 ms、新解码 0；SDR / HDR dispatcher 延迟 P95 1.40 / 1.03 ms。`artifacts/viewer-interaction-20261013-053608`：大图预读首图中位 5 ms、最大 6 ms，随机跳转中位 230 ms、最大 293 ms；40 次缓存对照中位 0.03 ms、最大 0.07 ms，新增任务/AI 调用 0，完整细节、缩放/平移、最新请求、图库刷新与尺寸回归通过。这些是内部呈现/回调检查，不能替代连续屏幕帧或实机 HDR 观感。

源码 Release 构建 0 警告、0 错误，自包含发布、MSIX 结构校验与 ZIP/MSIX 的 499 个文件逐字节比对通过，11 种关联及原始 NVIDIA 组件/许可完整，无用户照片、数据库或日志。0.1.11 目录约 192.7 MiB、便携 ZIP 81.4 MiB、未签名 MSIX 82.8 MiB。首次 SDK 初始化期间的绘制调度、跨窗口设备/模型复用、全局显存预算与连续屏幕帧验证继续待办。本工作目录未配置 Git 远程，本轮保存本地提交，没有推送或公开发行。

## 0.1.10 激活 I/O 与分路径看图测量

命令行照片路径选择不再通过 File.Exists 查询磁盘，明确跳过导入目录和测试目录的参数。预览窗口构造使用轻量占位数据；后台解码的租约携带文件大小和修改时间，随后用于照片信息及历史记录，避免为这些属性再次同步读盘。命令行导入的目录存在性检查也移到后台。不可用照片保留被请求的路径，显示读取错误；同文件夹有其他图片时不静默改为另一张。该改动消除代码中的同步查询，尚未以真实慢网络盘作定量对比。

`artifacts/photo-performance-20261013-033847/photo-performance.json` 的 75 项测量/检查通过。真实 7008×4672 JPG 在新预览窗口约 554 ms 提交适配首图，另行请求完整细节约 849 ms，完整帧为 7008×4672 / 130,965,504 字节。直接激活的历史大小与修改时间与原文件完全一致，不可用文件错误检查通过。

四种增强导航使用从同一原图生成的 1280×853 JPEG，分别放在独立路径下，避免把上一模式的解码命中当作冷读取。每个新窗口有初次初始化；随机跳转同时包含预读命中与未命中，随后连续导航为预读/缓存场景。检查每张新照片确实增加所需 SDK 调用计数，关闭的另一种增强调用不增加；十个重叠请求最终显示最新照片且实际增强仍正确。

| 模式 | 新窗口首图 / 完成处理 | 随机首图中位 / 最大 | 随机完成中位 / 最大 | 预读/缓存首图中位 / 最大 | 预读/缓存完成中位 / 最大 |
| --- | --- | --- | --- | --- | --- |
| SDR | 366 / 375 ms | 14.5 / 23 ms | 21.5 / 29 ms | 5 / 11 ms | 17.5 / 34 ms |
| 仅 HDR | 318 / 1334 ms | 9.5 / 16 ms | 14.8 / 20.1 ms | 5 / 7 ms | 12.5 / 29.1 ms |
| 仅 VSR | 324 / 1095 ms | 11 / 15 ms | 17.5 / 20.6 ms | 4 / 5 ms | 18.5 / 30.9 ms |
| HDR + VSR | 321 / 987 ms | 10 / 11 ms | 16.2 / 21.4 ms | 4 / 6 ms | 17 / 29.4 ms |

这里的“完成”指当前请求处理完成并提交呈现，包括实际 SDK 调用，不是物理屏幕扫描完成；不同模式的首次耗时还受到驱动/模型全局缓存与测试顺序影响，不能把组合模式偶尔更快解释为计算量更小。低分辨率副本的结果不能代表大尺寸照片的 AI 速度或增强质量。所有原始照片只读。

大图交互报告 `artifacts/viewer-interaction-20261013-034245`：预读首帧中位 5 ms、最大 7 ms；混合随机跳转中位 231 ms、最大 287 ms。40 次缓存原图/HDR 输入中位 0.03 ms、最大 0.08 ms，逐次等待后台呈现，无额外上传/增强任务或 HDR 调用。图库刷新、损坏封面回退、缩放/平移/完整细节、全屏恢复和最新请求检查通过；24 次图库尺寸到两次合成回调中位 29.7 ms、最大 79.1 ms。

性能报告 `artifacts/viewer-performance-20261013-034054`：四次重复打开 275/292/300/310 ms，新增解码 0；三个记住 HDR 的窗口首图 328/422/263 ms，照片呈现缺少玻璃的次数 0。SDR / HDR 操作的 dispatcher 延迟 P95 1.10 / 1.16 ms，最大 2.71 / 3.70 ms；静态浮层 21 次呈现仅重算一次模糊。XAML 回调延迟不是屏幕帧延迟；首次 SDK 初始化与绘制仍按同一 D3D 上下文串行，设备/模型复用和这期间的照片视角响应继续待办。

最终生命周期报告 `artifacts/viewer-lifecycle-20261013-034801`：49 个真实预览 / 两次新进程通过，覆盖真实 HDR/VSR、缓存对照、并发/忙时关闭、重叠请求、弹窗玻璃与保存设置后的重启。24 项图库/激活检查通过；没有修改原始文件或正式数据库。

启动报告 `artifacts/startup-0.1.10-final/startup-summary.json`：每路径两个新进程，未清空系统文件缓存，测量时无自动 UI 截图会话。空图库 826–829 ms；7008×4672 JPG 的 SDR 路径 1073–1075 ms；HDR 路径首图在进程启动 979–991 ms 提交，增强完成 2597–2636 ms。8 秒采样包含浮层淡出，后段稳定 4 秒 CPU 为单核约 0.78%（图库）、1.55%（SDR）、1.16–2.71%（HDR）；SDR/HDR 的最后 4 秒呈现计数不变，没有持续重算静态照片。工作集约 167 / 263–266 / 497–498 MiB，私有提交约 112–113 / 245–247 / 1630–1632 MiB；Windows GPU 专用 / 共享约 46–55 / 4.3、167 / 17、1349 / 151 MiB。HDR 的 SDK/驱动资源仍明显高于普通 SDR，这是继续优化的资源项，不属于 256 MiB 解码缓存预算。

### 发布预编译与外部空闲 CPU 排查

单独生成相同 0.1.10 源码的 ReadyToRun 实验输出，未替换正式便携版。目录约 238.6 MiB（实验未收集许可/说明），比正式目录仍多约 46 MiB。`artifacts/startup-r2r-experiment/startup-summary.json` 每路径三个新进程：图库 724–779 ms、SDR 930–952 ms、HDR 完成 2556–2641 ms；相比前面的普通发布样本，SDR 初步约节省 0.1 秒，首次 HDR 的收益较小。此实验不是严格的 A/B 冷启动测试。考虑体积增加和有限收益，本版继续关闭 ReadyToRun；依据 [微软 ReadyToRun 文档](https://learn.microsoft.com/dotnet/core/deploying/ready-to-run)，没有启用未经验证的裁剪。

实验后出现新的空闲 CPU 异常：实验 HDR 约 102% 单核，随后普通发布版的图库、SDR、HDR 都约 102% 单核（`artifacts/startup-0.1.10-after-r2r-control`）。空图库没有 AI 工作，也出现同样异常，因此没有将其归因于预编译或 HDR。非侵入式 CDB 检查 `artifacts/idle-native-profile-2/native-stack.log`：进程启动约 3 秒，105 / `afec` 线程已消耗 1.765 秒 CPU；该线程调用栈的 8 个帧全部位于注入的 `GPP64.dll`，主 UI 线程累计仅 0.421 秒。模块来自 `C:\Users\dddd\AppData\Local\GamePPSDK\1.2.1618.725\GPP64.dll`，与用户截图中的游戏加加 G+ 浮层相符。这是已观测到的外部忙等线程，不能证明此前所有卡顿都来自它。

最终输出的诊断复验 `artifacts/startup-0.1.10-observed-modules` 三条路径均正常完成，但空闲仍约 101% 单核；图库和 HDR 的模块快照记录到 GPP64.dll，SDR 的较早快照没有记录到它。单次模块快照未捕获不能证明随后没有注入。此复验的首条路径与包生成重叠，不作为无其他负载的启动基线。

尝试打开已安装的游戏加加轻量版后，没有得到可操作的设置窗口，未完成仅对 Luma 停用浮层的对照。该事项继续待办，没有全局停用软件或改动其配置。启动测量脚本现额外记录实际加载的 GPP 模块，便于后续保持测试环境一致。前述低空闲 CPU 样本和异常样本均保留；默认应用、实际屏幕连续帧与首次模型/设备复用仍需后续验证。

最终源码构建 0 警告、0 错误，自包含发布和 MSIX 结构校验通过；ZIP / MSIX 的 499 个文件逐字节比对通过，包含 11 种实际支持格式的关联和原始 NVIDIA DLL / 许可，无用户照片、数据库或日志。0.1.10 程序目录约 192.7 MiB、便携 ZIP 81.4 MiB、未签名 MSIX 82.8 MiB。构建与包校验没有将测试夹具或诊断工具打入发行文件。

## 0.1.9 依赖体积与原生选择器

Windows App SDK 改为按模块引用，保留原 2.5.1 元包解析出的 WinUI 2.3.9、Foundation 2.3.12、InteractiveExperiences 2.1.9、DWrite 2.1.0 和 Base 2.0.4，移除未使用的 AI、Windows ML、Search、Widgets 与 Runtime 元包。发布产物中不再有 ONNX / DirectML / Windows AI 运行库。与原 0.1.7 完整便携 ZIP 逐文件检查，所有继续保留的第三方 DLL 字节完全相同。保持 .NET 与 Windows App SDK 自包含，没有开启未经验证的裁剪或手动删除依赖。发布脚本在新目录中生成完整输出和许可后替换，避免增量发布残留已移除模块。

文件夹与文件选择器改用 Windows App SDK 的桌面 API，直接传入 AppWindow.Id，结果按文件路径使用。依据 [微软桌面选择器文档](https://learn.microsoft.com/windows/apps/develop/files/using-file-folder-pickers)。Computer Use 在隔离图库 `artifacts/ui-0.1.9` 和两张用户照片副本中实际执行：导入文件夹后得到一个相册、正确封面与两张照片；图库文件选择器打开 JPG；预览 Ctrl+O 取消后原窗口仍可使用；再次选取另一张竖图后正确替换预览，并完整适配。原图文件与正式图库没有修改。截图是状态检查，不能代替连续帧录屏或物理 HDR 观感判断。

发布版回归 `artifacts/viewer-performance-20261013-023012`：四次缓存照片重新打开 230/236/243/259 ms，新增解码为 0；三个记住 HDR 的窗口首图 259/254/260 ms，初始照片呈现缺少玻璃的次数为 0。SDR / HDR 压力时 dispatcher 延迟 P95 0.69 / 0.99 ms，最大 1.72 / 2.77 ms。600 张 BMP 长相册报告 `artifacts/gallery-performance-20261013-023121`：最多实现 26 张卡片，183 次 Shell 读取、817 次过时加载跳过，未变化刷新新增读取 0。

实际精简目录中的原生 DLL 通过 FP16 读回：真实 TrueHDR-only 高光 12.656（线性 scRGB，VSR 调用 0）、独立 VSR 与组合模式、40 次缓存纹理对照、原生 HDR JPEG XR、ICC/EXIF、透明度及照片高斯玻璃均通过。计算结果验证不代表照片各区域都更亮。

资源重测发现自动 UI 截图会话会干扰 CPU：会话存续时新旧两个版本均约 19%–23% 单核占用；释放截图会话后，`artifacts/startup-0.1.9-after-capture-reset` 的图库 / SDR / HDR 为 2.52 / 2.91 / 3.49% 单核占用。新进程完成时间分别为 830 / 1074 / 2676 ms；工作集 167 / 263 / 490 MiB，私有提交 114 / 246 / 1621 MiB。未清空系统文件缓存，单次采样不能解释为严格的版本性能提升。测量时应先释放持续截图的验证会话，并保持负载一致；过程与对照报告保留，不能选择性隐藏高开销样本。

最终启动报告 `artifacts/startup-0.1.9-final/startup-summary.json` 每条路径两个新进程：图库 827–847 ms、SDR 1032–1055 ms、HDR 完成 2598–2679 ms；8 秒空闲采样 CPU 单核为 2.13%、3.68–3.88%、2.71%，包含浮层的淡出。图库 / SDR / HDR 工作集约 167–168 / 263–264 / 495–496 MiB，私有提交约 113–115 / 246–248 / 1628–1631 MiB。没有将包体精简解释成运行内存或首次 AI 的大幅下降。

最终生命周期报告 `artifacts/viewer-lifecycle-20261013-031737`：49 个真实预览窗口 / 两次启动通过，包含独立与组合 HDR/VSR、弹窗玻璃、并发预览、重叠翻图、忙时关闭和记住 HDR 的重启。源码构建 0 警告、0 错误；自包含发布、MSIX 结构校验与 ZIP / MSIX 的 499 个文件逐字节比对通过。11 种实际格式的关联、原始 NVIDIA DLL / 许可保持完整，包不含用户照片、数据库或日志。程序目录约 192.6 MiB，便携 ZIP 81.4 MiB，未签名 MSIX 82.8 MiB；原 0.1.7 约 250.3 / 104.2 / 106.2 MiB。首次运行无需再下载共享 .NET / Windows App SDK；兼容系统与 NVIDIA 驱动仍是硬件增强条件。商店共享运行库版本尚未构建。

## 0.1.8 重复打开、缩略图调度与资源测量

解码缓存改为进程共享，预算 256 MiB / 16 条目（仅缓存拥有的像素，不包含当前窗口持有的引用、GPU 或 SDK）。窗口持有 SafeHandle 引用，关闭窗口不会清空其他窗口的缓存；缓存淘汰和失效不会释放仍在使用的帧。文件时间与长度读取移到后台，同一版本/尺寸的前台与预读合并，主动打开可抢占尚未开始的预读排队。取消单个请求不取消其他使用者；原生 WIC 已开始的解码不能中途取消，界面继续使用照片请求代次防止过时结果覆盖。

报告 `artifacts/viewer-performance-20261013-021510/viewer-performance.json`：四次重复打开窗口至首图为 269/238/264/255 ms，新增解码为 0；三个记住 HDR 的窗口首图为 263/260/262 ms。这是同一进程内已缓存的照片，不含整个应用启动或首次 AI 计算。前版 0.1.7 相同的新预览测试约 488–517 ms。前台/预读确实只解码一次，取消一个等待者保留其他等待者；修改文件时间会重新读取，超预算帧淘汰后仍能通过持有的引用在真实窗口绘制。SDR 压力操作的 dispatcher 延迟中位/P95/最大为 0.32/0.74/1.08 ms，HDR 处理期间为 0.30/0.66/1.45 ms；不是输入到屏幕的端到端延迟。

图库只在扫描结果变化时使对应文件夹的缩略图失效，相册内容未变时保留现有控件与封面。已离屏/回收的卡片不再继续执行排队中的 Shell RPC；已开始的 Shell 工作保留 4 个并发上限。显示器完整查询改为每两秒后台检查，窗口移动结束与重新激活另外触发查询，避免每 500 ms 重复扫描显示拓扑。

报告 `artifacts/gallery-performance-20261013-022017/gallery-performance.json`：600 张独立小 BMP 测试文件，建立索引并显示约 391 ms（含 300 ms 等待布局）；48 次跳转滚动最多实现 26 张卡片，执行 190 次 Shell 读取、跳过 813 次过时请求；未变化的刷新读取为 0。dispatcher 延迟中位 0.80 ms、P95 6.92 ms、最大 9.10 ms；检查已回收卡片未显示另一张照片。该夹具验证长列表的虚拟化和调度，不能作为大 JPG 解码速度或屏幕帧率。

启动采样 `artifacts/startup-0.1.8/startup-summary.json`，每种路径两个独立新进程，未清空系统文件缓存：

| 路径 | 外部计时至完成 | 空闲工作集 | 私有提交 | Windows GPU 专用 / 共享 |
| --- | --- | --- | --- | --- |
| 空图库 | 724–745 ms | 160–162 MiB | 109–111 MiB | 54.5 / 4.3 MiB |
| 7008×4672 JPG，SDR | 1033–1073 ms | 258–260 MiB | 240–241 MiB | 167 / 17.1 MiB |
| 同照片，TrueHDR-only | 2555–2574 ms | 492–496 MiB | 1628–1630 MiB | 1348.6 / 150–151 MiB |

HDR 路径首张 SDR 照片在进程创建约 943–963 ms 呈现，随后增强完成；没有等待增强才显示照片。内存为进程计数，GPU 数据来自 Windows GPUProcessMemory，每适配器求和；包括 WinUI、SDK 和驱动分配，与界面中的自有纹理估算不同。私有提交不是物理 RAM，不能把这些数字相加当作内存占用。开始的 8 秒“空闲”样本包含工具栏淡出；后段稳定 4 秒 CPU 为单逻辑核心约 0.77–1.54%，不是整机占用。SDR/HDR 最后 4 秒的 Present 计数保持不变，没有持续重算静态照片。

交互回归 `artifacts/viewer-interaction-20261013-022103/viewer-interaction.json` 通过：预读首帧中位 6 ms、最大 7 ms，混合缓存随机跳转 231/298 ms；40 次原图对照输入 0.03/0.12 ms，逐次等待实际呈现，无新增 AI/上传任务；24 次图库尺寸更新到两次合成回调 30.0/55.6 ms。21 项 SQLite/索引检查通过。跨屏、高 DPI 快速拖动的连续屏幕帧检查仍待办。

生命周期报告 `artifacts/viewer-lifecycle-20261013-022258` 完整通过：49 个窗口、两次新进程，包含共享缓存后的并发预览、重叠请求、真实 HDR/VSR 独立与组合、弹窗照片玻璃、忙时关闭，以及保存 HDR 偏好后的进程重启。源码 Release 构建 0 警告、0 错误。

## 0.1.7 首帧毛玻璃与界面响应

本轮只调整性能、资源调度和验证，没有新增产品功能。此前 ShowAsync 先呈现照片，UpdateGlass 才随后安装玻璃矩形，低优先级更新可能被正在执行的 SDK/上传任务延后；毛玻璃背景同时已经改成透明，造成明显的“先透明后模糊”。现在提前安装有效几何，照片上传与玻璃合成为同一次 Present；准备时保留中性可读底色，实际玻璃帧已提交后才清除底色。

D3D 绘制、ResizeBuffers 与 DXGI Present 在后台执行，XAML 线程只记录最新视角/尺寸/玻璃请求。同一 immediate context 的命令顺序保证上传、mip、SDK 与绘制依赖，不再在每次操作后等待 GPU fence；SwapChainPanel 绑定/解除绑定仍在其 XAML 所属线程，访问与销毁继续串行保护。空格对照只选择缓存纹理，并在后台呈现，不触发解码、SDK 或新的增强任务。架构依据为微软的 [SwapChainPanel 文档](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel?view=windows-app-sdk-1.8)及[界面线程响应指南](https://learn.microsoft.com/en-us/windows/apps/develop/performance/keep-ui-thread-responsive)。

Direct2D 原生 GaussianBlur 保留 FP16 和预乘 alpha。先把输入降到约四分之一边长，再按比例设置模糊半径；静态场景复用其照片/模糊纹理，只在照片、视角、尺寸、显示白色或增强结果改变时重算，面板绘制限制在实际矩形内。着色器编译结果在进程内复用。图片解码、设备准备和首个 XAML 帧并行，避免串行等待。首次 HDR 仍需真正计算；未使用亮度滤镜代替 NVIDIA。

真实 WinUI 性能报告 `artifacts/viewer-performance-20261013-011435/viewer-performance.json` 完整通过：首张和三次记住 HDR 后重新打开的第一张照片，UnfrostedPhotoPresents 均为 0，均有实际玻璃合成；三次新窗口创建至首图为 794、721、731 ms，不含整个应用冷启动。SDR 压力操作的界面回调延迟中位 0.44 ms、P95 6.77 ms、最大 29.15 ms；首次 HDR 处理同时操作时中位 0.39 ms、P95 2.00 ms、最大 28.32 ms。静态照片浮层隐藏/恢复共 13 次呈现，只重算 1 次模糊。该测量使用后台每 8 ms 向真实 XAML dispatcher 投递回调，并同时执行缩放、平移、显隐和 SDK；它衡量界面线程是否被阻塞，不是鼠标到物理屏幕的延迟。首次预览解码提前到 DisplayAsync 计时之外，不能把 FirstFrameMilliseconds 的 5–18 ms 当作完整新窗口加载时间。

原生 CPU 提交基线 `artifacts/performance-0.1.6/render.json` 与优化后 `artifacts/performance-0.1.7/render.json`：4K 画布的面板更新中位约 0.77 → 0.07 ms，视角更新约 0.77 → 0.18 ms。此测试为无窗口的 D3D 画布，批次 GPU event 等待约 3.96 秒，无法解释成实际窗口帧率或屏幕响应提升；报告同时保留 CPU 提交与批次完成时间，最终体验以真实窗口测试为准。

真实回归 `artifacts/viewer-interaction-20261013-011608/viewer-interaction.json`：预读翻图首帧中位 7 ms、最大 10 ms；混合缓存随机跳转中位 216.5 ms、最大 274 ms。40 次原图/HDR 输入调用中位 0.05 ms、最大 0.16 ms，每次随后等待真实后台呈现，新增增强任务与 AI 调用均为 0；此调用耗时不包括后台 Present 与显示刷新。图库 24 次尺寸更新到两次合成回调中位 61.2 ms、最大 293.1 ms，本次同时运行生命周期压力测试，不能作独占基准对比；快速拖动逐帧验证仍待办。

最终 0.1.7 独立运行的交互报告 `artifacts/viewer-interaction-20261013-013247/viewer-interaction.json` 全部通过：预读首帧中位 5 ms、最大 6 ms；混合随机跳转中位 233.5 ms、最大 283 ms；40 次缓存对照输入调用中位 0.03 ms、最大 0.08 ms，随后等待每次实际呈现；24 次图库尺寸更新到两次合成回调中位 27.8 ms、最大 66.7 ms。源码 Release 构建为 0 警告、0 错误，自包含发布成功。另行编译并运行无 NVIDIA 的原生版本，SDK 状态为 0、调用计数为 0，SDR、原生 HDR、照片毛玻璃、透明度、ICC/EXIF 和按需初始化不可用状态检查通过。

最终 0.1.7 独立性能报告 `artifacts/viewer-performance-20261013-013609/viewer-performance.json`：三个记住 HDR 的新窗口到首图为 488、448、502 ms，均无缺少玻璃的初始照片帧；SDR 的界面回调延迟中位 0.08 ms、P95 1.76 ms、最大 3.69 ms，HDR 处理期间为 0.08/1.38/2.85 ms。静态浮层 21 次呈现只构建一次模糊。保留前一轮数值以呈现负载差异，不把单次较好结果作为跨设备保证；此处记录的是回调与原生提交检查，仍不能替代高速操作的屏幕连续帧测量。

收尾检查补充了 DPI 变化时的模糊缓存失效，以及取消照片上传时仍安装已消费的窗口尺寸/玻璃几何，防止后续照片遗漏表面更新。最终报告 `artifacts/viewer-interaction-20261013-014424/viewer-interaction.json`：预读首帧 5 ms 中位/7 ms 最大，混合随机跳转 230.5/314 ms，缓存对照输入 0.04/0.11 ms，图库尺寸到两次合成回调 31.5/56.5 ms；全部交互检查通过。性能报告 `artifacts/viewer-performance-20261013-014441/viewer-performance.json`：三次记住 HDR 的新窗口首图为 515/488/517 ms，首帧玻璃完整；SDR 回调中位/P95/最大为 0.36/1.03/2.37 ms，HDR 处理时为 0.33/1.01/1.66 ms；13 次浮层呈现只重算一次模糊。收尾后的原生 FP16、HDR/VSR、缓存对照与玻璃像素读回再次通过。

生命周期报告 `artifacts/viewer-lifecycle-20261013-011558`：49 个真实预览窗口、两次启动通过，包括独立 HDR/VSR、组合模式、缓存对照、扩展弹窗真实照片毛玻璃、重叠请求、忙时关闭及偏好重启。原生 FP16 像素读回通过 SDR/原生 HDR、真实 TrueHDR-only（VSR 调用 0、亮部 12.656 > 1）、独立/组合 VSR、40 次像素一致的缓存对照、原图透明度、ICC/EXIF 和真实玻璃检查。

## 0.1.6 图库样式与空间利用

图库操作按钮继承 WinUI 的 DefaultButtonStyle，恢复原生背景、边框、悬停与按下状态，圆角使用与排序控件相同的 ControlCornerRadius。此前独立 GalleryAction 未继承原生样式，圆角属性回到 0，导致直角轮廓。

相册和照片卡片仍为原生 Button，点击、右键和系统键盘焦点保留；卡片模板移除覆盖整个图文区域的悬停底色。Composition 的 1.018 倍悬停缩放只作用于内部内容，在周围预留 6 逻辑像素，避免直接缩放按钮布局边界、碰到滚动视口的裁切；图片保留自己的圆角。

保留原生 ScrollViewer 及虚拟化列表，内容右侧留出 28 逻辑像素，滚动条向图库侧边留白对齐；窄窗口采用较小外边距。底部固定提示行移除，路径或“照片保留在原文件夹”与数量同处标题下方。长路径省略、悬停显示完整文字，离线提示仍保留。

Computer Use 在正式便携版、系统深色与 150% DPI 下检查约 1148 和 761 逻辑像素宽窗口：按钮圆角一致；相册悬停后的名称、数量和图片四角完整，没有矩形悬停底色；实际点击相册进入照片页，滚轮及原生滚动条拖动正常。展开的滚动条与末列照片之间保持空隙，标题行路径可读，底部不再占用固定提示区域。检查使用现有相册，没有修改原始文件。

源码 Release 构建 0 警告、0 错误，自包含发布成功。最终回归报告 `artifacts/viewer-interaction-20261012-235249/viewer-interaction.json` 完整通过：预读连续 10 张首帧中位 7 ms、最大 9 ms；随机跳转 6 张中位 272.5 ms、最大 308 ms（含缓存命中/未命中）；40 次缓存原图/HDR 提交中位 6.59 ms、最大 7.51 ms，新增后台任务和 HDR 调用为 0；两个 HDR 窗口同时打开时，24 次图库尺寸更新至两次合成回调中位 38.7 ms、最大 88.9 ms。覆盖刷新、损坏封面回退、缩放/平移、全屏恢复、快速翻图及按住原图期间的后台处理。上述数值是提交/合成回调时间，并非每帧屏幕响应测量。

## 0.1.5 相册刷新、窗口响应与预览交互

`._` 开头的 AppleDouble 元数据即使使用图片扩展名也不再作为照片导入或列举。在线相册的完整扫描将已删除条目标为缺失，相册和所有照片查询只返回仍存在的关联；保留缺失索引、收藏和历史，不删除用户文件。离线、不完整或取消的扫描保留已知内容。失效的指定封面回退到当前相册的可用照片；首个文件解码失败时继续尝试其他封面。21 项核心检查包含这些行为、重叠导入源各自刷新和离线保留。

窗口尺寸变化仅在跨越布局档位时切换导航模式和工具栏布局；未变更的列表保留 ItemsSource，后台文件监听只在索引实际改变时重载。缩略图使用 32 MiB/96 项的已解码缓存及并行请求去重，减少尺寸变化引起的重复 Shell/解码工作。原生尺寸、视角与玻璃更新合并为一次呈现。每 500 ms 的 DXGI 显示器探测改到后台，界面查询读取缓存状态；探测本身不执行 AI 或重复绘制。原图对照仍优先直接提交。

照片点击切换浮层的显示与隐藏；手动隐藏不因鼠标活动恢复，拖拽超过阈值不作为点击。第二次点击或主动打开菜单恢复操作。空格由 WinUI PreviewKeyDown/PreviewKeyUp 在按钮、缩略图及增强弹窗前处理，回车仍使用原生控件确认行为。选中缩略图具有完整强调色边框，96×72 逻辑像素外框包围 88×62 照片；失焦后保留选择。滚轮缩放采用 180 ms 缓动，连续滚动更新目标，拖拽从当前呈现状态开始。全屏使用系统 presenter 和 240 ms Windows Composition 内容缩放过渡，退出图标/提示/辅助功能名称同时改变，退出后恢复原位置、尺寸或最大化状态。遵循 Windows 动画设置；内容过渡不等于 DWM 窗口外框的动画。

最终真实 WinUI 报告 `artifacts/viewer-interaction-20261012-223856/viewer-interaction.json`：

| 测量/检查 | 最终结果 |
| --- | --- |
| 预读命中连续 10 张首帧 | 中位 8 ms，最大 10 ms |
| 随机跳转 6 张首帧 | 中位 127.5 ms，最大 270 ms；混合缓存命中与未命中 |
| 新窗口至首张照片呈现 | 1010 ms，照片加载后的首帧 303 ms；不含整个应用冷启动 |
| 40 次缓存原图/HDR 对照提交 | 中位 6.59 ms，最大 40.41 ms；新增后台任务、HDR 调用为 0 |
| 两个 HDR 预览打开时，24 次图库尺寸更新至两次 Composition 回调 | 中位 29.74 ms，最大 116.11 ms；布局物理宽度与客户区匹配 |
| 手动浮层、完整选中框、连续缩放、全屏状态和窗口恢复 | 真实 WinUI 状态及最终尺寸断言通过 |
| 临时图库删除、元数据过滤、损坏首封面回退 | 自建测试文件验证通过，封面回退到 photo-01.jpg |

这些时间记录函数提交、首图提交或合成回调，不是高速相机测得的显示延迟，也不代表快速拖动中每帧都没有黑块。Computer Use 实际缩窄正式图库窗口后，导航折叠、网格重新排布且捕获画面完整，未见持续的未绘制区域；连续高速拖动与跨屏逐帧覆盖仍在待办中。真实 Lightroom 相册复验为 98 张、首行照片与封面正常，先前 `._` 占位不再显示。真实相册只读，删除测试只作用于自建临时文件。

Computer Use 实际键盘验证记录在独立测试目录 `artifacts/input-ui-20261012-214812/keyboard-input.jsonl`：聚焦放大按钮后空格按下/松开保留 32% 缩放，回车将其放大到 40%；聚焦缩略图后空格保持当前选图与 40% 缩放。增强弹窗焦点下也接收原图对照。已有 HDR 的按下/松开不增加 SDK 计数或后台任务，松开恢复实际生效状态；全屏按钮显示“退出全屏”，退出恢复窗口。仅显式测试模式记录键盘断言，正式应用不记录按键。

原生 WIC/D3D11 检查通过：真实 7008×4672 JPG 成功执行官方 TrueHDR，VSR 计数为 0；合成测试高光 FP16 读回超过 1.0，浮点原生 HDR 的 4.0 值仍保留。40 次缓存对照分别像素完全匹配原图/增强纹理；独立 VSR 和 HDR+VSR 通过。增加了后台准备 HDR 时保持原图选择、显示器探测不额外呈现、尺寸和视角批量准备后只统一绘制的回归检查。没有采用 SDR 截图评价 HDR 亮度。

生命周期报告 `artifacts/viewer-lifecycle-20261012-223915` 完成 49 个窗口/两次新进程启动，覆盖 HDR/VSR 后关闭重开、并发增强、处理中关闭、重叠请求、弹窗与偏好恢复。独立无 SDK 核心 `artifacts/no-nvidia-0.1.5/Luma.Native.dll` 通过普通 SDR、原生 HDR、玻璃和正确不可用状态验证；正式目录保留带 SDK 的核心。源码与自包含发布构建均为 0 警告、0 错误。

## 0.1.4 原图对照延迟回退修复

0.1.3 的空格按下/松开仍调用完整 `ApplyEffectsAsync`，松开时还经过已完成的初始化和 GPU 完成等待。键盘自动重复会取消并重新创建增强请求；即使原图和 HDR 均已驻留显存，界面仍先显示“正在处理显示增强”。这属于对照路径的性能回退，不是 TrueHDR 模型必须重新计算。

新路径单独改变呈现所选纹理，保留请求的 HDR/VSR 模式与缓存；键盘重复状态直接忽略。空格、“按住查看原图”及失去焦点后的恢复均使用同一路径，不重新解码、上传、初始化或调用 SDK，不修改保存的增强设置。若照片或增强尚在处理中，仅保留最后的对照状态，后台继续生成结果，完成后仍按当前按住/松开状态呈现。关闭和设备重建保留原有串行 D3D 访问与生命周期检查。

最终真实 WinUI 报告 `artifacts/viewer-interaction-20261012-202448/viewer-interaction.json`：40 次原图/HDR 切换调用中位数 2.16 ms、最大 2.83 ms；200 次重复按住状态不产生新任务；后台任务、HDR 调用和 VSR 调用的增量均为 0，切换后的生效状态正确且没有“正在处理”提示。这是切换函数与呈现提交耗时，不是使用高速相机测量的屏幕响应时间。还通过了按住原图切换照片、照片/增强处理中按住，再松开恢复已生成 HDR 的检查。连续 SDR 翻图首帧中位数仍为 8 ms，随机跳转一组为 143 ms，未回退上一轮加载优化。

原生 FP16 GPU 读回验证 40 次原图/HDR 切换：原图与已生成 HDR 的像素分别逐次完全匹配，SDK 计数不变；VSR 单独及 HDR+VSR 缓存恢复也通过。真实 7008×4672 JPG 的 TrueHDR-only 调用成功，VSR 调用为 0。无 NVIDIA SDK 的独立构建通过 SDR、原生 HDR 与材质检查。

最终生命周期报告 `artifacts/viewer-lifecycle-20261012-202647`：49 个真实窗口 / 两次全新启动全部通过，增加了各个独立 HDR/VSR 窗口的原图对照及缓存恢复断言。覆盖忙时关闭、重叠翻图、双窗口增强、弹窗材质、缩略图滚动及保存偏好恢复，未修改原始照片。

Computer Use 在正式图库打开用户此前使用的夜景 JPG，聚焦照片画布后实际发送空格键；松开后的即时状态捕获为“RTX HDR 已生效”，照片与选择位置保持不变。工具发送的是按下/松开的组合操作；长按重复和处理中保持状态由上述真实窗口集成测试覆盖。源码构建 0 警告、0 错误。

## 0.1.3 翻图性能、窗口位置与交互

按窗口的物理像素尺寸读取适配照片，JPEG 优先使用 WIC 解码器的原生降采样，再进行 ICC 转换。缓存区分显示预览和完整解码，保留 256 MiB 预算；前台加载与后台邻图预读使用独立队列，未完成的旧请求不能覆盖当前照片。放大超过预览细节或当前硬件/显示器支持的 HDR/VSR 增强启用时才读取完整照片，完整照片用于 SDK 输入，VSR 仍独立控制。普通 SDR 渲染不初始化 NVIDIA；首次打开增强面板或请求增强时再检测，未成功调用 SDK 不显示已生效。加载指示延迟 180 ms 出现，缓存命中不闪烁加载动画。

最终真实 WinUI 测试使用本机 36 张照片的只读文件夹及独立数据库，结果位于 `artifacts/viewer-interaction-20261012-192620/viewer-interaction.json`：

| 测量 | 实测 |
| --- | --- |
| 预读命中连续翻图 10 张首帧 | 中位数 8 ms，最大 10 ms |
| 随机跳转 6 张首帧 | 中位数 143 ms，最大 314 ms；含缓存命中与未命中 |
| 新建预览窗口至首张照片呈现 | 1042 ms；其中进入照片加载后的首帧 375 ms，不含应用冷启动 |
| 普通 SDR NVIDIA 初始化 / 增强调用 | 0 / 0 |
| 适配 SDR 的应用自有 GPU 纹理估算 | 65 MiB，不含驱动与 SDK 内部分配 |
| 完整大图 HDR 的应用自有 GPU 纹理估算 | 592 MiB，不是显卡总占用 |

独立解码基准 `artifacts/decode-benchmark-current.json` 中，7008×4672 JPG 五次完整解码中位数约 606 ms、2048×1366 显示预览约 326 ms；CPU 解码帧分别为 130,965,504 / 11,190,272 字节。这个基准不含窗口、GPU 上传和呈现，不能与预读缓存翻图直接比较。

窗口在显示前按目标显示器的工作区居中、按其 DPI 设置大小，并为工作区留出边距。图库打开预览时明确使用图库所在的显示器。本机工作区 2560×1528、150% 缩放，图库为 (410,164)/1740×1200，预览为 (350,126)/1860×1275；集成检查确认居中误差不超过 2 物理像素且不越界。当前桌面只暴露一个活动显示区域，多个显示器和不同任务栏位置仍待实机验证。

缩小到适配大小及以下时强制居中；放大后按照片实际溢出范围限制平移，使用指针捕获实现拖拽。十个重叠请求只显示最后选中照片；放大后确认重新加载至 7008×4672，并保留当前视角，恢复适合窗口后偏移为零。Computer Use 实际操作也确认了滚轮缩小归中、放大拖拽和图库按钮对齐。图库操作统一为 36 逻辑像素高度，图标 16 像素，打开照片增加文字与快捷键提示。

玻璃面板改用通道相等的浅灰/深灰底色，原生 Direct2D 高斯模糊继续采样照片；照片本身的颜色仍可能出现在玻璃中，移除的是固定蓝色底色。GPU 检查确认真实 FP16 照片/模糊路径、透明度、模糊连续边界与面板外像素；JPEG ICC、EXIF、PNG alpha 和浮点原生 HDR 的显示预览均通过。无 NVIDIA SDK 的独立 DLL 构建也通过普通 SDR、原生 HDR 和玻璃检查，不影响正式构建。

最终构建生命周期报告 `artifacts/viewer-lifecycle-20261012-192840` 完成 49 个真实窗口 / 两次全新启动：HDR/VSR 独立与组合、关闭重开、双窗口、忙时关闭、快速翻页、增强弹窗、缩略图滚动条和跨进程恢复 HDR-only 均通过。源码和发布构建成功，未修改原始照片。新窗口准备时间、应用冷启动、完整进程资源测量及体积精简继续记录在 [待办清单](TODO.md)。

## 0.1.2 偏好、导航与原生模糊

主题未设置时改为 `ElementTheme.Default`，明确选择浅色/深色时仍保留选择。HDR、VSR、风格、自动亮度/峰值与手动参数保存至 SQLite 中的 `viewer.preferences`；新照片自动恢复偏好，是否生效继续根据当前 GPU、屏幕和照片的实际状态显示。

底部列表移除翻图按钮，显示原生横向滚动条。普通滚轮转为横向浏览，使用 WinUI `ScrollViewer.ChangeView` 的动画；滚动不会更换当前照片。左右预览边缘为悬停翻图区，按钮通过 Composition 淡入/淡出。缩略图使用原生 Entrance / AddDelete 主题过渡；窗口首帧准备后加入 260 ms 的 Windows Composition 内容淡入和位移，遵循 `UISettings.AnimationsEnabled`。窗口框架仍由 Windows 管理，没有用 GDI 的旧窗口快照动画套在 DirectComposition 上。

模糊核改为 Windows Direct2D `CLSID_D2D1GaussianBlur`，16-bit 浮点精度、24 逻辑像素标准差、随 DPI 缩放，低分辨率目标控制成本。保留模糊后的预乘 alpha，并与面板底色混合；此前对模糊颜色除以 alpha 会消除边缘渐变，造成照片与留边的硬切。原生 GPU 验证涵盖交界处连续性、照片外像素不变、浅/深色和弹窗遮挡，真实 7008 × 4672 照片的 HDR-only 调用成功。无 SDK 构建的原生 SDR / HDR / 玻璃检查也通过。

已通过 Computer Use 对照本机 Windows 照片应用打开的同一组照片：缩略图滚轮移动到后半部分时，标题及当前照片保持 DSC00864.jpg，底部保留横向滚动条；观察了更多菜单的原生深色材质。未修改照片或系统动画设置。HDR 截图过曝仍不能作为光度证据。

最终实机复验使用独立测试数据库。首次启动无需选择主题就显示深色；滚轮使缩略图列表偏移约 1080 逻辑像素，拖动原生滚动条后约为 2527，标题和位置始终保持 DSC00864.jpg / 7 of 36。预览右侧悬停按钮切换到 DSC00923.jpg / 8 of 36。修复了直接文件启动时列表先隐藏、模板稍后创建导致滚动条没有绑定的问题，布局后重新绑定当前 ScrollViewer，检查原生滚动条与列表偏移一致。滚动条采用原生 MouseIndicator 状态与模板过渡。

通过实际界面开启 HDR、关闭进程，随后用同一测试图库打开另一张 7008 × 4672 夜景照片，未操作开关即显示 RTX HDR 已生效。深色增强弹窗可读，VSR 保持关闭。深色 GPU 材质仅限制控件背景的亮度，并避免将未经模糊的 HDR 高光重新混入面板；照片输出不受这项调整影响。原生 Gaussian 的照片/留边连续性、深色对比度、面板外 FP16 像素不变及缺 SDK 的独立构建均通过。

窗口日志记录原生 Composition 的 260 ms 进入动画以及 ScopedBatch 完成事件；系统动画设置为启用。此证据确认使用并执行了 Windows 原生内容动画，不代表对窗口框架的系统缩放动画或远程连接下每帧的观感作出保证。未声称完成高帧率录屏。

最终回归三轮全部通过：每轮 48 个预览窗口及一次新进程恢复偏好，总计 147 个真实预览窗口、6 次全新启动。覆盖同进程关闭重开、HDR/VSR 独立与组合、忙时关闭、重叠翻图、5 次展开增强弹窗、图库序列与直接文件启动下滚动条的范围/偏移同步，以及跨进程自动恢复 HDR-only。每个进程均正常退出，JSON 完成标记与窗口数同时符合预期。本机报告为 `artifacts/viewer-lifecycle-20261012-172935`；照片、数据库和完整日志仅留在忽略的测试目录，不随应用打包。最终源码构建 0 警告、0 错误。

## 0.1.1 崩溃、首帧与材质修复

用户复验发现此前验收没有覆盖 HDR 预览关闭后反复新建窗口。旧版故障已重现并捕获完整本地转储。主要错误为 WinUI 的 `0x8000FFFF`：`CXcpDispatcher::OnReentrancyProtectedWindowMessage` 位于 Windows Storage / Shell 缩略图 RPC 同步等待的消息泵中。即使方法名为 Async，也不能从 XAML 的 Loaded / 布局回调直接发起这类调用。转储包含私人进程内存，只留在本机忽略的 artifacts 目录，没有放入源码或发行包。

修复将 StorageFile、缩略图和流读取全部移至工作线程，界面只接收内存字节并解码缩略图。过期及卸载请求不会重新更新控件。加载指示改为简单的 XAML 路径与 Composition 旋转。初始窗口先隐藏未绘制状态、按实际窗口 DPI 设置尺寸、等待两次合成帧，然后才显示；异步续体不会在 Rendering 回调中直接重入显示窗口。

D3D / NGX 初始化与最终释放在后台，SwapChainPanel 的绑定/解绑留在界面线程。关闭时先取消请求并解绑面板，等待已有 GPU 工作结束后释放资源。NGX 初始化、调用及关闭使用进程内锁，防止不同窗口并发调用非线程安全 SDK。补充测试另发现窗口销毁后的主题回调可能访问已消失的 AppWindow / 标题栏；主题与 Loaded 回调均增加关闭检查，标题栏更新同时检查原生对象是否存在。

实际照片材质验证使用真实 FP16 GPU 读回：红/蓝测试图的玻璃分别读到 `[5.164, 2.102, 2.162]` 与 `[2.031, 2.129, 5.305]`，证明颜色来自照片；边界高斯混合通过，面板外像素逐位不变；隐藏浮层恢复原图像素；透明留边 alpha 为 0，半透明照片使用预乘 alpha。弹窗独立背景也保留照片颜色，并遮挡下层 XAML 控件。弹窗背景的 SDR 位图转换仅用于控件，HDR 照片仍为不经 8-bit 控件的 FP16 输出。

`tests/verify_viewer_lifecycle.py` 从独立图库目录启动真实 WinUI 窗口，覆盖 HDR/VSR 独立开关、关闭重开、展开增强弹窗、双窗口同时增强、八个重叠翻图请求、初始化中关闭、HDR 处理中关闭，以及新建缩略图树。每轮 44 个预览窗口，结果逐步写入独立 JSON；进程退出和完成标记共同验证，不能把半途崩溃当作通过。132 个窗口 / 三次全新进程启动全部完成。

当前 Computer Use 没有原生录屏接口，因此启动动画采用连续窗口帧观察及首帧/尺寸日志检查，不声称完成 60 fps 录屏逐帧分析。实际捕获点击照片后约 0.8–1.4 秒的六帧：已绘制的 Acrylic 窗口外壳、旋转加载指示和随后完成的缩略图，未出现旧版黑色错位窗口。该检查也发现初始化后才绑定照片序列会使底部栏突然变宽，现已在显示前准备已知序列；直接文件打开时，同目录枚举与初始化并行，并在完成后一次显示缩略图栏，不阻塞当前照片解码。已显示窗口使用原生动画，最终尺寸在显示前确定。仍需在更多 Windows 构建、显示拓扑及系统动画设置下扩展覆盖。

最终版本于 15:01 再次通过 Computer Use 打开真实 7008 × 4672 夜景照片，连续捕获点击后约 0.98–1.69 秒的六帧。六帧中标题栏、工具栏和约 920 逻辑像素宽的缩略图栏位置/尺寸稳定，加载指示持续旋转，未观察到黑色错位窗口或底部栏撑开。照片加载后，深色浮层和展开到缩略图区域的增强弹窗均显示照片的模糊光影；弹窗正确遮挡下方缩略图。随后单独开启 HDR，真实界面显示 TrueHDR 已生效、VSR 关闭，关闭照片后图库继续运行。此观察不能覆盖点击后首个约 0.98 秒内的每一帧，也不能替代完整高帧率录屏。

## 环境与构建

- Windows 11，系统构建 26300；Windows SDK 10.0.26100.0。
- NVIDIA GeForce RTX 5070 Ti Laptop GPU，驱动 616.64；存在混合显卡与多个 DXGI 显示路径。最终桌面枚举仅有一个活动显示区域，150% 缩放。
- .NET SDK 8.0.425 / 自包含运行时 8.0.31；Windows App SDK 2.5.1；官方 RTX Video SDK 1.1.0。
- Release 原生核心与 WinUI 项目构建成功；最终源码检查无警告、无错误。
- 自包含便携版位于 `artifacts/Luma`，整个目录一起使用。

## 自动功能验证

15 项图库检查全部通过：根目录路径、重复导入、递归索引、汇总照片、相册元数据、重叠文件夹去重、刷新保留收藏、最近打开、离线保留、不完整扫描、取消扫描、移除相册保留原文件及历史、设置持久化、搜索通配字符转义。

实际 WIC / D3D11 / NVIDIA 运行检查通过：

| 检查 | 结果 |
| --- | --- |
| SDR 色彩、Windows SDR 白电平 | GPU 读回符合 sRGB 转线性与系统白电平 |
| 完整适配、缩放、旋转、EXIF 方向 | 真实渲染检查通过；适配产生留边，无自动裁切 |
| 损坏文件 | 返回解码错误，无伪装成功 |
| 仅 TrueHDR | 官方 SDK 调用成功，VSR 调用计数保持 0 |
| VSR 单独 / HDR + VSR | 独立与组合调用通过，返回 HDR-only 正确更换输入 |
| 静态缓存、按住原图 | 不重复计算缓存结果，原图状态取消增强 |
| 浮点 JPEG XR | scRGB 的 4.0 线性值通过 FP16 呈现，不被截成 1.0，绕过 SDR 增强 |
| 4K JPG 高光 | 本轮 FP16 读回值约 7.434，HDR 高光未经过普通 8-bit 图片控件 |
| 7008 × 4672 JPG | 完整解码、真实 HDR-only 成功，HDR/VSR 调用 1/0 |
| 16000 × 5000（8000 万像素）测试图 | 按预算解码为 14310 × 4472，增强处理 13834 × 4323，真实 TrueHDR 成功 |
| 普通缩小采样 | 细密棋盘纹理收敛到线性光平均值，无 AI 超分调用 |
| 后台准备结果 | GPU 完成查询通过，准备操作不呈现过时结果；最终呈现留在界面线程 |
| 缺少 NVIDIA SDK 的独立构建 | SDR / 原生 HDR 检查通过，SDK 与增强计数为 0，准确报告缺少 SDK |

预算用于应用持有的解码缓存及纹理；NVIDIA 模型、驱动与系统自身分配不包含在纹理估计中。每个预览窗口会持有自己的渲染设备与缓存。不能将 256 MB 解码预算解释为整个进程内存上限。

## HDR 变暗的修复与测量

旧实现固定使用 50 nits 中间灰，但本机测试显示路径的 Windows SDR 白电平为 480 nits，导致原图与 HDR 的中间调基准不匹配。修正后，直接设置 SDK 的中间灰参数，按系统 SDR 亮度计算并遵循 SDK 的 10–100 nits 范围；没有用亮度滤镜冒充 NVIDIA HDR。

使用真实 FP16 渲染目标读回测量，避免 Windows HDR 截图过曝影响判断。以下数值为目标中的线性亮度，单位 nits，并非对物理屏幕的光度计测量。

| 测试内容 | 原图 | 旧 50 nits HDR | 自动匹配 HDR |
| --- | ---: | ---: | ---: |
| 灰阶中间调（P50） | 103.59 | 51.32 | 102.41 |
| 日落照片平均亮度 | 125.96 | 70.52 | 126.10 |
| 日落照片高光（P99） | 300.17 | 194.60 | 311.71 |
| 夜景照片高光（P99） | 260.54 | 268.81 | 384.33 |

模型仍可能降低暗部，HDR 不等于把每个像素都变亮。自动峰值遵循 SDK 的 400–2000 nits 范围；当前某个显示路径报告的 DXGI 峰值大于此范围，因此界面显示传给 SDK 的实际参数，而不把报告值当作物理测量。支持手动选择峰值及中间灰。

本轮测试路径报告峰值 595.2 nits、SDR 白电平 480 nits。表中数据已按本轮输出更新，不能与不同峰值设置下的旧报告混用。测量脚本为 `tests/verify_hdr_luminance.py`，完整本机输出在 `artifacts/native-tests/luminance-report.json`。原照片及其完整报告未放入便携版、MSIX 或 Git。

## 实际界面检查

使用 Computer Use 操作原生应用，在独立测试数据库中导入本机文件夹，照片只读。

已经实际观察：

- 图库与相册真实照片缩略图、索引数量、搜索单张照片并清空搜索恢复全部照片。
- 浅色及深色主题、原生标题栏按钮、Mica 外壳与悬浮面板。
- 预览按比例完整适配，顶部工具栏与有两侧/底部留白的居中缩略图栏。
- 闲置隐藏后，键盘操作恢复浮层；信息面板和缩略图导航可用。
- HDR 单独、VSR 单独及二者组合在真实 SwapChainPanel 窗口中显示已生效。
- 展开亮度参数后，内容受窗口高度限制，可滚动，未把整个窗口变成固定设置栏。
- 连续三次翻图从第 11 张到第 14 张，最终文件名、位置及增强状态一致；窗口保持运行。
- 后台呈现导致的预览异常已修复：后台准备纹理，界面线程呈现；修复后预览和组合增强保持运行。
- 最终预览缩至约 501 × 521 逻辑像素，照片保持完整比例；浮层与缩略图栏在窗口内保留边距。增强面板可滚动，HDR-only 在此窗口中真实生效，VSR 保持关闭。
- 图库缩至约 581 × 551 逻辑像素，导航自动收起；相册、双列照片网格、收藏及可滚动设置页可读。
- Ctrl+D 收藏操作写入独立数据库，重启测试实例后收藏页仍只显示该照片，原文件未改变。
- 直接文件启动后无需先点击照片即可按 F11。普通窗口进入/退出全屏后，1228 × 844 的尺寸与位置 (85, 76) 完全恢复；最大化窗口进入/退出全屏后仍保持最大化。
- 缩略图及图库项目的辅助功能名称为文件名或相册名，不再朗读内部对象字段。
- 便携版注册后，Windows 的 `SHAssocEnumHandlers` 实际返回 Luma JPG 查看器，显示名称与可执行路径正确；开始菜单快捷方式已生成。

最终桌面解锁后，上述全屏、小窗口和收藏检查已继续完成。没有将锁屏时捕获到的旧窗口截图当作有效实机验收。

以下项目仍未声称完成完整实机验收：

- 文件选择器能正常打开，格式过滤包含实际支持的扩展名；Computer Use 未返回独立 PickerHost 窗口，不能完成选取文件的整条交互路径。命令行文件打开与同文件夹翻图已通过。
- 默认应用按钮已实际打开 Windows 设置。注册信息包含 11 种扩展名、应用名称、启动命令和开始菜单入口；系统接口识别 Luma。但本机应用专属 URI 仍落到通用默认应用页，应用列表搜索未显示 Luma。按 .jpg 能打开系统选择弹窗，但工具无法稳定继续操作此弹窗，因此没有声称已完成默认应用选择。设置页提供按扩展名选择的说明，没有改写 UserChoice。
- 最终只存在一个活动桌面显示区域，未测试真实跨屏拖动、不同 DPI 屏幕切换；未改变显示拓扑来模拟多屏。
- 休眠恢复、实际设备丢失、非 RTX 真机覆盖仍需扩展。缺少 SDK 的独立构建已经验证，但不能替代非 RTX 硬件覆盖。
- HDR 亮度修复已通过 FP16 数据测量；最终主观观感仍由用户在实际显示器上验收，HDR 截图不作为物理亮度证据。

## 打包与发行边界

MSIX 使用 Windows SDK MakeAppx 验证清单与文件内容。包包含实际支持的 11 种扩展名、开始菜单入口、原生核心及自包含运行库。MSIX 未签名，当前使用便携版；没有修改系统证书信任或用户的默认关联选择。

最终 ZIP 和 MSIX 的全部应用文件逐一核对一致，压缩包完整性通过；NVIDIA DLL 与许可 PDF 与原始 SDK 字节一致。包中未包含用户照片、图库数据库、测试日志或 SDK 开发工具。校验值保存在 `artifacts/SHA256SUMS.txt`。

MIT 仅适用于 Luma 原创代码和几何图标。已保存实际依赖的许可、NOTICE、Apache-2.0 原文，以及不变的 NVIDIA 许可 PDF。NVIDIA SDK / DLL 保持独立许可。公开发行前仍需 NVIDIA 许可条件与通知/商标核对、名称检查、签名和商店认证。

没有公开上传、创建远程仓库或发布商店。源码保存在本地 Git 仓库。

本地 Git 保存实现及后续修复。没有配置远程仓库，没有推送或公开发行。
