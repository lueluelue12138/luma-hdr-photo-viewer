param([string]$Configuration='Release',[switch]$Publish)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_NOLOGO='1'
& "$PSScriptRoot\Build-Native.ps1" -Configuration $Configuration
if($Publish){
    # A new directory prevents removed NuGet dependencies from surviving a
    # publish as stale DLLs. Swap only after the complete build/license succeeds.
    $staging=[System.IO.Path]::GetFullPath((Join-Path $root ('artifacts\publish-'+[guid]::NewGuid().ToString('N'))))
    & $dotnet publish "$root\src\Luma.App\Luma.App.csproj" -c $Configuration -p:Platform=x64 "-p:BaseOutputPath=$root\artifacts\build\" -o $staging
}else{& $dotnet build "$root\src\Luma.App\Luma.App.csproj" -c $Configuration -p:Platform=x64}
if($LASTEXITCODE -ne 0){throw 'Luma build failed.'}
if($Publish){
    & "$PSScriptRoot\Collect-Licenses.ps1" -Output $staging
    $payload=[System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\Luma'))
    $artifactRoot=[System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))+[System.IO.Path]::DirectorySeparatorChar
    $backup=[System.IO.Path]::GetFullPath((Join-Path $root ('artifacts\backup-'+[guid]::NewGuid().ToString('N'))))
    if(!$payload.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)-or !$backup.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)-or !$staging.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Publish targets must remain within workspace artifacts.'}
    if(Get-Process Luma.App -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($payload+[System.IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}){throw "Close the portable Luma window before replacing its files. Prepared output: $staging"}
    if(Test-Path -LiteralPath $payload){Move-Item -LiteralPath $payload -Destination $backup}
    try{Move-Item -LiteralPath $staging -Destination $payload}
    catch{if(Test-Path -LiteralPath $backup){Move-Item -LiteralPath $backup -Destination $payload};throw}
    if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Recurse -Force}
}
