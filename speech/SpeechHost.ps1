[CmdletBinding()]
param(
 [ValidateSet('status','verify','install','restore','migrate','model')][string]$Action='status',
 [Parameter(Mandatory=$true)][string]$GamePath,
 [Parameter(Mandatory=$true)][string]$ResultPath,
 [string]$LegacyState
)
$ErrorActionPreference='Stop'
foreach($module in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management')){
 Import-Module (Join-Path $PSHOME ('Modules\'+$module+'\'+$module+'.psd1')) -ErrorAction Stop
}
Set-StrictMode -Version Latest
$utf8=New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding=$utf8
function Quote([string]$s){
 if($s.Contains('"') -or $s.IndexOf([char]0) -ge 0 -or $s.Contains("`r") -or $s.Contains("`n")){throw 'Invalid argument.'}
 return '"'+$s.TrimEnd('\')+'"'
}
function ResultDestination([string]$path){
 $p=[IO.Path]::GetFullPath($path);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(-not $p.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)){throw 'Result must be in the current user temporary folder.'}
 $tail=$p.Substring($temp.Length)
 if($tail -notmatch '^F1RU-Speech-[a-fA-F0-9]{32}\\result\.json$'){throw 'Invalid result filename.'}
 $dir=[IO.Path]::GetDirectoryName($p)
 if(-not [IO.Directory]::Exists($dir)){throw 'Result folder is missing.'}
 $cursor=$p
 while($cursor){if(Test-Path -LiteralPath $cursor){if(([IO.File]::GetAttributes($cursor)-band[IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Reparse destination rejected.'}};$cursor=[IO.Path]::GetDirectoryName($cursor)}
 return $p
}
$destination=ResultDestination $ResultPath
$exit=1
try{
 $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $script=Join-Path $PSScriptRoot 'SpeechEngine.ps1'
 $extra=' -Action '+$Action+' -GamePath '+(Quote $GamePath)+' -PackageRoot '+(Quote $PSScriptRoot)
 if($Action -eq 'model'){$script=Join-Path $PSScriptRoot 'ModelBootstrap.ps1';$extra=' -Action install'}
 if($Action -eq 'migrate'){$extra+=' -LegacyState '+(Quote $LegacyState)}
 $si=New-Object Diagnostics.ProcessStartInfo
 $si.FileName=$ps;$si.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File '+(Quote $script)+$extra
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true;$si.StandardOutputEncoding=$utf8;$si.StandardErrorEncoding=$utf8
 $process=New-Object Diagnostics.Process;$process.StartInfo=$si
 try{
  if(-not $process.Start()){throw 'Speech process did not start.'}
  $errorTask=$process.StandardError.ReadToEndAsync();$text=$process.StandardOutput.ReadToEnd();$process.WaitForExit();$err=$errorTask.Result;$exit=$process.ExitCode
 }finally{$process.Dispose()}
 if($text.Length -gt 131072 -or [string]::IsNullOrWhiteSpace($text)){throw ('Speech engine returned no valid response. '+$err)}
 $j=$text|ConvertFrom-Json
 if(-not ($j.PSObject.Properties.Name -contains 'status') -or -not ($j.PSObject.Properties.Name -contains 'message')){throw 'Invalid speech response.'}
}catch{
 $exit=1;$j=[ordered]@{status='error';code='host_error';message=$_.Exception.Message;modelInstalled=$false;installed=$false;needsRestart=$false;gameRunning=$false;canInstall=$false;canRestore=$false;commandCount=37;details=$null}
}
[IO.File]::WriteAllText($destination,($j|ConvertTo-Json -Depth 24 -Compress),$utf8)
exit $exit
