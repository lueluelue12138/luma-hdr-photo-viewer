param([string]$SourceDirectory='artifacts/HdrJpeg-candidate',[string]$OutputDirectory='artifacts/release-converter')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=[IO.Path]::GetFullPath((Join-Path $root $SourceDirectory))
$output=[IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if(!$output.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Converter packages must remain inside workspace artifacts.'}
$info=Get-Content -LiteralPath (Join-Path $source 'build-info.json') -Raw | ConvertFrom-Json
if($info.product -ne 'Luma HDR Converter' -or $info.version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid converter build information.'}
$exe=Get-Item -LiteralPath (Join-Path $source 'Luma.HdrConverter.exe')
if($exe.VersionInfo.ProductVersion -ne $info.version -or (Get-FileHash -LiteralPath $exe.FullName).Hash -ne $info.executableSha256){throw 'Converter version or executable differs from its recorded build.'}
# An exact consumer whitelist excludes test programs, photos and compiler output.
$files=@('Luma.HdrConverter.exe','build-info.json','使用说明.md','licenses/Luma-LICENSE.txt','licenses/libultrahdr-LICENSE.txt','licenses/Adobe-gain-map-NOTICE.txt','licenses/libjpeg-turbo-LICENSE.txt','licenses/libjpeg-turbo-README.ijg.txt')
foreach($file in $files){if(!(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)){throw "Missing converter payload file: $file"}}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zip=Join-Path $output "Luma-HDR-Converter-$($info.version)-windows-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=[IO.File]::Open($zip,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $source $file),"HdrJpeg/$file",[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}
    finally{$archive.Dispose()}
}finally{$stream.Dispose()}
$hash=(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Portable converter: $zip"
