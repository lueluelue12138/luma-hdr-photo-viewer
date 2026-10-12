param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$SkipBuild){& "$PSScriptRoot\Build.ps1" -Publish}
$payload=Join-Path $root 'artifacts\Luma'
if(!(Test-Path "$payload\Luma.App.exe")){throw 'Publish the app before packaging.'}
Copy-Item "$root\packaging\AppxManifest.xml" "$payload\AppxManifest.xml" -Force
Copy-Item "$root\LICENSE","$root\THIRD-PARTY-NOTICES.md","$root\README.md" $payload -Force
& "$PSScriptRoot\Collect-Licenses.ps1" -Output $payload
$sdk='C:\Program Files (x86)\Windows Kits\10\bin'
$makeappx=(Get-ChildItem "$sdk\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1).FullName
if(!$makeappx){throw 'Windows SDK MakeAppx.exe is required.'}
$package=Join-Path $root 'artifacts\Luma_0.1.2.0_x64.msix'
$log=Join-Path $root 'artifacts\package.log'
& $makeappx pack /d $payload /p $package /o > $log
if($LASTEXITCODE -ne 0){Get-Content $log -Tail 35;throw 'MSIX schema/payload validation failed.'}
Get-FileHash -LiteralPath $package -Algorithm SHA256 | Format-List
Write-Host 'MSIX built and validated. It is unsigned; use the portable executable now, or sign with a matching publisher certificate before installation.'
