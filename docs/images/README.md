# Luma 产品展示图

`luma-product-showcase.webp` 用于仓库首页 README。2026-10-14，维护者选定浅色海岸双窗口方案，替换初版深色展示图。

- 来源：内置 image_gen 生成的方案 A，经同一工具按用户提供的透明 Luma logo、蓝白渐变背景和文字层级定向调整；选定的图像与 A 精修原稿 SHA256 一致。
- 用途：浅色 Windows 产品展示；相册窗口与照片预览窗口、悬浮玻璃控件及两个独立的 HDR / VSR 开关。
- 尺寸：1672 × 941，约 16:9。压缩没有缩放、裁切、调色、锐化或重新绘制。
- 照片、相册名称及数量为 AI 生成的虚构示例。最终选定图未包含维护者的私人相册或后续提供的真实照片截图。
- 这是产品示意，界面细节以实际版本为准；不是实际 SDK、HDR/VSR 生效或屏幕亮度的验证证据。实际验证见 [验证记录](../VERIFICATION.md)。
- 原始 PNG 与未采用的其他候选仅保留在忽略的本地 `artifacts/promotional-previews/`，不作为额外大文件提交。项目内展示资产受仓库 MIT 许可覆盖。

## 无损压缩与校验

| 文件 | 字节 | MiB | 处理 |
| --- | ---: | ---: | --- |
| 选定原始 PNG | 2320056 | 2.213 | 原稿保留在本地 |
| 优化 PNG 候选 | 2025373 | 1.932 | 无损，未提交 |
| 当前 WebP 资产 | 1556588 | 1.484 | 无损，减少 32.91% |
| 旧深色 PNG | 2238450 | 2.135 | 当时保留原生输出，没有额外压缩；既有 Git 历史不变 |

编码使用 Pillow 11.3.0 / libwebp 1.5.0，`lossless=True, quality=100, method=6, exact=True`。`quality=100` 在此调节无损编码投入，格式由 `lossless=True` 明确决定。PNG 与 WebP 都完整解码成 RGBA，逐像素、尺寸核对通过，原文件没有改变。README 引用核对与最终图像目视检查通过。

源 PNG SHA256：`9ecbd3e063c18776d178df8a3f03dd3b2129b1935d22d48d2bf26c7bc6493a59`。

当前 WebP SHA256：`633ec34e4b2121a158b86d915af0db5ca349c869e2392747d41749d231ee51b5`。

机器校验记录见 [luma-product-showcase.validation.json](luma-product-showcase.validation.json)。旧图从当前版本移除不会清除旧提交中的对象；本次没有改写已推送历史。

## 初始方案 A 提示词

```text
Use case: ads-marketing / realistic software product presentation.
Create a NEW Luma HDR Photo Viewer product showcase for a GitHub README, wide landscape 16:9. LIGHT theme. A restrained editorial layout with the visual clarity of a REAL Windows 11 desktop screenshot on a simple light background, not concept art, not a 3D render. Faithful native WinUI 3 window geometry, crisp thin line icons, normal Segoe UI typography, small 8 px corner radii, restrained shadows, thin gray dividers. Precise, flat, front-on, believable operating-system UI. No tilted perspective, levitation, glossy plastic, giant glows, cinematic framing or blue haze. Comfortable bright off-white background, subtle studio softness ONLY outside windows. UI chrome is neutral white/light gray, dark gray text with excellent contrast. The scene should feel quietly designed, mature, approachable, and photographic.
Brand copy outside windows: just "Luma" and small "HDR Photo Viewer". Optional single existing slogan "Rediscover your photos." Do not invent more marketing copy. Small app mark: blue/cyan gradient rounded square with a SINGLE continuous white mountain ridge line, ascending from lower left to one peak, down into a valley, then rising to the right. Not the letter M or multiple tiny mountains.

Real app UI:
Library window has a thin normal Windows titlebar "Luma", native minimize/maximize/close, a compact search field "搜索照片或相册". Left sidebar has thin line icons and labels "所有照片", "相册" (selected with soft pale blue selection), "收藏", "最近打开", bottom "设置". Main area has "相册", "6 个相册", a small rounded "导入文件夹" button and 3-by-2 cover grid. Simple modestly rounded photo cards, photo above a short album label and count, generous spacing. Use generic fictional names such as "海岸", "山野", "城市", "森林", "日常", "旅行". No file paths or personal album names, no large footer.
Viewer window has a thin light native titlebar with back arrow, a short generic filename, normal Windows controls. It shows the entire landscape image at the ORIGINAL proportion, sharp and opaque; no forced crop to fill. Compact floating translucent FROSTED WHITE neutral glass toolbar at top center: rotate, heart, divider, zoom out, zoom in, dropdown "适合窗口", fullscreen, SUN enhancement icon, more. Use natural low-key native blur with subtle gray border, readable charcoal controls. The toolbar is small, not a full-width bar.
At bottom, a floating frosted white thumbnail strip with modest rounded corners, margins on both sides and below; six coherent travel photos, clear thin blue selection outline, tiny scrollbar and "1 / 6" at RIGHT. No arrows inside the thumbnail strip. Optional discreet small enhancement flyout with "显示增强", independent switches "RTX HDR" ON blue and "VSR" OFF gray, consistent with the app. Do not let this flyout dominate the image.
All photos must be NEW original realistic photographs invented for this illustration, never use any user album, existing uploaded landscape or portrait. Absolutely avoid the default AI-landscape look: NO perfect triangular mountain centered over a perfectly mirrored lake, NO impossible sunbeams, luminous clouds, heavy orange/teal grading, excessive vibrance, crunchy sharpness, velvet hills, polished water, fantastical geography, ideal symmetry, painted detail or HDR-art look. Aim for ordinary but beautifully observed travel photography, captured with a real midrange camera, slightly imperfect natural composition, gentle sensor grain, variable focus depth, restrained exposure and believable textures. Do NOT try to depict HDR by over-brightening a photograph. The imagery should look like thoughtful human photographs in a photo viewer, not generated wallpaper.
No people or faces, private information, personal files, hardware devices, monitors, laptops, browser chrome, dark backdrop, lens flare, floating geometric shapes, sparkles, fake measured SDR/HDR comparisons, performance badges, endorsements or watermarks. This is an illustrative product scene, not proof of SDK output. All interface content and every window remains inside the frame.
Art direction A: Pure clean white and very pale pearl-gray background, neutral daylight. A small library window behind on the left, and a larger viewer window slightly overlapping on the right, both flat front-on, very subtle real OS shadows, generous outer margins. Main viewer image is a believable quiet coastline photographed from an ordinary footpath: weathered limestone shelves and pebbles in foreground, a softly curved shoreline placed off-center, muted clear turquoise-gray sea, tiny modest waves, distant headland in hazy midday air, pale blue sky with a few unremarkable soft clouds. Diffuse late-morning light, NOT a sunrise or sunset. Natural uneven rock texture, a few dry grasses, realistic spray, moderate contrast, no giant cliffs or perfectly sculpted shapes. The hero photograph feels like a good unedited coastal travel JPEG, not a stock CGI masterpiece. Other album covers: ordinary coast, woodland path, quiet cafe exterior, village rooftops, gray mountain slope, simple beach. The entire product scene is bright, simple and understated. An unobtrusive enhancement flyout may be visible.
```

## 定向调整提示词

```text
Use case: compositing / precise visual refinement.
Create a thoughtfully refined light-theme 16:9 Luma software product-showcase image by editing Image 1 (the preferred option A). Image 2 is the ACTUAL official Luma transparent PNG logo supplied by its owner. Image 1 is the edit target; Image 2 is a supporting brand asset to insert, not an inspiration to reinterpret.
Preserve the natural, believable coastal travel photograph in Image 1, all six fictional photo thumbnails, both front-facing native Windows app windows, clear screen text and sharp controls. Preserve the clean light theme, honest photographic color and restrained OS styling. Do not reinvent the landscape or make it cinematic. No local-user photographs may be used. The photograph should remain the primary product focus.

Make this a refined, coherent graphic-design composition with these changes:
BACKGROUND — Replace the pure white outside the windows with a beautifully restrained pale blue-to-pearl / pale lavender gradient. Upper left is cool powder blue around #C9DCF0, smoothly flowing across the canvas into very light bluish pearl around #E7EDF5; lower-right has a faint desaturated periwinkle around #D7E1EF. Keep the tone bright but visibly darker than the WHITE window surfaces so every edge is clearly distinguishable, including the library's left and bottom edges. No white-on-white disappearing boundaries. Use one broad, smooth, soft tonal transition, no decorative blobs, no patterned waves, no neon halo, no glossy stage, no spotlight or heavy vignette. A faint natural ambient shadow and very thin neutral gray OS window border are enough. Outer background has no artificial 3D textures.
BRAND LOGO — Remove the incorrect wave-like logo in Image 1 and insert the ACTUAL Image 2 logo unchanged. Match its real rounded-square silhouette, cyan-to-blue gradient, and SINGLE WHITE MOUNTAIN RIDGE LINE: ascending from low on the left edge toward a rounded central-left peak, descending to a broad valley at center-right, then rising diagonally toward the upper-right end. EXACT asset, NOT a sine wave, NOT a letter M, NOT a generic mountains pictogram. Preserve its transparency, no black square from the preview background, no redrawing, no logo redesign. Place this official logo in the upper-left branding block at a comfortable medium size, and the same mark at the small size in the library titlebar.
TYPOGRAPHY — Recompose the header as ONE well-aligned, balanced brand lockup in the available upper-left area, with clean vertical rhythm and comfortable space before the rear window. Official icon on the left, wordmark to its right. Primary text exactly "Luma" in confident near-black Segoe UI-like semibold, moderately large, not oversized. Immediately below, secondary text exactly "HDR Photo Viewer" in a smaller medium-weight cool slate gray. Below that, one short Chinese value line exactly "让普通照片，也有 HDR 的光影。" in a smaller but readable neutral slate tone. All three text lines left-aligned as a coherent block, never competing with the app windows or spreading unrelated copy across the canvas. Remove the old vertical separator and old "Rediscover your photos." text from Image 1. You MAY put a tiny quiet informational label exactly "Windows 11 · RTX HDR" above the right viewer window, right-aligned, ONLY if enough breathing room; otherwise omit it. No additional headlines, badges or paragraphs. Precise spelling, realistic crisp type.
WINDOWS AND GLASS — Maintain the established slight overlap between the smaller library window at left and larger photo viewer at right. Leave a few percent safe margin on all sides. Do not let either window or brand block overlap unreadably. Subtle native shadows and fine neutral border must give edge separation without making the software float theatrically. Toolbar and filmstrip stay compact white frosted glass: understated gaussian blur of photo content beneath, legible charcoal line icons, hairline edge. Keep the small enhancement flyout with two INDEPENDENT switches: "RTX HDR" on, "VSR" off. Keep selected thumbnail outline and subtle scrollbar; NO left/right chevrons inside the filmstrip. The landscape photo remains fully sharp and opaque.
Make the overall result feel like an understated, professionally art-directed Windows product illustration, with deliberately composed typography, exact real branding and calm light-blue gradient separation. Less AI styling, more credible real application presentation. No perspective tilt, no exaggerated reflections, no fake sunbeams, no dramatic HDR grading, no people, no hardware, no watermarks or endorsements. Preserve landscape 16:9 framing.
```
