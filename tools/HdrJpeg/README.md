# Luma HDR 转换器 0.1.0

把 Windows 的线性 HDR 截图（`.jxr` / `.wdp` / `.hdp`）转换成带增益图的 **Ultra HDR JPG**。Windows 10 2004 或更新的 x64 系统；无需安装、无需 .NET / Python / RTX 显卡，无需开启电脑 HDR。全部处理在本机完成，不联网。

1. 解压便携包，双击 `Luma.HdrConverter.exe`。
2. 拖入图片，或点击“添加图片”；支持多选，也可将图片拖到 EXE 上。
3. 点击“开始转换”。默认保存到各原图旁的 `Ultra HDR` 文件夹，也可更改保存位置。
4. 点击“打开文件夹”，将生成的 `_UltraHDR.jpg` **作为原文件**传到手机。

原图不变；同名输出自动加编号。失败会显示原因，点击“开始转换”仅重试未完成的图片。取消会等待当前编码返回，取消前未保存的图片不会写出；已经完成的图片保留。最多一次添加 1000 张。

## 手机观看

输出采用 Google libultrahdr，包含 SDR 底图、RGB 增益图、XMP 与 ISO 21496-1 元数据。兼容的 Android HDR 相册可读取高光，普通 JPEG 软件会显示 SDR 底图。建议 Android 14 或更新、支持 HDR 的屏幕及相册。**具体小米 / HyperOS 机型和相册版本尚需真机验证，不保证所有小米手机都会点亮 HDR。**

用 USB、局域网文件传输或网盘传送原文件。聊天软件的图片压缩、编辑及另存可能丢弃增益图。可先用支持 Ultra HDR 的 Google 相册对照，再判断系统相册的支持情况。

## 范围与画质

- 仅处理线性浮点 / 定点 scRGB JPEG XR；不根据扩展名或整数位深假定 HDR。普通 JPEG、PNG、HEIC、AVIF 暂未接入。
- 保留输入分辨率，支持奇数尺寸，范围 8–16384 像素且最多 4000 万像素。大图需要较多内存，逐张顺序处理。
- 按 scRGB `1 = 80 nits` 转为编码器的 `1 = 203 nits`，在线性 Rec.2020 中编码，不依赖本机屏幕亮度。生成 SDR 底图时使用库自带色调映射。
- JPEG 底图与全尺寸 RGB 增益图均使用质量 95；属于有损转换，不能保证逐像素还原。超出 Rec.2020 或 10000 nits 的值会裁切并在状态中提示；透明区域在线性光下合成到黑色。
- 应用 EXIF 方向，不复制其他 EXIF、定位、缩略图及来源 ICC；Windows 截图按线性 scRGB 解释，不支持任意自定义色彩空间的 JXR。
- 软件未签名，尚未制作安装器或公开发行。

## 构建

在仓库根运行 `./scripts/Build-HdrConverter.ps1 -Test`，需要 Visual Studio C++ x64 工具和 Windows SDK。与 Luma 看图器的原生库分开构建，编码器显式开启 XMP + ISO，不更改主程序版本或安装。

规范：[Android Ultra HDR](https://developer.android.com/media/platform/hdr-image-format)、[libultrahdr](https://github.com/google/libultrahdr)、[Windows JPEG XR](https://learn.microsoft.com/en-us/windows/win32/wic/jpeg-xr-codec)。再分发时保留 `licenses` 文件夹及本说明。
