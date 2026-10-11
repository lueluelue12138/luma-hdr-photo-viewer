param([string]$Configuration='Release',[switch]$Publish)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_NOLOGO='1'
& "$PSScriptRoot\Build-Native.ps1" -Configuration $Configuration
if($Publish){
    & $dotnet publish "$root\src\Luma.App\Luma.App.csproj" -c $Configuration -p:Platform=x64 "-p:BaseOutputPath=$root\artifacts\build\" -o "$root\artifacts\Luma"
}else{& $dotnet build "$root\src\Luma.App\Luma.App.csproj" -c $Configuration -p:Platform=x64}
if($LASTEXITCODE -ne 0){throw 'Luma build failed.'}
if($Publish){& "$PSScriptRoot\Collect-Licenses.ps1"}
