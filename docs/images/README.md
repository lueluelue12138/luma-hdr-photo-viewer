# Luma 产品展示图

`luma-product-showcase.png` 用于仓库首页 README。使用内置 image_gen 工具生成、再通过同一工具校准，未使用 CLI/API 回退。

- 用途：高级横屏产品展示；相册窗口与照片预览窗口，深色 Fluent 材质，独立 HDR / VSR 控制。
- 原生输出：1672 × 941 PNG，约 16:9，2238450 字节。保留工具原生输出，没有通过代码拉伸照片或修改图像。
- 界面参考：`src/Luma.App/MainWindow.xaml`、`ViewerWindow.xaml` 和 `App.xaml` 的实际布局、控件次序与深色材质。
- 品牌参考：仓库 `src/Luma.App/Assets/Logo256.png`。生成后的第一版与该 logo 作为第二次图像工具的输入，修正品牌符号和缩略图滚动结构。
- 所有风景、缩略图、相册名和数量为虚构；没有读入、复制或上传本地相册照片，也没有将用户的浏览器截图作为图像工具输入。
- 这是 AI 生成的产品示意，细节以实际版本为准。开关和状态为展示场景，不能作为 SDK、HDR/VSR 生效或实际屏幕亮度的验证证据；普通 PNG 也不能重现真实 HDR 屏幕观感。实际功能证据另见 [验证记录](../VERIFICATION.md)。
- 检查：两轮生成结果已目视检查，PNG 完整解码、尺寸与文件引用通过，README 明确标注产品示意。图库照片与预览照片均保持清晰，浮层边界、文字、选中缩略图和独立开关可辨。
- 保留原生输出，不覆盖工具保存的原稿；项目内文件受仓库 MIT 许可覆盖。

## 初始生成提示词

```text
Use case: ads-marketing / product showcase.
Create one finished, exceptionally polished promotional hero image for the GitHub homepage of Luma HDR Photo Viewer, a REAL Windows 11 native WinUI 3 desktop photo application. Landscape 16:9 canvas, ideally 3840 x 2160. This is a software product presentation, not a concept redesign, not a website screenshot, not hardware or a laptop mockup.

Art direction: premium and calm, restrained dark charcoal / graphite studio background, very soft cyan-blue ambient glow echoing the Luma icon, faint warm reflected light from the landscape, fine natural gradients. Two elegant front-facing overlapping Windows desktop app windows, with realistic soft shadows and delicate 8-12 px corner radii. Crisp and precise interface; nearly orthographic, no tilted perspective, no thick frames, no noisy futuristic decoration. Generous outer margins, balanced composition; the viewer is the unmistakable visual hero. The UI windows fill most of the lower 80% of the canvas. Small unobtrusive top-left branding: a blue rounded-square gradient icon with a simple WHITE mountain ridge line, followed by large clean text "Luma", secondary text exactly "HDR Photo Viewer". One understated line of copy exactly "Rediscover your photos." and small capability text exactly "RTX HDR · Windows 11". Use refined Segoe UI-like typography, excellent hierarchy, very little marketing copy.

Faithfully reproduce these actual app characteristics:
BACK LEFT WINDOW - album library, dark theme. Thin native title bar: small blue mountain icon, "Luma", center compact search "搜索照片或相册", native minimize/maximize/close buttons at right. Left graphite sidebar around 20% width with thin line icons and labels "所有照片", "相册" (selected), "收藏", "最近打开", bottom "设置". Main area title "相册", small text "6 个相册", an understated "导入文件夹" rounded button. Six airy album cover cards in a three-column two-row grid, each PHOTO COVER ABOVE label and count; covers dominate. Fictional generic titles "山野", "海岸", "城市", "森林", "沙漠", "光影". Photo cards have modest rounded corners, text uncut, no heavy nested borders. No large footer.
FRONT RIGHT WINDOW - image viewer, larger and on top, dark native title bar with back arrow, filename "Alpine light.jpg", and normal Windows controls. Beautiful entirely NEW photorealistic original landscape photo: sharp dramatic alpine ridge, golden sunlight after a storm, sparkling bright sunlit cloud edges, pristine lake reflections, dark textured evergreen trees. Rich but believable photographic detail, gentle warm/cool contrast; NOT blown-out white and NOT garish saturation. This landscape MUST be newly invented, not derived from any user's image. Preserve the photograph's original aspect ratio, fit the entire photo naturally, with unobtrusive neutral margins if needed. Photograph is sharp and opaque.
Viewer TOP: a compact FLOATING rounded neutral charcoal frosted-glass panel, centered above the image, containing fine white line icons in the actual order rotate, heart, divider, zoom out, zoom in, a native dropdown "适合窗口", fullscreen, SUN enhancement icon, more. The toolbar is a small translucent panel, NOT a full-width opaque bar.
Viewer BOTTOM: a FLOATING frosted-glass thumbnail strip, with clear spacing at BOTH sides and below, six newly generated fictional travel thumbnails, a clear cyan outline wrapping the active thumbnail, subtle horizontal scrollbar, counter "1 / 6". Native normal horizontal photo browsing; no solid black bar.
A SMALL frosted-glass enhancement flyout near the upper-right, with excellent readability, title "显示增强", two independent aligned switches: "RTX HDR" ON in blue, "VSR" OFF in neutral gray. A tiny photo-overlay status pill "RTX HDR". This flyout must be discreet, not an enormous dialog. Frosted glass should show gentle gaussian-blurred actual photo color beneath, neutral tint, readable white text, refined hairline edge and soft shadow. No colored blue panel background unrelated to photo. All native UI spacing consistent.

All gallery and viewer imagery should be ENTIRELY NEW generated generic landscapes: alpine lake, ocean, nighttime city, woodland, desert; no people, no personal photos, no filenames, album names, portraits, exact compositions or imagery from the user's local albums. The alpine hero photo and its selected thumbnail should be consistent.
This is an AI-created promotional interface illustration, not a measured before/after HDR comparison. Do not add SDR/HDR split-screen, brightness comparison, benchmarks, fake download/version claims, "4K" badges, unsupported format claims, Microsoft/NVIDIA logos or endorsements, watermarks, browser chrome, website UI, desktop taskbar, phones or laptops.
The overall effect should feel like a premium real Windows app showcase whose beauty comes from the photographs and calm Fluent UI materials. Finely rendered, exceptionally coherent and attractive, professional product launch imagery, landscape 16:9.
```

## 定向校准提示词

```text
Use case: compositing / precise-object-edit.
Image 1 is the promotional software product-showcase EDIT TARGET. Image 2 is the EXACT official Luma LOGO, a supporting insert ONLY. Do not use Image 2 to change anything else.
Preserve the composition, both Windows app windows, all newly generated landscape photographs, photographic clarity, premium dark background, subtle lighting, native typography, spacing, glass controls, all existing main text, and the overall visual quality of Image 1. Make these narrowly scoped corrections:
1. Replace the LARGE upper-left blue icon with the EXACT official icon from Image 2: same blue/cyan gradient, single white ascending mountain ridge line, NOT an M-shaped mountain symbol; scale and place the actual supporting icon without redesigning or interpreting it. Keep its transparent edge clean. Also use the exact same small icon in the rear library window title bar.
2. In the BOTTOM floating thumbnail strip, REMOVE the left/right chevrons. Real Luma uses a continuous horizontally scrollable list with a slim scrollbar. Keep all six existing newly generated thumbnails and the cyan selection outline. Place counter "1 / 6" unobtrusively at the RIGHT end in its own small reserved area, not below the middle.
3. Exact landscape 16:9 export, preferably exactly 1920 x 1080 pixels or 3840 x 2160. Use minute background-only edge extension if needed to get the exact ratio; never cut off windows or text, never stretch or warp the UI or photo. Preserve the original balanced framing.
Do not add new features, decorative icons, text, comparisons, user photos, people, hardware or logos. The image is still an illustrative premium product presentation, not an actual screen capture or measured HDR comparison.
```
