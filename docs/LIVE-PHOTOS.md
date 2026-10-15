# Live Photos / 动态照片与 RTX HDR 可行性调研

调研日期：2026-10-15，基于 Luma 0.1.23 源码、平台规范与已有 NVIDIA RTX Video SDK 1.1.0 接口。**这是技术方案，当前应用尚未实现 Live 播放，也未对动态片段做 RTX HDR 或帧率实测。** 商店账户注册延后，不影响此项研发。

## 结论

主流动态照片预览可做；动态部分通常是短视频，可以研究复用 RTX Video HDR 进行实时增强。基础播放属于中等工作量，做成当前看图体验中的低延迟、正确色彩、完整音画同步与 HDR 无缝切换，属于较大的一轮功能开发。它需要新增视频管线，不能只增加几个图片扩展名。

建议定位：**让回忆动起来，也让普通实况照片用上 HDR 屏幕。** 技术上有依据，但现阶段不宣传已经支持，更不声称“全球独家”。

## 先支持什么

| 类型 | 需要识别的结构 | 建议范围 / 风险 |
| --- | --- | --- |
| iPhone Live Photos | 原始 HEIC / JPEG 与配套 MOV；图片和视频元数据中的关联标识、静态帧时点 | 第一优先。图片仍走现有查看路径；从完整导出中配对资源。不能只靠文件名相同就认定配对成功；只有照片而没有 MOV 时，无法恢复原始动态 |
| Android 标准 Motion Photo | 图片中的 XMP 容器目录指向内嵌 MP4 / MOV；现代规范支持 JPEG，也定义 HEIC / AVIF 容器 | 第一优先 JPEG。Ultra HDR 增益图与视频可能同时存在，必须正确区分位置；AVIF 不因增加 Live 支持就自动成为 Luma 已支持格式 |
| Samsung Motion Photo | 新格式与标准容器、旧 Samsung trailer / SEF 变体 | 以实际样本分支支持，不能承诺一个通用偏移算法兼容全部代际 |
| 旧 Google MicroVideo | 旧 XMP 偏移 / 长度 | 作为独立兼容分支。现代 Motion Photo 1.0 明确弃用这些字段，不能用旧偏移覆盖现代容器描述 |
| 小米 / OPPO / vivo / 荣耀等 | 厂商、系统版本、分享 / 导出方式可能改变封装 | 收集原文件逐项验证；不能以“都是安卓”推定完全相同。导出后已丢失视频的静态 JPG 只能显示静态 |
| `.livp` 导出封装 | 某些工具的图片 + 视频打包形式 | 次优先，先验证实际 ZIP / 项目内容与配对，不把它当成 iPhone 相册底层唯一格式；限制展开预算并阻止路径越界 |

Apple 官方说明 Live Photo 由静态图与短片组成，通过 content identifier 关联，并有表示静态照片时点的元数据。视频可能使用不同编码，不能只按 MOV 后缀判定 HEVC。参考 [Apple Live Photo 元数据](https://developer.apple.com/documentation/avfoundation/avcapturephotosettings/livephotomoviemetadata)、[Apple WWDC16 结构与播放说明](https://developer.apple.com/videos/play/wwdc2016/501/)、[Apple 视频编码设置](https://developer.apple.com/documentation/avfoundation/avcapturephotosettings/livephotovideocodectype)。

Android 的 [Motion Photo 1.0 规范](https://developer.android.com/media/platform/motion-photo-format) 给出了容器、增益图和视频布局。实现时需校验真实视频边界、长度及元数据，而不是仅凭 MotionPhoto 标志；JPEG 与 HEIC 的封装也不能共用简单“寻找 JPEG 结尾”算法。厂商兼容可参考 [ExifTool 原作者维护的 Samsung 解析器](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/Samsung.pm)，其行为是兼容线索，不等同于所有厂商的官方格式保证。

## Live 与 HDR 是两个维度

**Live 表示有动态内容，HDR 表示亮度 / 色彩编码；二者不能互相推导。** 对封面照片和视频分别识别：

| 静态部分 | 视频部分 | 正确策略 |
| --- | --- | --- |
| SDR | SDR | 静态图可使用 RTX HDR；动态片段逐帧使用 RTX HDR 的新路径 |
| 原生 HDR | SDR | 静态图保留原生 HDR；动态片段可使用 RTX HDR，重点验收由静入动时亮度 / 颜色不突变 |
| SDR 或原生 HDR | 原生 HDR（PQ / HLG 等） | 视频需要独立的原生 HDR 解码、色彩与显示映射；不能先压成 SDR，再把 RTX 增强当原生还原 |
| 任意 | 编码器缺失 / 视频损坏 / 资源缺失 | 静态封面仍可查看，明确解释动态部分不可用，不能把整张照片判为打不开 |

Android 规范允许视频本身是 SDR 或 PQ / HLG HDR，因此单个增益图不能直接套用到所有视频帧。Apple / Android 的 HDR 视频、Dolby Vision 及特殊元数据要按实际编码与可用基础层分别验证；首轮不承诺完整 Dolby Vision 动态元数据还原。

## RTX HDR 接入路线

[NVIDIA 官方 RTX Video SDK](https://developer.nvidia.com/rtx-video-sdk) 原本面向视频增强，包含实时 SDR→HDR。Luma 已有 D3D11、TrueHDR 与 FP16 / scRGB 呈现，因此方向合理，无需为了 Live 再引入另一套 NVIDIA SDK。**现有静态图片增强成功，不能证明动态播放已经流畅或正确。**

建议管线：

```text
照片资源探测 / 图片与视频配对
          ↓（静态封面先显示，视频按需打开）
Windows Media Foundation 解封装与视频解码
          ↓
GPU 色彩转换 + 正确的旋转 / 裁切 / 时间戳
          ├─ SDR → 现有 TrueHDR SDK 的视频接入 → FP16
          └─ 原生 HDR → 单独的 PQ / HLG 等映射 → FP16
          ↓
复用 Luma 的 scRGB 呈现、缩放、工具栏和主题
```

优先评估 Windows Media Foundation 与 D3D11 解码，避免首轮捆绑整套 FFmpeg / 外部视频播放器。[微软支持的容器](https://learn.microsoft.com/windows/win32/medfound/supported-media-formats-in-media-foundation) 包括 MOV / MP4，[Source Reader 可关联 D3D 管理器](https://learn.microsoft.com/windows/win32/medfound/mf-source-reader-d3d-manager)。这仍不保证每个 MOV 变体、编码 / Profile 都可解码；HEVC / AV1 可用性须依实际系统扩展和硬件验证，不能承诺完全没有额外组件。

不能把视频简单覆盖到独立 `MediaPlayerElement` 后，就认定视频已走 Luma 的 TrueHDR / FP16 路径。最终 HDR 增强路径要能取得解码帧并送入 SDK，检查实际调用和高范围输出。音频需要独立调度与同步；不得用 UI 线程计时器当视频时钟。

## 现有代码能复用多少

- `src/Luma.Native/Luma.Native.cpp`：已有 TrueHDR 的 D3D11 输入、FP16 输出、显示器 / 白色 / 峰值、视图和玻璃渲染，可复用基础设施。
- 但 `Renderer::use(Frame&)` 现在从 CPU 像素上传，生成 mip，重置视图、原图 / 增强缓存和玻璃场景；这是“切换照片”接口，不能直接拿来逐帧播放。
- `src/Luma.App/NativeRenderer.cs` 现在适合后台执行离散看图任务；逐帧 `Task.Run`、读状态、回 UI 或每帧等待 GPU，都可能引入排队 / 卡顿，需要独立的持续播放提交机制。
- 现有静态原图 / HDR 对照依赖缓存两份结果。视频对照也应让时间轴持续前进，选择同一时间点的原始 / 增强帧，不能每次切换都重开解码器或重跑整段视频。
- 现有 `.heic/.jpg` 入口可以沿用；新增识别不应把所有 MOV / MP4 都混入照片列表。普通视频播放器、转换和导出不作为首轮目标。

## 性能必须先守住

1. 相册仍只加载静态缩略图；不在导入 3000 张照片时启动成千上百个解码器，不为检测 Live 全盘反复扫描。
2. 静态图先出现；仅当前预览窗口按需准备短片。初期只允许一个窗口主动播放，切图 / 关闭 / 最小化立即停止或取消过时任务。
3. 优先同 GPU 设备上的硬件解码与纹理传递，避免 GPU→CPU→GPU 来回拷贝。复用有限纹理和 SDK 会话，限制队列长度；GPU 忙时优先保持交互响应，不能无限积压旧帧。
4. 不缓存整段解码后的 FP16 视频。**容量估算**：1920×1080、RGBA FP16 每帧约 15.82 MiB；3 秒 × 30 fps 的 90 帧，光输出就约 1.39 GiB，尚未计入解码、SDK、玻璃和原始视频资源。使用有限帧缓冲与时间窗口，不把此算式当实际显存测量。
5. 30 fps / 60 fps 对应每帧总预算约 33.3 / 16.7 ms，只是目标约束，不是已达成结果。静态图片历史处理耗时不能直接推算持续播放帧率；需统计 GPU 时间、调度、丢帧、音画偏差、首帧和长时间循环资源。
6. 动态背景下的 Gaussian 玻璃会增加逐帧工作量；应单独测量，避免沿用静态缓存导致播放时模糊背景停住，或每帧全分辨率重新模糊拖慢播放。

## 交互建议

显示 LIVE 标记与独立播放按钮；按住标记播放、松开回到静态主照片，另提供可重复播放的明确入口。默认不在图库 / 鼠标悬停时自动播放，声音默认静音并提供显式控制。视频与静态封面的不同裁切、方向、分辨率和色彩是过渡验收重点，不能靠叠加任意淡入淡出掩盖。

**空格保持当前最高优先级的 HDR / 原图对照，不借给播放 / 暂停。** 点击图片显示 / 隐藏工具栏、拖拽平移等既有操作也要保留。播放快捷键可使用独立按键，例如 `L`，实施前统一说明。

## 分阶段落地

| 阶段 | 范围 | 完成证据 |
| --- | --- | --- |
| A：资源与解码原型 | iPhone 完整 HEIC/JPEG+MOV、标准 Android JPEG Motion Photo；有界读取、严格配对、孤立资源回退；解码少量真实片段 | 样本来源 / hash / 元数据、视频范围与静态封面保持不变；缺失 / 损坏 / 同名误配均覆盖 |
| B：基础 Live 预览 | 当前窗口按需播放、方向 / 裁切 / 静态回归、音频控制与同步、连续快速切图取消 | 真实窗口与录屏，首帧 / 帧间隔 / 音画同步 / CPU/GPU/RAM/显存；3000 张普通相册回归不退化 |
| C：SDR Live + RTX HDR | 固定帧缓冲 / SDK 复用、SDR 逐帧增强、空格同时间轴对照、跨模式平滑过渡 | 实际 SDK 调用、FP16 读回、动态测试片段逐帧正确性、持续播放与循环丢帧 / 资源稳定性 |
| D：扩展兼容与原生 HDR 视频 | PQ / HLG、更多品牌和旧封装、`.livp`、缺少扩展提示与降级 | 真机与导出样本矩阵；每个兼容分支实测后才列入支持清单 |

B 可用于验证播放体验，但不等同于 C 的 RTX HDR 成功。封面已是 HDR 的视频也要单独分类；开发中遇到原生 HDR 片段时，应明确当前回退行为，不能悄悄当 SDR 走 TrueHDR。

C 首先只开启 TrueHDR，VSR 继续默认关闭；是否同时做视频超分，待单独的帧率与显存评估，不把它隐式绑到 Live 播放。

## 是否独有

在本次定向检索中，已经看到 [JarkViewer](https://github.com/jark006/JarkViewer) 声明支持 Live / Motion Photos，[HDRImageViewer](https://github.com/linyusenzz/HDRImageViewer) 声明支持 HDR 静态封面与原生媒体层播放，并计划继续接入 HDR 视频 renderer。以上是对项目作者文档的核对，没有在本机安装验收竞品。

因此“支持 Live 图”或“同时能看 HDR 图”本身不是已证明的独家；**跨品牌 Live 图的动态片段 RTX HDR、快速切换和美观的一体体验**可以成为 Luma 的差异点。本轮没有足够证据证明没有其他实现，不使用“全球首个 / 唯一”宣传。

## 本轮验证边界与后续样本

已核对平台规范、竞品原始文档、SDK 头文件和 Luma 现有路径；没有把第三方源码 / 测试照片引入仓库，没有新加运行时依赖，没有执行 Live 解码 / 播放 / RTX HDR 实验，不给出实测帧率或安装包增量。

实施前准备：完整 iPhone 配对资源、标准 / 旧 Google Motion Photo、Samsung 与至少两家其他 Android 真机原文件，以及 SDR / 原生 HDR / Ultra HDR 封面组合。另需错误关联、重命名、缺失 MOV、已剥离视频、损坏偏移、纵向旋转、无音轨 / 带音轨、慢磁盘和缺少编码器的样本。文件只读测试、记录来源及 hash，私人照片不进 Git 或消费者包；公开样本的可下载性和许可证需在实际下载时再核对。
