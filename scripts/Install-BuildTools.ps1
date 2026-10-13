param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tools=Join-Path $root '.tools\innosetup-7.1.0'
$compiler=Join-Path $tools 'ISCC.exe'
if(Test-Path -LiteralPath $compiler){Write-Output $compiler;return}
$download=Join-Path $root '.tools\downloads'
New-Item -ItemType Directory -Path $download -Force | Out-Null
$installer=Join-Path $download 'innosetup-7.1.0-x64.exe'
$url='https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
$sha='0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
if(!(Test-Path -LiteralPath $installer)){Invoke-WebRequest -Uri $url -OutFile $installer}
if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $sha){throw 'Inno Setup download checksum mismatch.'}
$signature=Get-AuthenticodeSignature -LiteralPath $installer
if($signature.Status -ne 'Valid'){throw "Inno Setup publisher signature is not valid: $($signature.Status)"}
$process=Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER',('/DIR="'+$tools+'"'),('/LOG="'+(Join-Path $download 'innosetup-install.log')+'"')) -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)){throw "Inno Setup tool installation failed: $($process.ExitCode)"}
Write-Output $compiler
