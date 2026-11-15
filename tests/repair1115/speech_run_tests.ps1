[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
foreach($m in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management')){Import-Module (Join-Path $PSHOME ('Modules\'+$m+'\'+$m+'.psd1')) -ErrorAction Stop}
$Source=[IO.Path]::GetFullPath($Source);$Output=[IO.Path]::GetFullPath($Output)
if([IO.Directory]::Exists($Output)){throw 'Preserve existing test evidence.'}
[void][IO.Directory]::CreateDirectory($Output)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $Output 'speech-tests.exe'
& $csc /nologo /utf8output /target:exe /platform:x64 /warn:4 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/out:'+$exe) (Join-Path $Source 'SpeechUI.cs') (Join-Path $Source 'SupportDiagnostics.cs') (Join-Path $PSScriptRoot 'speech_lease_tests.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $exe $Source (Join-Path $Output 'fixtures')
exit $LASTEXITCODE
