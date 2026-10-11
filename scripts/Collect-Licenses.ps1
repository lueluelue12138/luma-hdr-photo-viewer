param([string]$Output)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Output){$Output=Join-Path $root 'artifacts\Luma'}
$licenseOutput=Join-Path $Output 'licenses'
New-Item -ItemType Directory $licenseOutput -Force | Out-Null
Copy-Item "$root\external\licenses\Apache-2.0.txt" $licenseOutput -Force
$assets=Get-Content "$root\src\Luma.App\obj\project.assets.json" -Raw | ConvertFrom-Json
$packageRoot=($assets.packageFolders.PSObject.Properties | Select-Object -First 1).Name
foreach($library in $assets.libraries.PSObject.Properties){
    if($library.Value.type -ne 'package'){continue}
    $source=Join-Path $packageRoot $library.Value.path
    $destination=Join-Path $licenseOutput ($library.Name.Replace('/','-'))
    New-Item -ItemType Directory $destination -Force | Out-Null
    Get-ChildItem $source -File | Where-Object {$_.Name -match '(?i)license|notice|\.nuspec$'} | Copy-Item -Destination $destination -Force
}
$runtimeLicense=Join-Path $root '.tools\dotnet'
if(Test-Path "$runtimeLicense\LICENSE.txt"){Copy-Item "$runtimeLicense\LICENSE.txt" "$licenseOutput\dotnet-LICENSE.txt" -Force;Copy-Item "$runtimeLicense\ThirdPartyNotices.txt" "$licenseOutput\dotnet-ThirdPartyNotices.txt" -Force}
Copy-Item "$root\LICENSE","$root\THIRD-PARTY-NOTICES.md","$root\README.md" $Output -Force
Copy-Item "$root\docs\VERIFICATION.md" $Output -Force -ErrorAction SilentlyContinue
