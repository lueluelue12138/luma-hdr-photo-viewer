param([string]$OutputDirectory='artifacts\hdr-samples')
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory $out -Force | Out-Null
# QA downloads only. These photographs are never included in an application,
# installer, source commit or release. Retain the source URLs and checksums.
$sources=@(
    @{Name='apple-camera.heic';Url='https://media.githubusercontent.com/media/johncf/apple-hdr-heic/master/tests/data/hdr-sample.heic';Sha='c690fa4ecc6ae71ee9a926de869a95835a5d41e764ccbc200647e2fb9d0800cc'},
    @{Name='ipad-iso.zip';Url='https://github.com/user-attachments/files/21392852/matrix4.HEIC.zip';Sha='40a2db436d2bda9f5c0b174b52c35275c3e04f9d609c203e8042eb150a92dcc5'},
    @{Name='apple-official-pq.zip';Url='https://docs-assets.developer.apple.com/published/6bb6a5a2302b/SupportingHDRImagesInYourApp.zip';Sha='09efee6ad2c07b59668f4d70b9912ab941c4e9289c3954a1e2a58c0a134506e7'}
)
foreach($sample in $sources){
    $target=Join-Path $out $sample.Name
    if(!(Test-Path -LiteralPath $target)){& curl.exe -fLsS --retry 3 -o $target $sample.Url;if($LASTEXITCODE -ne 0){throw "Sample download failed: $($sample.Name)"}}
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sample.Sha){throw "Sample checksum mismatch: $($sample.Name)"}
    if($sample.Name.EndsWith('.zip')){Expand-Archive -LiteralPath $target -DestinationPath (Join-Path $out $sample.Name.Replace('.zip','')) -Force}
}
$sources | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'sources.json') -Encoding utf8
Write-Output "HDR samples verified: $out"
