# Third-party components

Luma's MIT license applies to its original source and original geometric icon only. It does not replace any license below. Dependency notices are copied from locally resolved packages into `licenses` in the published output.

| Component | Version | License / source |
| --- | --- | --- |
| .NET runtime and libraries | 8.0.31 runtime used by SDK 8.0.425 | MIT and bundled third-party notices; preserve runtime notices |
| Microsoft.WindowsAppSDK | 2.5.1 | Microsoft package license and NOTICE.txt; bundled runtime components retain their own terms |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.9169 | Microsoft package SDK license; build dependency |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 8.0.31 | MIT |
| SQLitePCLRaw components | 2.1.12 | Apache-2.0 for managed wrappers; SQLite public domain; package native dependency notices apply |
| NVIDIA RTX™ Video SDK | 1.1.0 | NVIDIA proprietary license supplied in NVIDIA_RTX_Video_SDK_License.pdf |
| WIC / Direct3D / DXGI / Windows system codecs | Operating system | Windows system components, not relicensed by Luma |

This software contains source code provided by NVIDIA Corporation.

NVIDIA RTX™ Video SDK supplies the TrueHDR and VSR features. `nvsdk_ngx_s.lib` is linked into the original renderer; only the SDK's release `nvngx_truehdr.dll` and `nvngx_vsr.dll` are copied into this local development application. The SDK source, libraries, archive, developer tools and documentation are excluded from source control. The original license PDF accompanies the local output. These components must not be sublicensed as MIT or distributed as a standalone SDK.

The downloaded SDK license (February 23, 2024, with its attached supplement) contains conditions on object-code incorporation, protective downstream terms, attribution, notification and trademark use. Before public distribution, review the unchanged PDF for the actual planned release, resolve which NGX/RTX Video supplement requirements apply, and complete any required NVIDIA notification and proposed trademark-use review. No notification, marketing submission, public upload or release has been made. MIT dependency compatibility does not establish completion of the NVIDIA release requirements.

The generated local MSIX is an unsigned development artifact. Store distribution additionally requires the publisher identity, signing and package certification steps. Luma's name and trademark availability have not been checked.
