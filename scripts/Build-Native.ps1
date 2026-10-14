param([string]$Configuration='Release',[switch]$TestFixtures,[switch]$WithoutNvidia,[switch]$RequireNvidia,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($WithoutNvidia -and $RequireNvidia){throw 'WithoutNvidia and RequireNvidia cannot be combined.'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath=if(Test-Path -LiteralPath $vswhere){& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath}else{''}
if(!$vsPath){throw 'MSVC x64 C++ tools and the Visual Studio Installer are required.'}
$vc=(Get-ChildItem (Join-Path $vsPath 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk='C:\Program Files (x86)\Windows Kits\10'
$sdkVersion=(Get-ChildItem "$sdk\Include" -Directory | Where-Object {(Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h')) -and (Test-Path -LiteralPath "$sdk\Lib\$($_.Name)\um\x64\d3d11.lib")} | Sort-Object Name -Descending | Select-Object -First 1).Name
if(!$sdkVersion){throw 'A complete Windows SDK with x64 libraries is required.'}
$env:INCLUDE="$vc\include;$sdk\Include\$sdkVersion\ucrt;$sdk\Include\$sdkVersion\shared;$sdk\Include\$sdkVersion\um;$sdk\Include\$sdkVersion\winrt"
$env:LIB="$vc\lib\x64;$sdk\Lib\$sdkVersion\ucrt\x64;$sdk\Lib\$sdkVersion\um\x64"
$out=if($OutputDirectory){[System.IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'src\Luma.Native\bin'}
New-Item -ItemType Directory $out -Force | Out-Null
$hdrCodecs=& "$PSScriptRoot\Build-HdrCodecs.ps1" -VisualStudioPath $vsPath
$env:INCLUDE+=";$($hdrCodecs.Source);$($hdrCodecs.Source)\lib\include;$($hdrCodecs.JpegInclude)"
$args=@('/nologo','/std:c++20','/utf-8','/EHsc','/W4','/LD','/MT','/O2',"/Fo$out\Luma.Native.obj",(Join-Path $root 'src\Luma.Native\Luma.Native.cpp'),'/link',"/OUT:$out\Luma.Native.dll","/IMPLIB:$out\Luma.Native.lib")
$args+=@($hdrCodecs.Library,$hdrCodecs.JpegLibrary)
$nvidia=Join-Path $root 'external\nvidia\sdk'
$useNvidia=!$WithoutNvidia -and (Test-Path "$nvidia\include\nvsdk_ngx_helpers_truehdr.h")
if($RequireNvidia -and !$useNvidia){throw 'The licensed NVIDIA RTX Video SDK is required for this build. It is not stored in the public repository.'}
if($useNvidia){
    foreach($file in @('include\nvsdk_ngx_helpers_truehdr.h','include\nvsdk_ngx_helpers_vsr.h','lib\Windows\x64\nvsdk_ngx_s.lib','bin\Windows\x64\rel\nvngx_truehdr.dll','bin\Windows\x64\rel\nvngx_vsr.dll','NVIDIA_RTX_Video_SDK_License.pdf')){
        if(!(Test-Path -LiteralPath (Join-Path $nvidia $file))){throw "Incomplete NVIDIA SDK: $file"}
    }
    $env:INCLUDE+=";$nvidia\include"; $env:LIB+=";$nvidia\lib\Windows\x64"
    $args=@('/DLUMA_NVIDIA')+$args
}
if($TestFixtures){
    New-Item -ItemType Directory "$root\artifacts\native-tests" -Force | Out-Null
    & "$vc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /EHsc /MT /O2 "$root\tests\HdrFixture.cpp" "/Fo$out\HdrFixture.obj" /link "/OUT:$out\HdrFixture.exe" windowscodecs.lib ole32.lib
    if($LASTEXITCODE -ne 0){throw 'HDR fixture builder failed.'}
    & "$out\HdrFixture.exe" "$root\artifacts\native-tests\linear-hdr.jxr"
    if($LASTEXITCODE -ne 0){throw 'HDR fixture generation failed.'}
    & "$vc\bin\Hostx64\x64\cl.exe" /nologo /std:c++20 /utf-8 /EHsc /MT /O2 "$root\tests\GainMapFixture.cpp" "/Fo$out\GainMapFixture.obj" /link "/OUT:$out\GainMapFixture.exe" $hdrCodecs.Library $hdrCodecs.JpegLibrary
    if($LASTEXITCODE -ne 0){throw 'Gain map reference fixture builder failed.'}
}
& "$vc\bin\Hostx64\x64\cl.exe" @args
if($LASTEXITCODE -ne 0){throw 'Native renderer build failed.'}
$codecLicenses=Join-Path $out 'licenses'
New-Item -ItemType Directory $codecLicenses -Force | Out-Null
Copy-Item "$($hdrCodecs.Source)\LICENSE" "$codecLicenses\libultrahdr-LICENSE.txt" -Force
Copy-Item "$($hdrCodecs.Source)\adobe-hdr-gain-map-license\NOTICE" "$codecLicenses\Adobe-gain-map-NOTICE.txt" -Force
Copy-Item "$($hdrCodecs.JpegSource)\LICENSE.md" "$codecLicenses\libjpeg-turbo-LICENSE.txt" -Force
Copy-Item "$($hdrCodecs.JpegSource)\README.ijg" "$codecLicenses\libjpeg-turbo-README.ijg.txt" -Force
if($useNvidia){
    Copy-Item "$nvidia\bin\Windows\x64\rel\nvngx_truehdr.dll","$nvidia\bin\Windows\x64\rel\nvngx_vsr.dll" $out -Force
    Copy-Item "$nvidia\NVIDIA_RTX_Video_SDK_License.pdf" $out -Force
}else{
    # Rebuilding without the SDK must not accidentally ship an earlier build's
    # proprietary runtimes. Only these known generated output files are removed.
    foreach($name in @('nvngx_truehdr.dll','nvngx_vsr.dll','NVIDIA_RTX_Video_SDK_License.pdf')){
        $stale=Join-Path $out $name
        if(Test-Path -LiteralPath $stale){Remove-Item -LiteralPath $stale -Force}
    }
}
