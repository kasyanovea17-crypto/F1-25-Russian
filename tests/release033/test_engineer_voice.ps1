[CmdletBinding()]
param([string]$PackageRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),[string]$OutRoot,[string]$Python='python')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($OutRoot)){$OutRoot=Join-Path ([IO.Path]::GetTempPath()) ('F1RU-EngineerCI-'+[guid]::NewGuid().ToString('N'))}
& $Python -X utf8 -B (Join-Path $PSScriptRoot 'test_engineer_voice.py') --package $PackageRoot --out $OutRoot
exit $LASTEXITCODE
