[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
foreach($m in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management')){Import-Module (Join-Path $PSHOME ('Modules\'+$m+'\'+$m+'.psd1')) -ErrorAction Stop}
$Source=[IO.Path]::GetFullPath($Source);$Output=[IO.Path]::GetFullPath($Output)
if([IO.Directory]::Exists($Output)){throw 'Preserve existing test evidence.'}
[void][IO.Directory]::CreateDirectory($Output)
$utf8=New-Object Text.UTF8Encoding($false)
$checks=0;$records=@()
function CallHost([bool]$Existing){
 $id=[guid]::NewGuid().ToString('N');$dir=Join-Path $Output ('F1RU-Speech-'+$id);[void][IO.Directory]::CreateDirectory($dir)
 $result=Join-Path $dir 'result.json'
 if($Existing){[IO.File]::WriteAllText($result,'PRESERVE_EXISTING_RESULT',$utf8)}
 $si=New-Object Diagnostics.ProcessStartInfo
 $si.FileName=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $si.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $Source 'speech\SpeechHost.ps1')+'" -Action status -GamePath "'+(Join-Path $Output 'missing-game')+'" -ResultPath "'+$result+'"'
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardError=$true;$si.RedirectStandardOutput=$true
 $si.EnvironmentVariables['TEMP']=$Output;$si.EnvironmentVariables['TMP']=$Output
 $p=New-Object Diagnostics.Process;$p.StartInfo=$si
 try{[void]$p.Start();$err=$p.StandardError.ReadToEndAsync();$stdout=$p.StandardOutput.ReadToEnd();$p.WaitForExit();return [pscustomobject]@{exit=$p.ExitCode;result=$result;text=[IO.File]::ReadAllText($result,$utf8);stdout=$stdout;stderr=$err.Result}}finally{$p.Dispose()}
}
try{
 $r=CallHost $true;$records+=$r
 if($r.exit -ne 1 -or $r.text -cne 'PRESERVE_EXISTING_RESULT'){throw 'preexisting_result_overwritten_or_not_rejected'};$checks++
 $r=CallHost $false;$records+=$r
 $j=$r.text|ConvertFrom-Json
 if($r.exit -ne 1 -or $j.status -cne 'error' -or $j.code -cne 'operation_failed'){throw 'missing_game_status_not_preserved'};$checks++
 if([IO.Directory]::Exists((Join-Path $Output 'missing-game'))){throw 'status_created_game_path'};$checks++
 [IO.File]::WriteAllText((Join-Path $Output 'RESULTS.json'),($records|ConvertTo-Json -Depth 8),$utf8)
 Write-Output ('SPEECH_HOST_IPC_PASS checks='+$checks+' preexisting_result=preserved missing_game_exit=1 game_writes=0 registry_writes=0');exit 0
}catch{
 [IO.File]::WriteAllText((Join-Path $Output 'RESULTS.json'),($records|ConvertTo-Json -Depth 8),$utf8)
 Write-Output ('SPEECH_HOST_IPC_FAIL '+$_.Exception.Message);exit 1
}
