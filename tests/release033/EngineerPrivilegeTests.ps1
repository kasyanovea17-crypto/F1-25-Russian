[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$SourceRoot,
 [Parameter(Mandatory=$true)][string]$Output,
 [switch]$ExpectLegacy
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$checks=0;$events=New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$Ok,[string]$Label){if(-not $Ok){throw $Label};$script:checks++;$events.Add($Label)}
function DidReject([scriptblock]$Body){try{& $Body;return $false}catch{return $true}}
$source=Join-Path $SourceRoot 'audio\engineer\EngineerVoice.ps1'
$sourceText=[IO.File]::ReadAllText($source,[Text.Encoding]::UTF8)
$tokens=$null;$errors=$null;$tree=[Management.Automation.Language.Parser]::ParseInput($sourceText,[ref]$tokens,[ref]$errors)
Check ($errors.Count-eq 0) 'production script parses'
$need=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.FunctionDefinitionAst] -and $n.Name-eq 'Need'},$true))
Check ($need.Count-eq 1) 'one production Need guard'
. ([scriptblock]::Create($need[0].Extent.Text))
$calls=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Value-eq 'IsInRole'},$true))
Check ($calls.Count-eq 1) 'one production role predicate'
$call=$calls[0]
Check ($call.Expression-is[Management.Automation.Language.VariableExpressionAst] -and $call.Expression.VariablePath.UserPath-eq 'principal') 'role predicate uses principal object'
Check ($call.Arguments.Count-eq 1) 'role predicate has one argument'
$roleTest=[scriptblock]::Create($call.Extent.Text)
$guard=$call.Parent
while($null-ne $guard -and $guard-isnot[Management.Automation.Language.CommandAst]){$guard=$guard.Parent}
Check ($null-ne $guard -and $guard.GetCommandName()-eq 'Need') 'role predicate feeds production rejection guard'
$guardTest=[scriptblock]::Create($guard.Extent.Text)
Add-Type -TypeDefinition @'
using System.Security.Principal;
public sealed class EngineerPrincipalSpy {
 public bool Elevated;
 public string Overload;
 public string StringRole;
 public WindowsBuiltInRole EnumRole;
 public bool IsInRole(string role) {
  Overload="string";StringRole=role;
  // No group named Administrator is present in this controlled identity.
  return false;
 }
 public bool IsInRole(WindowsBuiltInRole role) {
  Overload="WindowsBuiltInRole";EnumRole=role;
  return Elevated && role==WindowsBuiltInRole.Administrator;
 }
}
'@
$principal=New-Object EngineerPrincipalSpy
$principal.Elevated=$true
$elevatedResult=& $roleTest
$elevatedOverload=$principal.Overload
if($ExpectLegacy){
 Check ($elevatedOverload-eq 'string') 'legacy predicate selects string overload'
 Check ($principal.StringRole-ceq 'Administrator') 'legacy argument is literal group name'
 Check (-not $elevatedResult) 'legacy elevated identity is falsely rejected'
 Check (DidReject $guardTest) 'legacy production Need rejects elevated identity'
}else{
 Check ($elevatedOverload-eq 'WindowsBuiltInRole') 'fixed predicate selects enum overload'
 Check ($principal.EnumRole-eq [Security.Principal.WindowsBuiltInRole]::Administrator) 'fixed predicate checks built-in Administrator role'
 Check ([bool]$elevatedResult) 'fixed elevated identity passes'
 Check (-not (DidReject $guardTest)) 'fixed production Need permits elevated identity'
}
$principal.Elevated=$false
$unelevatedResult=& $roleTest
Check (-not $unelevatedResult) 'non-elevated identity remains rejected'
Check (DidReject $guardTest) 'production Need blocks non-elevated mutation'

$ancestors=New-Object 'System.Collections.Generic.List[object]'
$node=$guard.Parent
while($null-ne $node){if($node-is[Management.Automation.Language.IfStatementAst]){$ancestors.Add($node)};$node=$node.Parent}
Check ($ancestors.Count-ge 2) 'privilege guard is nested inside fixture and mutation gates'
Check ($ancestors[0].Clauses[0].Item1.Extent.Text-eq '-not $fixture') 'only inert fixture skips role predicate'
Check ($ancestors[1].Clauses[0].Item1.Extent.Text-eq '$mutate') 'only mutations require elevation'
$mutationBlock=$ancestors[1].Clauses[0].Item2
$gameClosedCalls=@($mutationBlock.FindAll({param($n) $n-is[Management.Automation.Language.CommandAst] -and $n.GetCommandName()-eq 'GameClosed'},$true))
Check ($gameClosedCalls.Count-eq 1 -and $gameClosedCalls[0].Extent.StartOffset-lt $guard.Extent.StartOffset) 'running-game guard precedes role predicate'
$createCalls=@($mutationBlock.FindAll({param($n) $n-is[Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Value-eq 'CreateDirectory'},$true))
Check ($createCalls.Count-eq 1 -and $guard.Extent.EndOffset-lt $createCalls[0].Extent.StartOffset) 'role predicate precedes state directory write'
$assignments=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text-eq '$mutate'},$true))
Check ($assignments.Count-eq 1) 'one production mutation classification'
$classification=[scriptblock]::Create($assignments[0].Extent.Text)
foreach($Action in @('russian','english','restore','status','verify','unknown','RUSSIAN')){
 . $classification
 Check ($mutate-eq ($Action-cin @('russian','english','restore'))) ('production mutation classification: '+$Action)
}
$marker=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.BinaryExpressionAst] -and $n.Extent.Text.Contains('INERT ENGINEER VOICE TEST FIXTURE - NEVER EXECUTE')},$true))
Check ($marker.Count-eq 1 -and $marker[0].Operator-eq [Management.Automation.Language.TokenKind]::Ceq) 'fixture requires exact case-sensitive inert marker'
Check ($marker[0].Right.Value-ceq "INERT ENGINEER VOICE TEST FIXTURE - NEVER EXECUTE`n") 'fixture marker is not a real executable'
Check ($marker[0].Extent.EndOffset-lt $guard.Extent.StartOffset) 'fixture safeguard precedes privilege bypass'
$containment=@($tree.FindAll({param($n) $n-is[Management.Automation.Language.CommandAst] -and $n.GetCommandName()-eq 'Within' -and $n.Extent.Text-eq 'Within $game $FixtureRoot'},$true))
Check ($containment.Count-eq 1 -and $containment[0].Extent.EndOffset-lt $marker[0].Extent.StartOffset) 'fixture path containment precedes marker check'

$uiPath=Join-Path $SourceRoot 'EngineerVoiceUI.cs'
$ui=[IO.File]::ReadAllText($uiPath,[Text.Encoding]::UTF8)
$method=[regex]::Match($ui,'internal static bool Mutation\(string a\)\{[^}]+\}')
Check $method.Success 'production UI mutation method extracted'
$settings=[regex]::Match($ui,'new ProcessStartInfo\(ps,Arguments\(host,action,game,result\)\)\{(?<settings>[^}]+)\};(?<elevation>if\(Mutation\(action\)\)start\.Verb="runas";)')
Check $settings.Success 'production process settings and runas branch extracted'
$launchSource='using System.Diagnostics;public static class EngineerLaunchSettingsSpy {'+$method.Value+' public static ProcessStartInfo Settings(string action){var start=new ProcessStartInfo{'+$settings.Groups['settings'].Value+'};'+$settings.Groups['elevation'].Value+'return start;}}'
Add-Type -TypeDefinition $launchSource
foreach($action in @('russian','english','restore','status','verify')){
 $settingsResult=[EngineerLaunchSettingsSpy]::Settings($action)
 $expected=$action-cin @('russian','english','restore')
 Check ($settingsResult.UseShellExecute-eq $expected) ('UI shell execution only for mutation: '+$action)
 Check ($(if($expected){$settingsResult.Verb-ceq 'runas'}else{[string]::IsNullOrEmpty($settingsResult.Verb)})) ('UI elevation verb: '+$action)
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try{
 $principal=New-Object Security.Principal.WindowsPrincipal($identity)
 $actualTokenResult=[bool](& $roleTest)
 $actualSidResult=$principal.IsInRole((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
 $actualEnumResult=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
 $actualStringResult=$principal.IsInRole('Administrator')
 Check ($actualEnumResult-eq $actualSidResult) 'actual token enum matches built-in Administrators SID'
 if($ExpectLegacy){Check ($actualTokenResult-eq $actualStringResult) 'actual legacy production predicate matches string lookup'}
 else{Check ($actualTokenResult-eq $actualSidResult) 'actual fixed production predicate matches built-in Administrators SID'}
}finally{$identity.Dispose()}
$mode=if($ExpectLegacy){'BASELINE'}else{'MODIFIED'}
$literal=if($ExpectLegacy){'ENGINEER_PRIVILEGE_BASELINE_PASS checks='+$checks+' overload=string elevated_rejected=true non_elevated_rejected=true no_uac_or_game_writes=true'}else{'ENGINEER_PRIVILEGE_PASS checks='+$checks+' overload=WindowsBuiltInRole elevated_accepted=true non_elevated_rejected=true no_uac_or_game_writes=true'}
$result=[ordered]@{status='PASS';phase=$mode;checks=$checks;source=$source;sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();productionPredicate=$call.Extent.Text;elevatedResult=[bool]$elevatedResult;elevatedOverload=$elevatedOverload;nonElevatedResult=[bool]$unelevatedResult;actualToken=[ordered]@{production=$actualTokenResult;builtinSid=$actualSidResult;enum=$actualEnumResult;string=$actualStringResult};uacInvoked=$false;gameWrites=$false;networkCalls=$false;literalOutput=$literal;tests=$events}
$fullOutput=[IO.Path]::GetFullPath($Output);[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput))
[IO.File]::WriteAllText($fullOutput,($result|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
Write-Output $literal
