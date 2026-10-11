# NVIDIA RTX Video SDK 1.1

Place the licensed SDK contents in `external/nvidia/sdk` (include, lib, bin, license PDF). Obtain the SDK through your developer account at https://developer.nvidia.com/rtx-video-sdk/getting-started.

The user supplied the official 1.1 archive locally. `Build-Native.ps1` detects it, links `nvsdk_ngx_s.lib`, and copies release TrueHDR / VSR runtime DLLs plus the unchanged license PDF. Without the SDK, ordinary SDR / native HDR viewing remains available and enhancement reports unavailable.

HDR-only never creates/evaluates VSR. Input is sRGB RGB UNORM; TrueHDR FP16 linear scRGB output goes directly to the FP16 swap chain. Native HDR and transparent images bypass SDR enhancement. Full 7008×4672 JPG input was successfully verified. There is no fixed 8K HDR gate; texture/memory budgets proportionally resample oversized input without invoking VSR.

NVIDIA components keep their proprietary license, independently of Luma's MIT original code. SDK files, archives and binaries are excluded from source control. Review the actual downloaded PDF's distribution, attribution, notification and trademark terms before any public release; no public release or NVIDIA submission has been made.
