[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$Output,[switch]$ExpectLegacy)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$checks=0;$events=New-Object 'System.Collections.Generic.List[object]'
function Check([bool]$Ok,[string]$Label){$script:checks++;$events.Add([ordered]@{label=$Label;pass=$Ok});if(-not $Ok){throw $Label}}
function Rejects([scriptblock]$Body){try{& $Body;return $false}catch{return $true}}
$source=Join-Path $SourceRoot 'audio\MaxVoice.ps1';$tokens=$null;$errors=$null
$tree=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
try{
 Check ($errors.Count-eq 0) 'production Max script parses'
 foreach($name in @('Need','GameClosed')){
  $f=@($tree.FindAll({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq$name},$true))
  Check ($f.Count-eq 1) ('production function '+$name);. ([scriptblock]::Create($f[0].Extent.Text))
 }
 $detector=@($tree.FindAll({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq'Get-RunningGameProcesses'},$true))
 if($detector.Count-eq 1){. ([scriptblock]::Create($detector[0].Extent.Text))}
 $fixture=$false;$script:fakeNames=@();$script:enumerationFailure=$false
 function Get-Process {[CmdletBinding()]param();if($script:enumerationFailure){Write-Error 'test enumeration denied';return};foreach($n in $script:fakeNames){[pscustomobject]@{ProcessName=$n}}}
 foreach($n in @('F1_25_RU_v0.33','F1_25_RU_v0.33.1','F1_25_launcher','F1_250','F1_25_dx12_backup','Launcher')){
  $script:fakeNames=@($n);$blocked=Rejects {GameClosed}
  if($ExpectLegacy){Check ($blocked-eq($n-ne'Launcher')) ('legacy false game classification: '+$n)}
  else{Check (-not$blocked) ('launcher is not the game: '+$n)}
 }
 foreach($n in @('F1_25','f1_25','F1_25_dx12','f1_25_DX12')){$script:fakeNames=@($n);Check (Rejects {GameClosed}) ('real game remains blocked: '+$n)}
 $script:fakeNames=@();$script:enumerationFailure=$true
 if($ExpectLegacy){Check (-not(Rejects {GameClosed})) 'legacy enumeration failure is ignored'}else{Check (Rejects {GameClosed}) 'enumeration error fails closed'}
 $script:enumerationFailure=$false
 $calls=@($tree.FindAll({param($n)$n-is[Management.Automation.Language.InvokeMemberExpressionAst]-and$n.Member.Value-eq'IsInRole'},$true))
 Check ($calls.Count-eq 1) 'one production privilege predicate';$call=$calls[0];$predicate=[scriptblock]::Create($call.Extent.Text)
 Add-Type -TypeDefinition @'
using System.Security.Principal;
public sealed class MaxPrincipalSpy {
 public bool Elevated; public string Overload;
 public bool IsInRole(string role) { Overload="string"; return false; }
 public bool IsInRole(WindowsBuiltInRole role) { Overload="WindowsBuiltInRole"; return Elevated && role==WindowsBuiltInRole.Administrator; }
}
'@
 $principal=New-Object MaxPrincipalSpy;$principal.Elevated=$true;$elevated=[bool](& $predicate)
 if($ExpectLegacy){Check (-not$elevated-and$principal.Overload-eq'string') 'legacy elevated token falsely rejected by group name'}else{Check ($elevated-and$principal.Overload-eq'WindowsBuiltInRole') 'built-in SID role accepts elevated identity independent of language'}
 $principal.Elevated=$false;Check (-not[bool](& $predicate)) 'non-elevated identity remains blocked'
 $guard=$call.Parent;while($null-ne$guard-and$guard-isnot[Management.Automation.Language.CommandAst]){$guard=$guard.Parent}
 Check ($guard.GetCommandName()-eq'Need') 'privilege predicate feeds real production rejection guard'
 $guardBody=[scriptblock]::Create($guard.Extent.Text);Check (Rejects $guardBody) 'real guard rejects non-elevated fixture identity'
 $principal.Elevated=$true;if($ExpectLegacy){Check (Rejects $guardBody) 'legacy production guard rejects elevated identity'}else{Check (-not(Rejects $guardBody)) 'fixed production guard accepts elevated identity'}
 $ancestors=@();$node=$guard.Parent;while($null-ne$node){if($node-is[Management.Automation.Language.IfStatementAst]){$ancestors+=,$node};$node=$node.Parent}
 Check ($ancestors.Count-ge2) 'role predicate has fixture and mutation gates'
 Check ($ancestors[0].Clauses[0].Item1.Extent.Text-eq'-not $fixture') 'production role guard is never skipped outside fixture'
 Check ($ancestors[1].Clauses[0].Item1.Extent.Text-eq"`$Action -in @('install','restore')") 'only explicit install and restore require elevation'
 if(-not$ExpectLegacy){
  Check ($detector.Count-eq1) 'one shared exact process detector'
  $all=@($tree.FindAll({param($n)$n-is[Management.Automation.Language.CommandAst]-and$n.GetCommandName()-eq'Get-RunningGameProcesses'},$true));Check ($all.Count-eq2) 'status and write preflight share detector'
  Check ([IO.File]::ReadAllText($source).Contains('INERT MAX VOICE TEST FIXTURE - NEVER EXECUTE')) 'fixture privilege skip requires inert executable marker'
 }
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent();try{$principal=New-Object Security.Principal.WindowsPrincipal($identity);$actual=[bool](& $predicate);$sid=$principal.IsInRole((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')));if(-not$ExpectLegacy){Check ($actual-eq$sid) 'actual token matches built-in Administrators SID'}}finally{$identity.Dispose()}
 $phase=if($ExpectLegacy){'BASELINE'}else{'MODIFIED'}
 $literal='MAX_REGRESSION_'+$phase+'_PASS checks='+$checks+' no_uac_or_game_writes=true'
 $report=[ordered]@{pass=$true;phase=$phase;source=$source;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();checks=$checks;events=$events;literal=$literal}
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($report|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
 Write-Output $literal
}catch{Write-Output ('MAX_REGRESSION_FAIL '+$_.Exception.Message);exit 1}
finally{if(Test-Path Function:\Get-Process){Remove-Item Function:\Get-Process}}
