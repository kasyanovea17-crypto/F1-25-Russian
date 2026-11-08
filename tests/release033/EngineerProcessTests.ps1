[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$checks=0;$events=New-Object 'System.Collections.Generic.List[string]'
$owned=@();$live=@();$fixture=$false
function Check([bool]$Ok,[string]$Label){if(-not $Ok){throw $Label};$script:checks++;$events.Add($Label)}
function Rejected([scriptblock]$Body,[string]$Label){$rejected=$false;try{& $Body}catch{$rejected=$true};Check $rejected $Label}
$source=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'audio\engineer\EngineerVoice.ps1'
$tokens=$null;$errors=$null;$tree=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
Check ($errors.Count-eq 0) 'production script parses'
foreach($name in @('Need','Get-RunningGameProcesses','GameClosed')){
 $found=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.FunctionDefinitionAst] -and $n.Name-eq $name},$true))
 Check ($found.Count-eq 1) ('one production function '+$name)
 . ([scriptblock]::Create($found[0].Extent.Text))
}
$calls=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.CommandAst] -and $n.GetCommandName()-eq 'Get-RunningGameProcesses'},$true))
Check ($calls.Count-eq 2) 'status and mutation share the same process detector'
$enumerations=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.CommandAst] -and $n.GetCommandName()-eq 'Get-Process'},$true))
Check ($enumerations.Count-eq 1 -and $enumerations[0].Extent.Text-match '-ErrorAction Stop') 'process enumeration errors propagate'
Check (-not ([IO.File]::ReadAllText($source).Contains("-like 'F1_25*'"))) 'launcher-prefix wildcard removed'
$script:fakeNames=@();$script:enumerationFailure=$false
function Get-Process {
 [CmdletBinding()]param()
 if($script:enumerationFailure){Write-Error 'test enumeration denied';return}
 foreach($name in $script:fakeNames){[pscustomobject]@{ProcessName=$name}}
}
try{
 foreach($name in @('F1_25','f1_25','F1_25_dx12','f1_25_DX12')){
  $script:fakeNames=@($name)
  Check (@(Get-RunningGameProcesses).Count-eq 1) ('actual game name recognized: '+$name)
  Rejected {GameClosed} ('mutation blocked for '+$name)
 }
 foreach($name in @('F1_25_RU_v0.33','F1_25_RU_v0.33.1','F1_25_launcher','Launcher','F1_250','F1_25.exe','F1_25_dx12_backup')){
  $script:fakeNames=@($name)
  Check (@(Get-RunningGameProcesses).Count-eq 0) ('non-game excluded: '+$name)
  GameClosed;Check $true ('non-game does not block: '+$name)
 }
 $script:fakeNames=@();GameClosed;Check $true 'no processes permits mutation guard'
 $script:fakeNames=@('F1_25_RU_v0.33','F1_25','Launcher')
 Check (@(Get-RunningGameProcesses).Count-eq 1) 'mixed list detects only actual game'
 $script:enumerationFailure=$true
 Rejected {Get-RunningGameProcesses} 'status detection fails closed on enumeration error'
 Rejected {GameClosed} 'mutation guard fails closed on enumeration error'
 $root=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output))) ('process-fixture-'+[guid]::NewGuid().ToString('N'))
 [void][IO.Directory]::CreateDirectory($root);$FixtureRoot=$root;$fixture=$true
 GameClosed;Check $true 'inert fixture without marker permits guard'
 [IO.File]::WriteAllText((Join-Path $root 'game.running'),'inert marker')
 Rejected {GameClosed} 'inert fixture running marker still blocks'
 Remove-Item -LiteralPath (Join-Path $root 'game.running')
 $fixture=$false
 Remove-Item Function:\Get-Process
 $script:enumerationFailure=$false
 # This executable only sleeps; it has no game code, file writes or network calls.
 $src=Join-Path $root 'InertProcess.cs'
 [IO.File]::WriteAllText($src,'class InertProcess { static void Main() { System.Threading.Thread.Sleep(120000); } }')
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $launcher=Join-Path $root 'F1_25_RU_regression.exe'
 & $csc /nologo /target:exe /warn:4 /warnaserror+ ('/out:'+$launcher) $src
 Check ($LASTEXITCODE-eq 0 -and [IO.File]::Exists($launcher)) 'inert process fixture compiles'
 foreach($name in @('F1_25','F1_25_dx12')){[IO.File]::Copy($launcher,(Join-Path $root ($name+'.exe')),$false)}
 $p=Start-Process -FilePath $launcher -PassThru -WindowStyle Hidden;$owned+=,$p
 Start-Sleep -Milliseconds 250
 Check (-not $p.HasExited) 'inert launcher fixture running'
 Check (@(Get-RunningGameProcesses|Where-Object{$_.Id-eq $p.Id}).Count-eq 0) 'live launcher process excluded'
 foreach($name in @('F1_25','F1_25_dx12')){
  $p=Start-Process -FilePath (Join-Path $root ($name+'.exe')) -PassThru -WindowStyle Hidden;$owned+=,$p
  Start-Sleep -Milliseconds 250
  Check (-not $p.HasExited) ('inert '+$name+' fixture running')
  Check (@(Get-RunningGameProcesses|Where-Object{$_.Id-eq $p.Id}).Count-eq 1) ('live '+$name+' detected')
  Rejected {GameClosed} ('live '+$name+' blocks mutation')
  $p.Kill();$p.WaitForExit();$live+=@([ordered]@{name=$name;pid=$p.Id;stopped=$p.HasExited})
 }
 $result=[ordered]@{pass=$true;checks=$checks;source=$source;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();inertProcesses=$live;tests=$events}
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($result|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
 Write-Output ('ENGINEER_PROCESS_PASS checks='+$checks+' launcher_excluded=true actual_game_blocked=true enumeration_error_blocked=true')
}finally{
 if(Test-Path Function:\Get-Process){Remove-Item Function:\Get-Process}
 foreach($p in $owned){try{if(-not $p.HasExited){$p.Kill();$p.WaitForExit()}}finally{$p.Dispose()}}
}
