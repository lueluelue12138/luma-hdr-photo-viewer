param([string]$ArchivePath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$env:NVIDIA_SDK_SHA256){throw 'Configure the NVIDIA_SDK_SHA256 variable for the exact licensed SDK archive.'}
if($env:NVIDIA_SDK_SHA256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'NVIDIA SDK SHA256 is invalid.'}
if($ArchivePath){
    $zip=[System.IO.Path]::GetFullPath($ArchivePath)
    if(!(Test-Path -LiteralPath $zip -PathType Leaf)){throw 'The private SDK checkout does not contain its archive.'}
}else{
    if(!$env:NVIDIA_SDK_URL){throw 'Provide ArchivePath from the private SDK checkout, or configure a private NVIDIA_SDK_URL secret.'}
    try{$uri=[Uri]$env:NVIDIA_SDK_URL}catch{throw 'The private SDK address is invalid.'}
    if($uri.Scheme -ne 'https'){throw 'The private SDK download must use HTTPS.'}
    $out=Join-Path $root 'artifacts\private-sdk-download'
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $zip=Join-Path $out 'sdk.zip'
    try{Invoke-WebRequest -Uri $uri -OutFile $zip}catch{throw 'Private SDK download failed. The address is omitted from logs.'}
}
if((Get-Item -LiteralPath $zip).Length -gt 128MB){throw 'Unexpected SDK download size.'}
if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $env:NVIDIA_SDK_SHA256){throw 'NVIDIA SDK download checksum mismatch.'}
$sdk=[System.IO.Path]::GetFullPath((Join-Path $root 'external\nvidia\sdk'))
$prefix=$sdk+[System.IO.Path]::DirectorySeparatorChar
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[System.IO.Compression.ZipFile]::OpenRead($zip)
try{
    [long]$expanded=0
    foreach($entry in $archive.Entries){
        $target=[System.IO.Path]::GetFullPath((Join-Path $sdk $entry.FullName))
        if(!$target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'SDK archive path escapes its intended directory.'}
        if((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000){throw 'SDK archive symlinks are not allowed.'}
        $expanded+=$entry.Length
        if($expanded -gt 512MB){throw 'Unexpected expanded SDK size.'}
    }
}finally{$archive.Dispose()}
New-Item -ItemType Directory -Path $sdk -Force | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($zip,$sdk,$true)
Write-Host 'Verified licensed SDK prepared. SDK inputs are never uploaded as public workflow artifacts.'
