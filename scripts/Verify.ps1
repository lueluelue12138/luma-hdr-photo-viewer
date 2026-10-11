param([string]$Photo)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
& "$PSScriptRoot\Build-Native.ps1" -TestFixtures
& $dotnet run --project "$root\tests\Luma.Tests\Luma.Tests.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Library verification failed.'}
$python=(Get-Command python -ErrorAction Stop).Source
$env:PYTHONUTF8='1'
if($Photo){& $python "$root\tests\verify_native.py" $Photo}else{& $python "$root\tests\verify_native.py"}
if($LASTEXITCODE -ne 0){throw 'Native renderer verification failed.'}
