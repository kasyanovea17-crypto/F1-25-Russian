[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$Work)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Package=[IO.Path]::GetFullPath($Package)
$Work=[IO.Path]::GetFullPath($Work)
if([IO.Directory]::Exists($Work)-or[IO.File]::Exists($Work)){throw 'Preserve evidence: choose a new Work directory.'}
$launcher=Join-Path $Package 'Launcher.exe'
if(-not[IO.File]::Exists($launcher)){throw 'Build the package before UI diagnostic tests.'}
$exe=Join-Path $Package 'UiDiagnosticsTests.exe'
if([IO.File]::Exists($exe)){throw 'Preserve existing harness: choose a clean package or move it manually.'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /utf8output /target:exe /platform:x64 /warn:4 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/out:'+$exe) (Join-Path $PSScriptRoot 'UiDiagnosticsTests.cs')
if($LASTEXITCODE-ne0){throw 'UI diagnostic harness compile failed.'}
$report=$Work+'.txt'
& $exe $launcher $Work $report
exit $LASTEXITCODE
