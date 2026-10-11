param([string]$Configuration='Release',[switch]$TestFixtures,[switch]$WithoutNvidia,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vs=Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio\2022','C:\Program Files\Microsoft Visual Studio' -Directory -ErrorAction SilentlyContinue | Where-Object {Test-Path (Join-Path $_.FullName 'VC\Tools\MSVC')} | Select-Object -First 1
if(!$vs){throw 'MSVC C++ tools are required.'}
$vc=(Get-ChildItem (Join-Path $vs.FullName 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk='C:\Program Files (x86)\Windows Kits\10'
$sdkVersion=(Get-ChildItem "$sdk\Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:INCLUDE="$vc\include;$sdk\Include\$sdkVersion\ucrt;$sdk\Include\$sdkVersion\shared;$sdk\Include\$sdkVersion\um;$sdk\Include\$sdkVersion\winrt"
$env:LIB="$vc\lib\x64;$sdk\Lib\$sdkVersion\ucrt\x64;$sdk\Lib\$sdkVersion\um\x64"
$out=if($OutputDirectory){[System.IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'src\Luma.Native\bin'}
New-Item -ItemType Directory $out -Force | Out-Null
$args=@('/nologo','/std:c++20','/utf-8','/EHsc','/W4','/LD','/MT','/O2',"/Fo$out\Luma.Native.obj",(Join-Path $root 'src\Luma.Native\Luma.Native.cpp'),'/link',"/OUT:$out\Luma.Native.dll","/IMPLIB:$out\Luma.Native.lib")
$nvidia=Join-Path $root 'external\nvidia\sdk'
if(!$WithoutNvidia -and (Test-Path "$nvidia\include\nvsdk_ngx_helpers_truehdr.h")){
    $env:INCLUDE+=";$nvidia\include"; $env:LIB+=";$nvidia\lib\Windows\x64"
    $args=@('/DLUMA_NVIDIA')+$args
}
if($TestFixtures){
    New-Item -ItemType Directory "$root\artifacts\native-tests" -Force | Out-Null
    & "$vc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /EHsc /MT /O2 "$root\tests\HdrFixture.cpp" "/Fo$out\HdrFixture.obj" /link "/OUT:$out\HdrFixture.exe" windowscodecs.lib ole32.lib
    if($LASTEXITCODE -ne 0){throw 'HDR fixture builder failed.'}
    & "$out\HdrFixture.exe" "$root\artifacts\native-tests\linear-hdr.jxr"
    if($LASTEXITCODE -ne 0){throw 'HDR fixture generation failed.'}
}
& "$vc\bin\Hostx64\x64\cl.exe" @args
if($LASTEXITCODE -ne 0){throw 'Native renderer build failed.'}
if(!$WithoutNvidia -and (Test-Path "$nvidia\include\nvsdk_ngx_helpers_truehdr.h")){
    Copy-Item "$nvidia\bin\Windows\x64\rel\nvngx_truehdr.dll","$nvidia\bin\Windows\x64\rel\nvngx_vsr.dll" $out -Force
    Copy-Item "$nvidia\NVIDIA_RTX_Video_SDK_License.pdf" $out -Force
}
