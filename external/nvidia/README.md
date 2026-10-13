# NVIDIA RTX Video SDK 1.1

Place the licensed SDK contents in `external/nvidia/sdk` (include, lib, bin, license PDF). Obtain the SDK through your developer account at https://developer.nvidia.com/rtx-video-sdk/getting-started.

The user supplied the official 1.1 archive locally. `Build-Native.ps1` detects it, links `nvsdk_ngx_s.lib`, and copies release TrueHDR / VSR runtime DLLs plus the unchanged license PDF. Without the SDK, ordinary SDR / native HDR viewing remains available and enhancement reports unavailable.

HDR-only never creates/evaluates VSR. Input is sRGB RGB UNORM; TrueHDR FP16 linear scRGB output goes directly to the FP16 swap chain. Native HDR and transparent images bypass SDR enhancement. Full 7008×4672 JPG input was successfully verified. There is no fixed 8K HDR gate; texture/memory budgets proportionally resample oversized input without invoking VSR.

NVIDIA components keep their proprietary license, independently of Luma's MIT original code. SDK files, archives and binaries are excluded from this public source repository. Release CI retrieves the maintainer's original archive from a private build-input repository through a read-only deploy key, pinned by Git commit and SHA256. The SDK archive, headers, libraries, developer tools and samples are never public release assets.

The complete RTX application package includes the unchanged license PDF, NVIDIA runtime terms, and only the release runtime DLLs/object code used by the application. The installer provides a button to read the original PDF before acceptance. Tag builds create complete packages and a Release draft; compiling a private candidate is separate from authorizing a formal public distribution. Review remaining notification/trademark requirements before publicly publishing a download or commercial release; no NVIDIA submission has been made. See [release setup](../../docs/RELEASING.md).
