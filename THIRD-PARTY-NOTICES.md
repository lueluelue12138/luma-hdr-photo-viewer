# Third-party components

Luma's MIT license applies to its original source and project artwork only, including the application logo supplied by the project maintainer in `branding/Luma.png`. It does not replace any license below. Dependency notices are copied from locally resolved packages into `licenses` in the published output.

| Component | Version | License / source |
| --- | --- | --- |
| .NET runtime and libraries | 8.0.31 runtime used by SDK 8.0.425 | MIT and bundled third-party notices; preserve runtime notices |
| Windows App SDK components | WinUI 2.3.9, Foundation 2.3.12, InteractiveExperiences 2.1.9, DWrite 2.1.0, Base 2.0.4 (same component versions as SDK 2.5.1) | Microsoft package licenses and notices in `licenses`; bundled components retain their own terms |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.9169 | Microsoft package SDK license; build dependency |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 8.0.31 | MIT |
| SQLitePCLRaw components | 2.1.12 | Apache-2.0 for managed wrappers; SQLite public domain; package native dependency notices apply |
| NVIDIA RTX™ Video SDK | 1.1.0 | NVIDIA proprietary license supplied in NVIDIA_RTX_Video_SDK_License.pdf |
| libultrahdr | 2.0.2, commit e5f5a022fe96fc4dc2ee35c19f733a50df807abe | MIT / Apache-2.0; [official source](https://github.com/google/libultrahdr/tree/e5f5a022fe96fc4dc2ee35c19f733a50df807abe); original license and Adobe gain-map notice preserved in `licenses` |
| libjpeg-turbo | 3.2.0 | IJG / BSD-style / zlib terms; [official source release](https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.0); original LICENSE.md and README.ijg preserved in `licenses` |
| WIC / Direct3D / DXGI / Windows system codecs | Operating system | Windows system components, not relicensed by Luma |

This software contains source code provided by NVIDIA Corporation.

This software is based in part on the work of the Independent JPEG Group.

This product includes Gain Map technology under license by Adobe.

The JPEG gain-map codec is statically linked into Luma.Native.dll and is available in both standard and RTX builds. These original upstream sources are downloaded with pinned SHA256 checks and are not modified. Its HEIF/libheif, HEVC, AV1 and OpenGL build dependencies are disabled. Luma's HEIC path uses the user's installed Windows HEIF/HEVC codecs; it does not bundle or relicense those codecs. Apple's published gain-map equations are implemented in Luma's own code. NASM 2.16.03 is a pinned build-only tool for JPEG SIMD assembly. pillow-heif/libheif and numpy are used only for local reference tests, never consumer packages. Neither codec sources, reference tools nor test photographs are included in consumer packages.

NVIDIA RTX™ Video SDK supplies the TrueHDR and VSR features. `nvsdk_ngx_s.lib` is linked into the original renderer; only the SDK's release `nvngx_truehdr.dll` and `nvngx_vsr.dll` are copied into the complete RTX application. The SDK source, libraries, archive, developer tools and documentation are excluded from this public source repository and from consumer packages. CI reads the maintainer's original SDK archive from a private repository through a dedicated read-only deploy key. The unchanged original license PDF and `licenses/NVIDIA-runtime-terms.rtf` accompany RTX packages; the installer makes the original PDF available before acceptance. These components are not sublicensed as MIT or distributed as a standalone SDK.

The downloaded SDK license (February 23, 2024, with its attached supplement) contains conditions on object-code incorporation, protective downstream terms, attribution, notification and trademark use. SDK-controlled builds and owner-accessible Release drafts are distinct from a formal public runtime release. Before publicly publishing a download, resolve the applicable NGX/RTX Video supplement requirements and complete any required NVIDIA notification and proposed trademark-use review. No NVIDIA notification or trademark/marketing submission has been made. MIT dependency compatibility and a successful private build do not establish completion of every public distribution requirement.

The generated local MSIX is an unsigned development artifact. Store distribution additionally requires the publisher identity, signing and package certification steps. Luma's name and trademark availability have not been checked.
