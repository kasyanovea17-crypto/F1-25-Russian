param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output directory must be new.'}
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
$exe=Join-Path $OutputDirectory 'SupportDiagnosticsTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /warn:4 /warnaserror+ /target:exe /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/out:$exe" (Join-Path $source 'SupportDiagnostics.cs') (Join-Path $PSScriptRoot 'SupportDiagnosticsTests.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $exe (Join-Path $OutputDirectory 'fixture')
exit $LASTEXITCODE
