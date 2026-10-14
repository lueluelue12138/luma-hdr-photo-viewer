param(
    [ValidateSet('standard','rtx')][string]$Variant='standard',
    [string]$Tag,
    [switch]$SkipBuild,
    [switch]$TestInstaller,
    [switch]$Distribution,
    [string]$RepositoryUrl='https://github.com/lueluelue12138/luma-hdr-photo-viewer'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Variant -eq 'rtx'){
    if(!(Test-Path -LiteralPath "$root\packaging\RTX-EULA.rtf")){throw 'NVIDIA runtime terms are required in packaging/RTX-EULA.rtf.'}
}
$project=[xml](Get-Content -LiteralPath "$root\src\Luma.App\Luma.App.csproj" -Raw)
$version=[string]($project.Project.PropertyGroup.Version | Where-Object {$_} | Select-Object -First 1)
$manifest=[xml](Get-Content -LiteralPath "$root\packaging\AppxManifest.xml" -Raw)
$fileVersion=[string]$manifest.Package.Identity.Version
if($fileVersion -ne "$version.0"){throw 'Project and MSIX versions must match before packaging.'}
if($Tag){
    if($Tag -notmatch '^v(?<base>\d+\.\d+\.\d+)(?:-(?<pre>[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?$'){throw 'Use a release tag such as v0.1.16 or v0.1.16-beta.1.'}
    if($Matches.base -ne $version){throw "Release tag $Tag does not match source version $version."}
    $releaseVersion=$Tag.Substring(1)
}else{$releaseVersion=$version}
$payload=Join-Path $root "artifacts\release-payload-$Variant"
if(!$SkipBuild){& "$PSScriptRoot\Build.ps1" -Publish -WithoutNvidia:($Variant -eq 'standard') -RequireNvidia:($Variant -eq 'rtx') -OutputDirectory $payload}
if(!(Test-Path -LiteralPath "$payload\Luma.App.exe")){throw "Release payload is missing: $payload"}
$nvidiaFiles=@('nvngx_truehdr.dll','nvngx_vsr.dll','NVIDIA_RTX_Video_SDK_License.pdf')
foreach($name in $nvidiaFiles){
    if((Test-Path -LiteralPath (Join-Path $payload $name)) -ne ($Variant -eq 'rtx')){throw "Payload does not match declared $Variant variant: $name"}
}
if($Variant -eq 'rtx'){
    New-Item -ItemType Directory -Path "$payload\licenses" -Force | Out-Null
    Copy-Item -LiteralPath "$root\packaging\RTX-EULA.rtf" -Destination "$payload\licenses\NVIDIA-runtime-terms.rtf" -Force
}
$gitCommit=(& git -C $root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Release provenance requires a Git commit.'}
$sourceDirty=[bool](& git -C $root status --porcelain=v1 --untracked-files=all)
if($LASTEXITCODE -ne 0){throw 'Cannot verify release source status.'}
if($Distribution -and $sourceDirty){throw 'Distribution requires a clean committed source tree.'}
$metadata=@{schema=1;version=$releaseVersion;fileVersion=$fileVersion;variant=$Variant;commit=$gitCommit;sourceDirty=$sourceDirty;repository=$RepositoryUrl;architecture='x64';minimumWindows='Windows 11';rtxHdr=($Variant -eq 'rtx');rtxVsr=($Variant -eq 'rtx');nativeHdrFormats=@('jxr','wdp','hdp','jpeg-gain-map');gainMapCodec='libultrahdr 2.0.2';signed=$false}
if($Variant -eq 'rtx'){
    $runtimeHashes=@{}
    foreach($name in $nvidiaFiles){$runtimeHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $payload $name) -Algorithm SHA256).Hash.ToLowerInvariant()}
    $metadata.nvidia=@{sdkVersion='1.1.0';inputRevision=$env:NVIDIA_SDK_REVISION;inputArchiveSha256=$env:NVIDIA_SDK_SHA256;runtimeSha256=$runtimeHashes;termsSha256=(Get-FileHash -LiteralPath "$payload\licenses\NVIDIA-runtime-terms.rtf" -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$payload\build-info.json" -Encoding utf8
$compiler=& "$PSScriptRoot\Install-BuildTools.ps1"
$output=Join-Path $root "artifacts\release-$Variant"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$associations=Join-Path $output 'FileAssociations.iss'
$entries=foreach($extension in @($manifest.SelectNodes('//*[local-name()="FileType"]') | ForEach-Object {$_.InnerText})){
    if($extension -notmatch '^\.[a-z0-9]+$'){throw 'Invalid supported extension in manifest.'}
    'Root: HKCU; Subkey: "{#CapabilityKey}\FileAssociations"; ValueType: string; ValueName: "'+$extension+'"; ValueData: "{#ProgId}"'
    'Root: HKCU; Subkey: "Software\Classes\'+$extension+'\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""'
}
$entries | Set-Content -LiteralPath $associations -Encoding utf8
$uninstallAssociations=Join-Path $output 'UninstallAssociations.iss'
@($manifest.SelectNodes('//*[local-name()="FileType"]') | ForEach-Object {"RegDeleteValue(HKCU, 'Software\Classes\$($_.InnerText)\OpenWithProgids', '{#ProgId}');"}) | Set-Content -LiteralPath $uninstallAssociations -Encoding utf8
$compilerArgs=@("/DPayloadDir=$payload","/DOutputDir=$output","/DReleaseVersion=$releaseVersion","/DFileVersion=$fileVersion","/DBuildVariant=$Variant","/DRepositoryUrl=$RepositoryUrl","/DFileAssociationsFile=$associations","/DUninstallAssociationsFile=$uninstallAssociations")
if($Variant -eq 'rtx'){$compilerArgs+="/DRtxLicenseFile=$root\packaging\RTX-EULA.rtf"}
& $compiler @compilerArgs "$root\packaging\Luma.iss"
if($LASTEXITCODE -ne 0){throw 'EXE installer compilation failed.'}
$zip=Join-Path $output "Luma-$releaseVersion-windows-x64-$Variant-portable.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[System.IO.Compression.CompressionLevel]::Optimal,$false)
$installer=Join-Path $output "Luma-$releaseVersion-windows-x64-$Variant-setup.exe"
$sums=@($installer,$zip) | ForEach-Object {"$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"}
$sums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding utf8
if($TestInstaller){
    $testOutput=Join-Path $root 'artifacts\installer-verification'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testArgs=@("/DPayloadDir=$payload","/DOutputDir=$testOutput","/DReleaseVersion=$releaseVersion","/DFileVersion=$fileVersion","/DBuildVariant=$Variant","/DRepositoryUrl=$RepositoryUrl","/DFileAssociationsFile=$associations","/DUninstallAssociationsFile=$uninstallAssociations",'/DTestInstall=1')
    if($Variant -eq 'rtx'){$testArgs+="/DRtxLicenseFile=$root\packaging\RTX-EULA.rtf"}
    & $compiler @testArgs "$root\packaging\Luma.iss"
    if($LASTEXITCODE -ne 0){throw 'Isolated installer fixture compilation failed.'}
}
Write-Host "Release candidates prepared in $output. Nothing was uploaded or published."
