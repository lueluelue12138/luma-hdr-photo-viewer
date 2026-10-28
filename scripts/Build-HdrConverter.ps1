param([string]$OutputDirectory='artifacts/HdrJpeg-candidate',[switch]$Test)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=[IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if(Get-Process -Name 'Luma.HdrConverter' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $out 'Luma.HdrConverter.exe') }){throw 'Close the converter before rebuilding this directory.'}
New-Item -ItemType Directory $out -Force | Out-Null
$vsPath=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsPath){throw 'MSVC x64 C++ tools are required.'}
$vc=(Get-ChildItem "$vsPath\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk='C:\Program Files (x86)\Windows Kits\10'
$sdkVersion=(Get-ChildItem "$sdk\Include" -Directory | Where-Object {Test-Path "$sdk\Lib\$($_.Name)\um\x64\windowscodecs.lib"} | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:INCLUDE="$vc\include;$sdk\Include\$sdkVersion\ucrt;$sdk\Include\$sdkVersion\shared;$sdk\Include\$sdkVersion\um;$sdk\Include\$sdkVersion\winrt"
$env:LIB="$vc\lib\x64;$sdk\Lib\$sdkVersion\ucrt\x64;$sdk\Lib\$sdkVersion\um\x64"
$env:PATH="$sdk\bin\$sdkVersion\x64;$vc\bin\Hostx64\x64;$env:PATH"
$codecs=& "$PSScriptRoot\Build-HdrCodecs.ps1" -VisualStudioPath $vsPath -AndroidEncoder
$env:INCLUDE+=";$($codecs.Source);$($codecs.Source)\lib\include;$($codecs.JpegInclude)"
$source=Join-Path $root 'tools\HdrJpeg'
& "$sdk\bin\$sdkVersion\x64\rc.exe" /nologo "/fo$out\App.res" "$source\App.rc"
if($LASTEXITCODE -ne 0){throw 'Converter resources failed.'}
$libs=@($codecs.Library,$codecs.JpegLibrary,'windowscodecs.lib','ole32.lib','oleaut32.lib','propsys.lib','user32.lib','gdi32.lib','shell32.lib','comctl32.lib','dwmapi.lib','gdiplus.lib','advapi32.lib','uuid.lib')
& "$vc\bin\Hostx64\x64\cl.exe" /nologo /std:c++20 /utf-8 /EHsc /W4 /MT /O2 /DUNICODE /D_UNICODE "$source\Main.cpp" "/Fo$out\Main.obj" /link "/OUT:$out\Luma.HdrConverter.exe" /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA /MANIFEST:EMBED "/MANIFESTINPUT:$source\app.manifest" "$out\App.res" @libs
if($LASTEXITCODE -ne 0){throw 'Converter compilation failed.'}
$licenses=Join-Path $out 'licenses';New-Item -ItemType Directory $licenses -Force | Out-Null
Copy-Item "$root\LICENSE" "$licenses\Luma-LICENSE.txt" -Force
Copy-Item "$($codecs.Source)\LICENSE" "$licenses\libultrahdr-LICENSE.txt" -Force
Copy-Item "$($codecs.Source)\adobe-hdr-gain-map-license\NOTICE" "$licenses\Adobe-gain-map-NOTICE.txt" -Force
Copy-Item "$($codecs.JpegSource)\LICENSE.md" "$licenses\libjpeg-turbo-LICENSE.txt" -Force
Copy-Item "$($codecs.JpegSource)\README.ijg" "$licenses\libjpeg-turbo-README.ijg.txt" -Force
Copy-Item "$source\README.md" "$out\使用说明.md" -Force
$info=[ordered]@{product='Luma HDR Converter';version='0.1.0';commit=(& git -C $root rev-parse HEAD);dirty=[bool](& git -C $root status --porcelain --untracked-files=normal);libultrahdr='e5f5a022fe96fc4dc2ee35c19f733a50df807abe';libjpegTurbo='3.2.0';xmp=$true;iso21496=$true;runtime='static MSVC /MT';encoderLibrarySha256=(Get-FileHash $codecs.Library).Hash;executableSha256=(Get-FileHash "$out\Luma.HdrConverter.exe").Hash}
$info | ConvertTo-Json | Set-Content "$out\build-info.json" -Encoding utf8
if($Test){
    & "$vc\bin\Hostx64\x64\cl.exe" /nologo /std:c++20 /utf-8 /EHsc /W4 /MT /O2 /DUNICODE /D_UNICODE "$root\tests\HdrConverterTests.cpp" "/Fo$out\HdrConverterTests.obj" /link "/OUT:$out\HdrConverterTests.exe" @libs
    if($LASTEXITCODE -ne 0){throw 'Converter tests compilation failed.'}
    & "$out\HdrConverterTests.exe" "$root\artifacts\hdr-converter-tests"
    if($LASTEXITCODE -ne 0){throw 'Converter checks failed.'}
}
Write-Host "Converter ready: $out\Luma.HdrConverter.exe"
