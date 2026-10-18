# 项目主页

`site/` 是纯静态页面：HTML、CSS 和约 190 行 JS，没有构建步骤，也不加载第三方字体、脚本或统计。页面里的界面图是 Luma 在 Windows 11 上的实机截图，只有“照片信息”卡片例外（它的面板很长、会滚动，一张截图拍不全，所以按应用的实际界面用 HTML 重绘，见下文）；首屏是整窗截图（一个带 HDR 增益图的 AVIF），所有访客只下载这一个文件；HDR 屏幕上的对比模式另取一张 11 KiB 的徽标覆盖层。页面不写版本号：能从 GitHub 的公开 API 读到最新发布时才显示（见“版本号”）。任何静态托管都能直接使用，包括 GitHub Pages。

## 预览与素材

- 预览：`python -m http.server 8080 --bind 127.0.0.1 --directory site`，打开 `http://127.0.0.1:8080/`。
- 输入是被忽略的 `artifacts/promotional-previews/` 里的五个文件（换截图就是覆盖这些文件名后重跑下面两条命令）：

  | 文件 | 内容 | 用在 |
  | --- | --- | --- |
  | `hero-capture.jxr` | 看图窗口的 Game Bar HDR 截图（2118 × 1259，float16 scRGB；雪山照片 `DSC06998.jpg`，2026-10-18 22:16） | 首屏 HDR |
  | `hero-sdr.png` | 同一窗口的 SDR 截图（Snipaste，2118 × 1255，窗口显示“SDR · 原图”，22:18） | 首屏 SDR 基底、旧浏览器的 WebP、分享图 |
  | `live-sdr.png` | 看图窗口显示一张实况照片（工具栏里有实况图标）的 SDR 截图 | 实况演示 |
  | `ui-favorites.png` | 图库“收藏”页，带 HDR 与实况角标 | “原生 HDR”卡片 |
  | `ui-albums.png` | 图库“相册”页 | “相册”卡片 |

- `python scripts/Prepare-SiteAssets.py`（Pillow，需带 WebP 与 AVIF）：从截图裁出 `card-badges.webp`、`card-albums.webp`、`live-demo.webp`（q90–92，视觉无损，不是逐比特无损），旧浏览器用的 `hero-1100.webp`（q72），1200 × 628 的分享图 `og.png`，以及 logo、`apple-touch-icon.png`、`favicon.ico`。裁剪框是脚本开头的常量。分享图保持全彩 PNG：调色板版本会让渐变出现色带，而源码检查不允许提交 JPEG。
- 为什么卡片图用有损：相册页（2025 px 宽）逐比特无损 1510 KB、q95 489 KB、q90 351 KB、q85 272 KB；实况演示无损 857 KB、q90 182 KB。要逐比特无损，页面会明显变重。

## 首屏：整窗实机截图 + 一张带增益图的 AVIF

- 版面：标题、两行简介、下载按钮，之后只有一行小字（系统，以及 RTX HDR 与 VSR 需要兼容的 NVIDIA RTX 显卡；读到最新发布时前面加“当前正式版 x.y.z”），窗口紧随其后；1568 × 810 的屏幕上，首屏能露出窗口约 215 px。小字曾是两行，窗口前又空出约 70 px，整块看起来悬空；不要再加第二行，新增的说明放进简介或页脚。≤ 640 px 时这一行的两项分成两行，不留悬空的分隔点（版本号跟“Windows 11 x64”在同一行）。简介里的“普通照片，甚至实况照片，也能借助 NVIDIA RTX Video HDR 增强光影”对应 README 的“甚至使用 HDR 预览 live 照片”。
- 窗口：不再用 HTML / CSS 重绘，界面只有一套，应用改版后换一张截图即可。`.win` 的圆角（宽 0.8%、高 1.35%，按百分比缩放，比窗口自己的圆角略大）裁掉矩形截图在窗口圆角外留下的几个桌面像素。
- 图片：`hero-1834.avif` 与 `hero-1100.avif` 是同一张图的两个宽度，用 `<picture>` 的 `srcset` / `sizes` 选择。SDR 屏幕和不支持增益图的浏览器显示它的 SDR 基底；不支持 AVIF 的旧浏览器用 `hero-1100.webp`。页面注明这是 SDR 实机截图。
- 对比：在开启 HDR 的屏幕上，用支持 HDR 图片和 CSS `dynamic-range-limit` 的浏览器（Chrome / Edge）打开，`site.js` 把同一个文件再叠一份：下层 `dynamic-range-limit: standard`（SDR），上层是 HDR，分界线可拖动 / 键盘操作。窗口界面在两半边都是 SDR 白，所以只有照片的高光和中间调不同。判断在 `hdrCapable()`；解码失败则保留普通图片。两份是同一个 URL，从缓存取，没有第二次请求。移动的是整个 `<picture>`：把 `<img>` 单独搬出 `<picture>` 会让浏览器改用 WebP。
- 徽标覆盖层：两张截图的窗口右上角写的不一样（SDR 图是“SDR · 原图”，HDR 图是“RTX HDR 已生效”），一张增益图装不下两种文字。`Prepare-SiteHdr.py --badge` 在增益图里让这个框保持 SDR，并另存 `hero-hdr-badge.webp`：整幅大小的透明无损 WebP（11 KiB），只有徽标框（较大那个徽标四周各加 3 px，圆角、2 px 柔边）里是 HDR 捕获自己的像素。`site.js` 只在建立对比时取它，放在 HDR 一半上，和 HDR 层共用同一个裁剪，所以拖动分界线时徽标跟着换；文件取不到就保留“SDR · 原图”，对比照常可用。柔边处与 HDR 层的差别（脚本打印）平均 4.3 个码值，最大 23。`index.html` 里 `figure` 的 `data-badge` 指向它；两张截图的标签相同（例如同一帧）时不加 `--badge`、去掉该属性即可。
- 生成：`python scripts/Prepare-SiteHdr.py --avifgainmaputil <路径> --register --badge 1917,84,2088,136`。需要 `imagecodecs`、NumPy、Pillow、`opencv-python-headless`，以及 libavif 1.2 以上的 `avifgainmaputil`（从源码构建，打开 `AVIF_BUILD_APPS`）。输出 `hero-1834.avif`、`hero-1100.avif` 和 `hero-hdr-badge.webp`。`--badge` 的四个数是较大那个徽标（“RTX HDR 已生效”）在 SDR 截图里的像素框，裁边之前；这一对是放大 5 倍、加网格线后目测量出的，误差 1–2 px，脚本已在四周各留 3 px。窗口外沿的 2–3 px 是轮廓和圆角外的桌面像素，脚本四边各裁 `--crop 2`，得到 2114 × 1251，再缩到 1834 宽（`--width`），文件大小保持在上一版的量级。
- 配准：这一对来自两个工具（Game Bar 的 JXR，Snipaste 的 PNG），窗口位置差整整 (+2, +4) 个像素（JXR 的上边多 4 行、左边多 2 列轮廓，右边比 PNG 少 2 列，被 `--crop` 裁掉），`--register` 找到这个整数位移后直接裁切，不重采样，不会糊。别用 `--register affine`（ECC）去对这一对：照片在两帧里的渲染本来就不同（见下面“HDR 目标图”），ECC 被带偏到 (+2.27, +4.00)，在两帧完全相同的标题栏上平均误差 1.009 个码值，整数位移是 0.714（工具栏 5.20 对 4.37）。`affine` 留给缩放不同的一对，例如旧雪山那一对（HDR 帧里的照片小 1.1%）。Game Bar 自己写出的 JXR + PNG 零位移，不加 `--register`。
- SDR 白：捕获时 Windows 的“SDR 内容亮度”决定 1.0 对应多少 nits。窗口自己的浅色平面界面（标题栏）在两帧里相同、没有被裁剪或色调映射，脚本优先从它读出 SDR 白（本次 6.016 × 80 = 481 nits，几种读法给出 480–481 nits）；找不到才退回照片里暗到中间调的像素。也可用 `--sdr-white` 指定。
- HDR 目标图：这次的 SDR 图是应用显示“原图”时的截图，HDR 图是 RTX HDR 的显示，两帧里的照片本来就渲染得不同（RTX HDR 的中间调更亮、阴影更深）。脚本把目标图做成“SDR 截图 + HDR 捕获高出 SDR 的部分”：HDR 一侧不比 SDR 暗，差别小于 SDR 白的 5% 的丢弃、大于 12% 的整体保留，中间平滑过渡（`hdr_target()`），界面因此在两半边保持一致。HDR 层是“缩放后的基底 + 缩放后的差值”，而不是把两张图各自缩放：这样没有差别的地方增益恰好为 1，文字边缘不会出现多余的增益（增益图的范围也从 −0.55…1.85 收窄到 −0.03…1.39，log2）。
- 编码：`avifgainmaputil combine`，基底 q85、4:4:4、8 位，增益图 q70、半分辨率。1834 宽 386 KiB，1100 宽 172 KiB（灰雁那一版是 611 / 295 KiB：这张照片的天空平滑，窗口也更小）。备用余量 1.08（log2，即 2.1 倍）。解码核对（`avifdec` 与 `avifgainmaputil tonemap`，满余量）：SDR 基底与源 PNG 平均差 1.55 个码值（最大 39，在文字边缘）；捕获亮度不低于 SDR 白 70% 的像素，HDR 渲染 / 捕获的中位数 0.997–1.001（5–95 分位 0.92–1.07）；比 SDR 基底亮 5% 以上的像素占 25.1%，暗 5% 以上的占 0.01%。
- 素材余量：HDR 效果取决于照片里有多少高光超过 SDR 白，脚本会打印峰值倍数和占比。本次雪山：峰值是 SDR 白的 2.19 倍（约 1003 nits，p99.9 与最大值相同，像是屏幕或系统设置的上限），9.10% 的像素有通道高于 SDR 白，处理后与 SDR 有差别的占 28.33%。对照：灰雁 1.36 倍 / 3.61% / 0.93%，上一张雪山 2.45 倍 / 9.8%，湖景 1.38 倍 / 0.025%。这次 Windows 的 SDR 内容亮度是 481 nits（接近滑块上限）；调低到约 200 nits 再截，余量还能更大，未验证。
- 为什么用增益图而不是 PQ 图：基底按页面的 SDR 白显示，只有屏幕有 HDR 余量的地方才提亮。早先试过不带增益图的 PQ AVIF，在无头 Chromium 中整张图比页面的 SDR 暗约 25–30%，已放弃。两层用同一个文件，是因为增益图 AVIF 本来就含 SDR 基底，用 `dynamic-range-limit: standard` 显示即可，省掉整张 SDR 图；真机探针验证过 `standard` 能把同一个增益图 AVIF 压回 SDR。
- 发布：`.avif` 默认被 `tests/check_public_source.py` 禁止入库（该检查扫描全部历史）；该脚本只给 `site/assets/` 下的 `.avif` 开了例外。`.jxr` 与 Game Bar 的原始文件继续只放在被忽略的 `artifacts/`。

## 宣言节

首屏之后只有一句话，逐字取自 README 的第一句（也是 v0.1.23 发布说明的开头）：“如果你有一块好 HDR 屏幕，别只用它看电影。也用它，重新看看自己的照片。”README 改了这句，这里要一起改。前半句灰色，后半句墨色；后半句第一次滚入视野时有一道蓝紫高光从左扫到右（约 2 秒，只一次），之后是纯墨色。高光是 `background-clip: text` 的渐进增强：浏览器不支持时就是纯墨色文字，系统开启“减少动态效果”时没有动画。这句话兼作“亮点”的引言，所以亮点区不再有自己的大标题（保留一个只给读屏用的 `h2`）。

## “照片信息”卡片：页面里唯一重绘的界面

- 为什么重绘：应用里的面板很长、会滚动，一张窗口截图只拍得到前几节（实况、文件名），拍不到相机、镜头和曝光，卡片“什么都看不到”。换成 HTML 后文字清晰，也不带用户的路径和文件时间。
- 跟应用对齐的地方：分节顺序、标题、三个按钮、特性标签取自 `src/Luma.App/ViewerWindow.xaml` 和 `ViewerWindow.Details.cs`；数字格式取自 `src/Luma.Core/PhotoMetadata.cs`（大小 `7.49 MiB · 7,851,495 字节`，尺寸 `4,080 × 3,072 像素 · 12.5 MP`，日期 `yyyy年M月d日 HH:mm:ss`，焦距、光圈、快门、ISO 用“ · ”连接）。
- 数据：样张 `pixel-6-pro.jpg` 自己的 EXIF（Google Pixel 6 Pro、19 mm、f/3.5、ISO 29，拍摄时间 15:47:34）；内嵌视频的 2.42 MiB / 2,535,338 字节与实机截图里的面板一致（样张文件取自 `05_Pixel_6_Pro.jpg`，XMP 里的视频长度相同）。不含 GPS。两处是示意：快门按 1/371 秒写出，应用里的小数位可能不同；应用会把拍摄时间换算成本机时区，钟点可能不同。省略了“文件名”“尺寸与大小”“文件位置”等行，只留最能说明用途的几节；底部渐隐，表示面板还能向下滚。
- 维护：面板的分节或文案改了，要同步 `index.html` 里的 `.panel`（约 25 行）。这是页面里唯一要跟着应用界面手工改的地方；以后若能截到一张完整的面板（例如把窗口拉高），可以换回截图。

## 实况区

- 文案：标题下的“动态部分，也能用 RTX HDR 预览。”是这一节的重点，出自 README（“动态部分也能选择 RTX HDR 预览”）、[LIVE-PHOTOS.md](LIVE-PHOTOS.md)（“动态 SDR 可以选择 RTX HDR；VSR 在播放期间暂停”）和设置页文字；它在首屏简介、“普通照片”卡片的一行和支持范围表里也各出现一次。不写“独家”“唯一”：LIVE-PHOTOS.md 明确“不声称全球独家”。
- 状态徽标：演示播放时右下角出现“LIVE · RTX HDR 已生效”，文字来自 `ViewerWindow.xaml.cs`（实况播放且 RTX HDR 生效时的状态文字），空闲时隐藏、对读屏隐藏。位置不是应用里的真实位置（应用里在窗口右上角，不在这张裁剪图里），所以它属于示意动画的一部分。
- 右侧是 `live-demo.webp`：从 `live-sdr.png` 裁出的 1040 × 780（起点 x 400、y 67，照片区从 y 67 开始），含完整工具栏。透明的 `<button class="live-btn">` 盖在实况图标上，用虚线框、淡蓝底和扩散环标出，下方是“点这里，试试实况回放”的提示条；点击后图片缓慢放大，约 3.2 秒后自动停止，Esc 也能停。它只是示意动画，不播放视频，状态文字也这样标注。
- 对齐的几何：图标所在的按钮格（66 px）中心在裁剪图的 (60.05 %, 7.76 %)，宽 6.4 %（最小 26 px，满足 WCAG 2.2 的 24 px 目标尺寸）。这些数字写在 `site.css` 的 `.viewer > img { transform-origin }`、`.live-btn` 和 `.viewer__hint`。换窗口截图时，`Prepare-SiteAssets.py` 里的 `LIVE` 裁剪框和这几处百分比都要重新量。

## 内容原则

- 文案只取自 README、`docs/` 和对应 Release 说明，不新增功能、性能或兼容性声明；有出入以这些文档为准。界面里已经能看到的内容（工具栏、图库布局）页面不再赘述。
- 页面里的界面图是 Luma 在 Windows 11 上的实机截图；只有“照片信息”卡片是按实际界面用 HTML 重绘的示例（页脚也这样写明）。截图是 SDR 的；在 HDR 屏幕上看到的对比来自同一帧的 HDR 捕获。界面有明显改动时重新截图。
- 截图里的照片：首屏和分享图用的雪山 `DSC06998.jpg` 是项目维护者自己拍的（2026-10-18 他确认；此前首屏用的灰雁来源不明，已换掉）。灰雁 `pixel-6-pro.jpg`（Pixel 6 Pro 动态照片）仍出现在几个小处：实况演示、图库角标的裁图，以及“照片信息”示例面板的 EXIF（维护者 2026-10-18 决定这些小处暂不改）。它的 EXIF 是 Pixel 6 Pro、2024-06-02（UTC+1），[LIVE-PHOTOS.md](LIVE-PHOTOS.md) 的“样本来源”一节列了 Immich 测试资源里的 Pixel 6 Pro 动态照片，同一文档还写明“公开可下载不等于获准随产品重新分发”；没有核对它是不是那份测试资源里的文件，来源和授权仍未确认。图库页面里还有人物（“夏天”封面里的人、“人物”相册里的角色渲染图），属于维护者准备的展示图库。
- 实况区的按钮只播放一段“示意动画”，不播放视频，状态文字也这样标注。
- 不使用 NVIDIA 或 Microsoft 标识，页脚保留非官方声明。公开前的 NVIDIA 通知与商标使用审查见 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)；主页比 README 更像宣传材料，应一并审查。

## 验证与限制

- 开发环境是 Linux 无头 Chromium：能验证 SDR 回退、对比逻辑（含徽标覆盖层）、布局（1440 / 820 / 390 px，另有 1568 × 810 的首屏）、控制台与请求、axe-core 无障碍、文字对真实渲染背景的对比度、实况示意（含状态徽标）、减少动态效果，以及版本号的几种情形（GitHub API 用 Playwright 拦截成：正常、不可达、被限流、返回仓库之外的地址）；HDR 检测是模拟的（改写 `matchMedia` 与 `CSS.supports`），看不到真实的 HDR 观感。检查脚本在维护者本机，没有入库。
- 2026-10-18 换成雪山整窗截图、去掉版本号后，30 项检查全过：三个宽度的 SDR 与模拟 HDR 均无控制台错误、坏图和横向溢出，对比组件只在 HDR 下建立；一次访问只请求一个 AVIF，徽标覆盖层只在对比建立时请求一次；axe 4 种配置 0 违规；滑块点击 / 键盘 / aria-valuetext 正常，徽标覆盖层与 HDR 层同盒同裁剪；页面源码里没有任何版本号；API 正常时版本、直链、体积和更新说明链接都填上（一次调用，一小时内重载沿用保存的结果），API 不可达 / 被限流 / 返回仓库之外的地址时一律不显示版本、按钮留在 Releases 页；390 px 下显示版本后仍是两行、不溢出；实况、宣言、信息卡片等其余项目同上一版。
- 真机上用被忽略的 `artifacts/hdr-probe/` 确认浏览器行为。2026-10-17 维护者在 Windows HDR 屏上看过第一版 CSS 窗口复刻（雪山）的首屏，HDR 效果和分界线正常；整窗截图版本（含这次的雪山整窗和徽标覆盖层）还没有在真机上看过。Safari、Firefox 和系统字体的实际渲染没有测。

## 版本号

页面自己不写版本号（上一版写死了 0.1.27，而最新发布已是 0.1.28），也就不需要每次发版时更新页面。`site.js` 在页面加载完成后向 `https://api.github.com/repos/lueluelue12138/luma-hdr-photo-viewer/releases/latest` 要最新的正式发布（不含预发布与草稿），读到后填上：

- 首屏小字前面的“当前正式版 x.y.z”；
- 两个下载按钮改为直达安装包 / 便携版 ZIP（按文件名匹配 `windows-x64…setup.exe`、`windows-x64…portable.zip`），并显示体积；
- “x.y.z 更新说明”链接。

读不到（离线、被墙、被限流、返回了意外内容）就什么都不写：版本号不出现，两个下载按钮都指向 `…/releases/latest`，进去后在 Assets 里选，“更新说明”也指向那里。返回的地址必须以本仓库的 Releases 地址开头才会采用。结果存在 `localStorage`（键 `luma-latest-release`）一小时，所以同一访客每小时最多问一次；匿名请求的限额是每个 IP 每小时 60 次，被限流时按“读不到”处理。失败不缓存，下次打开再试。这是页面唯一的第三方请求（只取公开的发布信息，不带 Cookie），已写在 `site.js` 开头的注释里；不想请求 GitHub 的话，删掉 `site.js` 里“Latest release”一段即可，页面仍然完整。

附件改名（不再含 `windows-x64` 与 `setup.exe` / `portable.zip`）时，直链匹配不到，按钮会留在 Releases 页，需要改 `site.js` 里的两个正则。

## 部署

2026-10-19 已通过仓库 Pages API 启用 `build_type=workflow`，手动运行现有工作流成功（Actions `39153789167`，源码 `52ed9ac`）。实际地址为 https://lueluelue12138.github.io/luma-hdr-photo-viewer/ 。Windows 本机浏览器已打开并核对首屏与实机图片；上线截图和远端状态保存在忽略目录 `artifacts/pages-20261019/`。旧的失败运行没有被当作成功记录，工作流本身无需改写；继续由 `site/**` / 工作流文件进入 main 或手动触发进行部署。

- **GitHub Pages**（`.github/workflows/pages.yml`）：`site/` 变化并进入 `main`，或手动运行时发布。一次性设置：仓库 Settings → Pages → Build and deployment → Source 选 **GitHub Actions**。没设时 workflow 会在 `configure-pages` 一步失败：该 Action 的 `enablement` 默认关闭，替仓库开启 Pages 需要比 `GITHUB_TOKEN` 权限更高的令牌（读自 v6.0.0 的 `action.yml`），所以这一步留给维护者点一下。四个官方 Action 都固定到提交 SHA（与 `ci.yml` 一致，`checkout` 同 v7.0.1，其余 `configure-pages` v6.0.0、`upload-pages-artifact` v5.0.0、`deploy-pages` v5.0.1，SHA 已用 `git ls-remote` 对照标签核实）。最初制作时仅做了 YAML 解析；2026-10-19 已完成启用和实际 Actions 部署，结果见上文。页面里的 `canonical`、`og:url`、`og:image` 按 `https://lueluelue12138.github.io/luma-hdr-photo-viewer/` 填写，上线后需核对；换自定义域名则一并改掉。
- **国内访问**：GitHub Pages 在中国大陆可以打开，但第三方测试（2024 年，博客园《各前端部署平台详细体验汇总》）报告它不稳定、常断连和慢；Cloudflare Pages 回源到美国，国内延迟很高；Vercel 的默认域名会被屏蔽，换自定义域名才好用。腾讯云 EdgeOne Makers（原 EdgeOne Pages）有永久免费版（官方 2026-02 正式版公告和定价页），支持直接导入 GitHub 仓库：纯静态，构建命令留空、输出目录填 `site`，加速区域选“全球可用区（不含中国大陆）”时不要求备案，默认域名可用；要用中国大陆节点，自定义域名必须先 ICP 备案（腾讯云文档）。这些是文档与第三方测试的结论，没有从维护者的网络实测，EdgeOne 的账号和授权也只能维护者本人操作。两处部署可以并存，页面本身不需要改。
