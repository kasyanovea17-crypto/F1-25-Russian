[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$Source=[IO.Path]::GetFullPath($Source);$Output=[IO.Path]::GetFullPath($Output)
if([IO.Directory]::Exists($Output)){throw 'Preserve prior evidence.'}
[void][IO.Directory]::CreateDirectory($Output)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $Output 'voice-journal-tests.exe'
& $csc /nologo /utf8output /target:exe /platform:x64 /warn:4 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/out:'+$exe) (Join-Path $Source 'SupportDiagnostics.cs') (Join-Path $Source 'MaxVoiceUI.cs') (Join-Path $Source 'SpeechUI.cs') (Join-Path $Source 'EngineerVoiceUI.cs') (Join-Path $PSScriptRoot 'voice_journal_tests.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $exe $Source (Join-Path $Output 'fixtures')
exit $LASTEXITCODE
