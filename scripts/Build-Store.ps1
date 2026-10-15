param(
    [switch]$SkipBuild,
    [string]$PayloadDirectory,
    [string]$IdentityPath,
    [switch]$ForStore,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$head=(& git -C $root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Store packaging requires Git provenance.'}
if($ForStore){
    if(!$IdentityPath){throw 'ForStore requires the actual Partner Center identity JSON; see packaging/store/identity.example.json.'}
    if(& git -C $root status --porcelain=v1 --untracked-files=all){throw 'Store submission packaging requires clean committed source.'}
}
$identity=$null
if($IdentityPath){
    $identity=Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json
    foreach($field in @('packageName','publisher','publisherDisplayName','displayName')){
        if(!$identity.$field -or $identity.$field -match '(?i)REPLACE|PLACEHOLDER|00000000-0000|Luma Development'){
            throw "Use the exact Partner Center value for $field, not an example or development identity."
        }
    }
    if($identity.packageName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $identity.publisher -notmatch '^CN=.+'){
        throw 'Invalid Partner Center package name or publisher distinguished name.'
    }
}
if(!$PayloadDirectory){$PayloadDirectory=Join-Path $root 'artifacts/release-payload-rtx'}
if(!$SkipBuild){
    if($PayloadDirectory -ne (Join-Path $root 'artifacts/release-payload-rtx')){throw 'A custom payload requires SkipBuild.'}
    & "$PSScriptRoot/Build-Release.ps1" -Variant rtx -Distribution:$ForStore
}
$payload=(Resolve-Path -LiteralPath $PayloadDirectory).Path
$build=Get-Content -LiteralPath "$payload/build-info.json" -Raw | ConvertFrom-Json
if($build.variant -ne 'rtx' -or !$build.rtxHdr -or !$build.rtxVsr){throw 'Store candidate must contain the complete RTX application.'}
if($ForStore -and ($build.sourceDirty -or $build.commit -ne $head)){throw 'Rebuild the clean current source before creating a submission candidate.'}
$manifest=[xml](Get-Content -LiteralPath "$root/packaging/AppxManifest.xml" -Raw)
if($manifest.Package.Identity.Version -ne $build.fileVersion){throw 'Manifest and actual payload versions differ.'}
foreach($name in @('nvngx_truehdr.dll','nvngx_vsr.dll','NVIDIA_RTX_Video_SDK_License.pdf')){
    if((Get-FileHash -LiteralPath "$payload/$name" -Algorithm SHA256).Hash -ne $build.nvidia.runtimeSha256.$name){throw "Original NVIDIA component hash differs: $name"}
}
if((Get-FileHash -LiteralPath "$payload/licenses/NVIDIA-runtime-terms.rtf" -Algorithm SHA256).Hash -ne $build.nvidia.termsSha256){throw 'NVIDIA runtime terms differ.'}
if($identity){
    $manifest.Package.Identity.Name=[string]$identity.packageName
    $manifest.Package.Identity.Publisher=[string]$identity.publisher
    $manifest.Package.Properties.PublisherDisplayName=[string]$identity.publisherDisplayName
    $manifest.Package.Properties.DisplayName=[string]$identity.displayName
    $visual=$manifest.SelectSingleNode('//*[local-name()="VisualElements"]')
    $visual.SetAttribute('DisplayName',[string]$identity.displayName)
}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/store/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $root 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(!$output.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Store output must remain inside workspace artifacts.'}
if($output.Equals($payload,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($payload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Store staging must be separate from the source payload.'
}
if(Test-Path -LiteralPath $output){throw 'Use a new output directory to prevent stale submission files.'}
$staging=Join-Path $output 'payload'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Get-ChildItem -LiteralPath $payload -Force | Copy-Item -Destination $staging -Recurse
# Generate only the staged manifest. Never change the EXE/ZIP source payload.
$manifest.Save((Join-Path $staging 'AppxManifest.xml'))
$sdk='C:/Program Files (x86)/Windows Kits/10/bin'
$makeappx=Get-ChildItem "$sdk/*/x64/makeappx.exe" | Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if(!$makeappx){throw 'Windows SDK MakeAppx.exe is required.'}
$package=Join-Path $output ("Luma_$($build.fileVersion)_x64.msix")
& $makeappx.FullName pack /d $staging /p $package /o *> (Join-Path $output 'makeappx.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $output 'makeappx.log') -Tail 25;throw 'MSIX manifest/payload validation failed.'}
$metadata=[ordered]@{
    schema=1; version=$build.fileVersion; sourceCommit=$build.commit; packagingCommit=$head
    mode=$(if($ForStore){'submission-candidate'}else{'development'})
    storeIdentityConfigured=[bool]$identity; signed=$false; submitted=$false
    package=(Split-Path $package -Leaf); bytes=(Get-Item -LiteralPath $package).Length
    sha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    identity=[ordered]@{name=[string]$manifest.Package.Identity.Name;publisher=[string]$manifest.Package.Identity.Publisher}
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'store-build-info.json') -Encoding utf8
"$($metadata.sha256)  $($metadata.package)" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Unsigned MSIX prepared: $package"
Write-Host 'No installation, certificate trust change, Store upload or submission was performed. Run tests/verify_store_package.py before submission.'
