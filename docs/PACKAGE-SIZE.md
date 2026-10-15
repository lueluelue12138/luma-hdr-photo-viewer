# 首次公开发布前的包体积审计

2026-10-15，源码版本 0.1.23，Windows 11 x64，完整 RTX 版。维护者补充要求“适度精简，主要减小安装包”，因此采用无损压缩与主程序发布符号排除，保留完整离线 .NET / WinUI 运行库、语言资源、原生 HDR 与 NVIDIA HDR / VSR。没有切换为依赖用户另装 .NET 的轻量包，也没有发布新标签或 Release。

## 实测结果

以下使用同一轮源码、许可与完整功能构建比较。MiB = 1,048,576 字节，Windows 常将这个单位标成“MB”。程序目录不含用户图库 / 缓存；真正安装后另有少量卸载程序文件。

| 项目 | 优化前 | 优化后 | 减少 |
| --- | ---: | ---: | ---: |
| EXE 安装包 | 63,936,602 字节 / 60.97 MiB | 61,090,486 字节 / 58.26 MiB | 2.71 MiB，4.45% |
| 便携 ZIP | 88,275,457 字节 / 84.19 MiB | 85,668,757 字节 / 81.70 MiB | 2.49 MiB，2.95% |
| 完整程序目录 | 203,502,107 字节 / 194.07 MiB，504 文件 | 203,347,466 字节 / 193.93 MiB，503 文件 | 约 0.15 MiB |

这是提交前本地候选的测量，`build-info.json` 如实记录 `sourceDirty=true`。提交后正式出包应使用干净源码重新构建；文档、提交号和编译元数据会造成少量字节变化，以上数字不是对未来每次构建的精确大小承诺。当前不把本地候选自动上传或作为正式公开发行。

之前“80 多 M”主要对应 **便携 ZIP**，不是 EXE 安装包。0.1.17 的本地完整 RTX 包为 ZIP 83.75 MiB、安装器 60.67 MiB、程序目录约 192.93 MiB。0.1.23 优化前目录只增加约 1.15 MiB，包括原生 HDR、照片详情及其他界面迭代，不能将整个差额都归因于解码器。

## 什么占空间

按最终候选逐文件分类，分类之和等于程序目录总字节数：

| 内容 | 未压缩体积 |
| --- | ---: |
| .NET 8 运行库 | 70.48 MiB |
| WinUI / Windows App SDK、文字、合成与语言资源 | 68.10 MiB |
| Windows API 的托管投影与 WinRT 运行时 | 25.63 MiB |
| NVIDIA HDR / VSR 原始运行库 | 22.03 MiB |
| Luma 托管程序、XAML、图标及构建信息 | 3.48 MiB |
| SQLite | 2.14 MiB |
| Luma 原生渲染、HDR 解析与静态解码库合在一起 | 1.20 MiB |
| 许可与说明 | 0.88 MiB |

前三项约占 **84.7%**。单个最大的文件 `Microsoft.Windows.SDK.NET.dll` 约 25.12 MiB；VSR DLL 18.25 MiB，HDR DLL 3.77 MiB。`Luma.Native.dll` 的 1.20 MiB 同时包含渲染 / 玻璃 / 格式处理，不是 HDR 解码器单独占用 1.20 MiB。软件本身的图标资源仅约 0.12 MiB，继续压图标收益很小。

## 用户电脑需要哪些解码器

| 能力 | 实际实现与依赖 |
| --- | --- |
| 普通 JPEG / PNG / BMP / TIFF / GIF / ICO，JPEG XR | 调用 Windows 自带的 WIC 图像组件，不启动外部转换程序 |
| Ultra HDR / ISO / Apple 增益图 JPEG | 固定版本的 libultrahdr 2.0.2、libjpeg-turbo 3.2.0 静态编进 `Luma.Native.dll`，应用内完成 HDR 重建 |
| Apple / ISO 增益图 HEIC、PQ HEIC | 应用内解析容器 / HDR 元数据；像素、HEVC、网格和变换交给 Windows WIC 的 **HEIF / HEVC 扩展**。用户机器需要安装相应扩展，扩展不随 Luma 重分发 |
| RTX HDR / VSR | 包内包含 NVIDIA 原始运行 DLL；用户仍需兼容的 RTX 显卡与驱动，RTX HDR 还需系统 HDR |
| 图库索引 | 包内 SQLite，无需用户单独安装数据库 |
| .NET / WinUI | 当前包自包含，用户无需单独安装 .NET 或 Windows App SDK 运行库 |

用户运行 Luma **不需要 Python、FFmpeg、ImageMagick、libheif 命令行工具、NASM、CMake、Visual Studio 或开发 SDK**。其中 CMake / NASM / .NET SDK 等仅用于构建，Python / Pillow / libheif 等用于本机验证，不进入消费者包。HEIF / HEVC 属于系统扩展，不能把“应用自包含”理解为所有 Windows 图像格式的系统解码器都内置。

原生 DLL 的导入检查只见 Windows 系统 DLL；JPEG / Ultra HDR 库静态链接。原始 NVIDIA DLL / PDF / 条款的逐字节核对通过。公开包中没有 SDK ZIP、头文件 / 静态库、测试原片、数据库或开发工具。

## 采纳的优化

1. Inno Setup 从 `lzma2/normal` 改为 `lzma2/ultra64`。同一份未改文件内容的候选中，normal / max / ultra64 分别约 60.97 / 59.44 / 58.28 MiB；max / ultra64 本机压缩约 23.3 / 33.6 秒，原 normal 编译约 16.9 秒。属于单机样本，不代表 CI 固定耗时。ultra64 使用 64 MiB 字典，解压端内存约 67 MiB；增加的是安装解压需求，不改变运行时渲染或图片质量。
2. ZIP 使用 .NET 支持的 `SmallestSize`，保持普通 ZIP，资源管理器可解压；旧版 Windows PowerShell 不支持此枚举时保留 `Optimal` 回退。
3. 主程序 PDB 不再随 `publish` 输出，开发构建符号仍保留。Core 的约 20 KiB 符号仍在 ZIP，安装器一贯排除 PDB；未为这点体积引入额外手工删除规则。
4. 每次 `Build-Release.ps1` 自动生成本地 `package-size.json`，记录目录 / EXE / ZIP 字节数与压缩方式。该诊断文件不改变当前 Release 的四份上传白名单。

## 没有采纳的激进方案

独立实验 `PublishTrimmed=true; TrimMode=partial; JsonSerializerIsReflectionEnabledByDefault=true` 生成约 **108.76 MiB** 目录，设置 / 照片详情检查通过，但仍有 JSON 反射、动态 COM、生成 XAML 静态初始化等裁剪警告，且默认关闭 built-in COM interop。没有完成全部用户路径的裁剪兼容证明；尤其便携默认程序注册仍用动态 COM。按维护者的适度精简要求，不修改正式配置、不压掉警告、不将这份实验当成可发布成品。

实验日志保存在 `artifacts/size-audit/trim-partial.log`，目录仅在忽略的本机 artifacts。实验首次完整看图测试使用了混有 HDR 的原生夹具目录，不满足“最终应触发 SDR 增强”的验证前提，后来只有一张 SDR 又没有可滚动的缩略图范围；这些失败不被当成裁剪导致的产品错误。最终完整包改用独立、足量且首张为 SDR 的相册，完整 49 窗口检查通过。后续裁剪应先解决反射 / COM / XAML 根保留和完整覆盖，再独立评估；不是首发的压缩选项。

共享 .NET / Windows App SDK 可以进一步缩小应用自身目录，但会把首次运行库下载和安装转移给用户。单文件 EXE 也不等于解码 / 运行库消失，还会涉及首次解包。本轮均不采用，不以减少下载统计数字替代完整的安装体验。

## 验证与复现

- 基线包：`artifacts/size-audit/baseline/`；压缩 A/B 日志与详细分类 `artifacts/size-audit/analysis.json`。
- 优化后 `tests/verify_release.py --variant rtx` 通过 503 文件与 ZIP 逐字节比对、许可 / NVIDIA 原件与 SHA256 检查。
- `tests/verify_installer.py` 通过隔离安装 / 重复安装 / 卸载 / 13 个格式关联和其他副本注册归属保留，真实图库与默认程序快照未变。
- `tests/verify_installer.py --previous-installer artifacts/size-audit/previous-installer/Luma-installer-test.exe` 通过 **0.1.17 → 0.1.23**：升级调用不传 `/DIR`，自动找到上次自定义安装位置；两版实际打开同一隔离 SQLite 图库，相册、收藏、最近打开、主题、HDR/VSR 偏好、原照片哈希与缓存标记保留，卸载后图库仍在。报告 `artifacts/installer-verification/1a4264da7e8c4b489a22869f5e7e32ca/verification.json`。旧夹具装的是原 0.1.17 完整载荷，使用隔离测试身份，未安装真实产品身份。
- 最终消费者 DLL `verify_native.py` 实际 SDK HDR-only、VSR-only、组合、40 次缓存对照与 FP16 GPU 读回通过；消费者 EXE 的 49 个真实窗口 / 两次启动通过，报告 `artifacts/viewer-lifecycle-20261015-161644/`。本轮不以安装成功代替 HDR / VSR 实测。

参考：[Inno 压缩参数](https://jrsoftware.org/ishelp/topic_setup_compression.htm)、[C#/WinRT 裁剪支持与边界](https://github.com/microsoft/CsWinRT/blob/master/docs/aot-trimming.md)、[微软自包含部署说明](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)、[Inno 旧目录识别](https://jrsoftware.org/ishelp/topic_setup_usepreviousappdir.htm)。
