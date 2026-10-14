param([string]$VisualStudioPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.tools\hdr-codecs'
New-Item -ItemType Directory $cache -Force | Out-Null
if(!$VisualStudioPath){$VisualStudioPath=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath}
$cmake=Join-Path $VisualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if(!(Test-Path -LiteralPath $cmake)){$cmake=(Get-Command cmake -ErrorAction Stop).Source}
$vsVersion=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion
$generator=switch(([version]$vsVersion).Major){17{'Visual Studio 17 2022'}18{'Visual Studio 18 2026'}default{throw 'Unsupported Visual Studio CMake generator.'}}
function Source([string]$name,[string]$url,[string]$sha,[string]$directory){
    $tarArchive=$url.EndsWith('.tar.gz')
    $archive=Join-Path $cache $(if($tarArchive){"$name.tar.gz"}else{"$name.zip"})
    if(!(Test-Path -LiteralPath $archive)){
        & curl.exe --fail --silent --show-error --location --retry 3 --output $archive $url
        if($LASTEXITCODE -ne 0){throw "Cannot download $name from its official source."}
    }
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha){throw "$name source archive SHA256 mismatch. Remove the cached archive before retrying."}
    $source=Join-Path $cache $directory
    if(!(Test-Path -LiteralPath $source)){
        if($tarArchive){& tar.exe -xzf $archive -C $cache;if($LASTEXITCODE -ne 0){throw "$name source extraction failed."}}
        else{Expand-Archive -LiteralPath $archive -DestinationPath $cache}
    }
    return $source
}
# Every build input is pinned; no transitive CMake downloads or system install.
$uhdr=Source 'libultrahdr' 'https://codeload.github.com/google/libultrahdr/zip/e5f5a022fe96fc4dc2ee35c19f733a50df807abe' '7c2b5f02b463cb9e67ad00f24cc504198ffc384d32b3793781038245fb4b9171' 'libultrahdr-e5f5a022fe96fc4dc2ee35c19f733a50df807abe'
$jpeg=Source 'libjpeg-turbo-3.2.0' 'https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.2.0/libjpeg-turbo-3.2.0.tar.gz' '6f30092cef9fb839779646608f4ee14ae3cbac989c47fa05e841b0841f09878e' 'libjpeg-turbo-3.2.0'
$nasm=Source 'nasm' 'https://www.nasm.us/pub/nasm/releasebuilds/2.16.03/win64/nasm-2.16.03-win64.zip' '3ee4782247bcb874378d02f7eab4e294a84d3d15f3f6ee2de2f47a46aa7226e6' 'nasm-2.16.03'
$jpegBuild=Join-Path $cache 'jpeg-build-3.2.0'
$uhdrBuild=Join-Path $cache 'uhdr-build'
& $cmake -S $jpeg -B $jpegBuild -G $generator -A x64 -DENABLE_SHARED=OFF -DWITH_CRT_DLL=OFF -DWITH_SIMD=ON "-DCMAKE_ASM_NASM_COMPILER=$nasm\nasm.exe" -DWITH_TOOLS=OFF -DWITH_TESTS=OFF -DWITH_TURBOJPEG=OFF | Out-Host
if($LASTEXITCODE -ne 0){throw 'JPEG codec configuration failed.'}
& $cmake --build $jpegBuild --config Release --target jpeg-static --parallel 6 | Out-Host
if($LASTEXITCODE -ne 0){throw 'JPEG codec compilation failed.'}
$jpegInclude=Join-Path $cache 'jpeg-include'
New-Item -ItemType Directory $jpegInclude -Force | Out-Null
Copy-Item "$jpeg\src\jpeglib.h","$jpeg\src\jmorecfg.h","$jpeg\src\jerror.h","$jpegBuild\jconfig.h" $jpegInclude -Force
$jpegLib=Join-Path $jpegBuild 'Release\jpeg-static.lib'
& $cmake -S $uhdr -B $uhdrBuild -G $generator -A x64 -DBUILD_SHARED_LIBS=OFF -DUHDR_BUILD_DEPS=OFF -DUHDR_BUILD_EXAMPLES=OFF -DUHDR_BUILD_TESTS=OFF -DUHDR_ENABLE_HEIF=OFF -DUHDR_ENABLE_GLES=OFF -DUHDR_ENABLE_INSTALL=OFF "-DJPEG_LIBRARY=$($jpegLib.Replace('\','/'))" "-DJPEG_INCLUDE_DIR=$($jpegInclude.Replace('\','/'))" | Out-Host
if($LASTEXITCODE -ne 0){throw 'HDR codec configuration failed.'}
& $cmake --build $uhdrBuild --config Release --target uhdr-static --parallel 6 | Out-Host
if($LASTEXITCODE -ne 0){throw 'HDR codec compilation failed.'}
# Return a single object; keep compiler output on the host stream.
[pscustomobject]@{Source=$uhdr;Library=(Join-Path $uhdrBuild 'Release\uhdr-static.lib');JpegLibrary=$jpegLib;JpegSource=$jpeg;JpegInclude=$jpegInclude}
