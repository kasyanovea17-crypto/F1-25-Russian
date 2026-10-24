[CmdletBinding()]
param(
 [ValidateSet('status','verify','install','restore')][string]$Action='status',
 [Parameter(Mandatory=$true)][string]$GamePath,
 [string]$PackageRoot=$PSScriptRoot,
 [string]$FixtureRoot,
 [ValidateSet('None','AfterJournal','AfterStage','AfterReplace')][string]$FixtureFailPoint='None'
)
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$utf8=New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding=$utf8
$fullHash='b3202523d49c3cd173a7dd4172b78f071fd5e0b9967bdb59cbcaae9719dd50a5'
$originalHash='af47405ce900a44cc9be72f3cc97868f0c3dc179e0d14127087cb106119ba772'
$previousHash='bf42b070e1277f50646a627a72e5845b672e257863eee38e311ffd705ebe163f'
$manifestHash='24353455483c192129a8cc6b9b6238d481da901d5237b9d16ca6c9d6b789e77b'
$literalHash='e26fb80e9ee24c335f2bdb1197d49c1104519670f56a619e7fe06e39446bca16'
$fixture=-not [string]::IsNullOrWhiteSpace($FixtureRoot)
$lock=$null;$state=$null;$running=$false;$current=$null

function Need([bool]$Ok,[string]$Message){if(-not $Ok){throw $Message}}
function HashBytes([byte[]]$Bytes){$h=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($h.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$h.Dispose()}}
function Hash([string]$Path){$h=[Security.Cryptography.SHA256]::Create();$s=[IO.File]::OpenRead($Path);try{return ([BitConverter]::ToString($h.ComputeHash($s))).Replace('-','').ToLowerInvariant()}finally{$s.Dispose();$h.Dispose()}}
function Checked([string]$Path){
 Need ([IO.Path]::IsPathRooted($Path)) 'An absolute local path is required.'
 $p=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 Need (-not $p.StartsWith('\\') -and -not $p.Substring(2).Contains(':')) 'A local drive path without alternate streams is required.'
 $cursor=$p
 while($cursor){
  if([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)){Need (([IO.File]::GetAttributes($cursor)-band [IO.FileAttributes]::ReparsePoint)-eq 0) ('Linked path rejected: '+$cursor)}
  $parent=[IO.Path]::GetDirectoryName($cursor);if($parent -eq $cursor){break};$cursor=$parent
 }
 return $p
}
function Within([string]$Child,[string]$Parent){return $Child.StartsWith($Parent.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)}
function ReadJson([string]$Path){[void](Checked $Path);Need ((Get-Item -LiteralPath $Path).Length -le 262144) 'JSON exceeds its bound.';return ([IO.File]::ReadAllText($Path,$utf8)|ConvertFrom-Json)}
function AtomicJson($Value,[string]$Path){
 [void](Checked $Path);$temp=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp';$previous=$temp+'.previous'
 try{
  [IO.File]::WriteAllText($temp,($Value|ConvertTo-Json -Depth 16 -Compress),$utf8)
  if([IO.File]::Exists($Path)){[IO.File]::Replace($temp,$Path,$previous);[IO.File]::Delete($previous)}else{[IO.File]::Move($temp,$Path)}
 }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Result([string]$Status,[string]$Code,[string]$Message,[int]$Exit,[bool]$Installed=$false,[bool]$CanInstall=$false,[bool]$CanRestore=$false){
 $backup=$null;if($null -ne $state){$backup=BackupPath $state}
 [ordered]@{schema=1;status=$Status;code=$Code;message=$Message;installed=$Installed;canInstall=$CanInstall;canRestore=$CanRestore;gameRunning=$running;testOnly=$true;maxSlots=192;pronunciationReview=18;gameplayTested=$false;backup=$backup;targetHash=$current;expectedHash=$fullHash}|ConvertTo-Json -Depth 6 -Compress
 exit $Exit
}
function GameClosed{
 if($fixture){Need (-not [IO.File]::Exists((Join-Path $FixtureRoot 'game.running'))) 'F1 25 is running.';return}
 Need (@(Get-Process -ErrorAction SilentlyContinue|Where-Object {$_.ProcessName -like 'F1_25*'}).Count -eq 0) 'Close F1 25 before changing its audio.'
}
function BackupPath($S){return Checked (Join-Path (Join-Path (Join-Path $installDir 'transactions') $S.transaction) 'original.nfs')}
function LoadState{
 if(-not [IO.File]::Exists($stateFile)){return $null}
 $s=ReadJson $stateFile
 Need ($s.schema -eq 1 -and $s.identity -ceq $identity -and $s.gamePath -ieq $game -and $s.transaction -match '^[0-9a-f]{32}$') 'Saved audio installation identity differs.'
 Need ($s.targetRelative -ceq 'audio/2025/speech/en_drivers.nfs' -and $s.afterHash -ceq $fullHash -and $s.beforeHash -in @($originalHash,$previousHash)) 'Saved audio hash contract differs.'
 Need ($s.status -in @('PREPARED','APPLIED','RESTORING','RESTORED','ROLLBACK_REQUIRED')) 'Saved audio state is invalid.'
 return $s
}
function VerifyBackup($S){$p=BackupPath $S;Need ([IO.File]::Exists($p) -and (Hash $p)-ceq $S.beforeHash) 'The saved previous audio bank is missing or changed.'}
function Swap([string]$Source,[string]$ExpectedCurrent,[string]$ExpectedNew){
 GameClosed;Need ((Hash $target)-ceq $ExpectedCurrent) 'The installed audio bank changed.'
 Need ((Hash $Source)-ceq $ExpectedNew) 'The restore source changed.'
 $temp=$target+'.'+[guid]::NewGuid().ToString('N')+'.max.tmp';$old=$temp+'.previous'
 try{
  [IO.File]::Copy($Source,$temp,$false);Need ((Hash $temp)-ceq $ExpectedNew) 'Staged audio verification failed.'
  GameClosed;Need ((Hash $target)-ceq $ExpectedCurrent) 'The destination changed during staging.'
  [IO.File]::Replace($temp,$target,$old)
  Need ((Hash $target)-ceq $ExpectedNew) 'Installed audio verification failed.'
  [IO.File]::Delete($old)
 }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function RestoreState($S){
 VerifyBackup $S;$h=Hash $target;Need ($h -in @($S.beforeHash,$fullHash)) 'The audio bank was changed externally; restore stopped.'
 $S.status='RESTORING';AtomicJson $S $stateFile
 if($h -cne $S.beforeHash){Swap (BackupPath $S) $h $S.beforeHash}
 Need ((Hash $target)-ceq $S.beforeHash) 'Previous audio bank was not restored.'
 $S.status='RESTORED';AtomicJson $S $stateFile
}
function Whole([object]$N,[string]$Label){Need ($N -is [int] -or $N -is [long]) ('Integer required: '+$Label);return [long]$N}
function ReadPatch([string]$InputHash){
 $p=Checked (Join-Path $PackageRoot 'payload\max-ru-test.json');Need ((Hash $p)-ceq $manifestHash) 'Audio patch manifest checksum differs.'
 $m=ReadJson $p
 Need ($m.schema -eq 1 -and $m.target -ceq 'audio/2025/speech/en_drivers.nfs' -and $m.output_sha256 -ceq $fullHash -and $m.output_bytes -eq 65471820) 'Audio patch output contract differs.'
 Need ($m.literal_file -ceq 'max-ru-test.delta' -and $m.literal_sha256 -ceq $literalHash -and $m.literal_bytes -eq 3665934) 'Literal patch contract differs.'
 Need ($m.max_slots -eq 192 -and $m.unchanged_non_max_slots -eq 2646 -and @($m.inputs).Count -eq 2) 'Audio patch scope differs.'
 $literal=Checked (Join-Path $PackageRoot 'payload\max-ru-test.delta')
 Need ([IO.File]::Exists($literal) -and (Get-Item -LiteralPath $literal).Length -eq 3665934 -and (Hash $literal)-ceq $literalHash) 'Audio literal data is missing or changed.'
 $input=@($m.inputs|Where-Object {$_.sha256 -ceq $InputHash});Need ($input.Count -eq 1) 'No patch for this exact driver bank.'
 $input=$input[0];Need ((Get-Item -LiteralPath $target).Length -eq $input.bytes) 'Input bank size differs.'
 $sum=0L;Need (@($input.ops).Count -ge 1 -and @($input.ops).Count -le 2048) 'Patch operation count rejected.'
 foreach($op in $input.ops){
  Need ($op.kind -in @('copy','literal')) 'Unknown patch operation.'
  $offset=Whole $op.offset 'offset';$length=Whole $op.length 'length'
  $limit=if($op.kind -ceq 'copy'){[long]$input.bytes}else{[long]$m.literal_bytes}
  Need ($offset -ge 0 -and $length -gt 0 -and $offset -le $limit -and $length -le $limit-$offset) 'Patch range rejected.'
  $sum+=$length;Need ($sum -le [long]$m.output_bytes) 'Patch output bound exceeded.'
 }
 Need ($sum -eq [long]$m.output_bytes) 'Patch output length differs.'
 return [pscustomobject]@{manifest=$m;input=$input;literal=$literal}
}
function BuildPatch($Patch,[string]$Destination){
 Need (-not [IO.File]::Exists($Destination)) 'Staging path already exists.'
 $source=$null;$literal=$null;$output=$null
 try{
  $source=[IO.File]::Open($target,'Open','Read','Read');$literal=[IO.File]::OpenRead($Patch.literal);$output=[IO.File]::Open($Destination,'CreateNew','Write','None')
  $buffer=New-Object byte[] 1048576
  foreach($op in $Patch.input.ops){
   $stream=if($op.kind -ceq 'copy'){$source}else{$literal};[void]$stream.Seek([long]$op.offset,'Begin');$left=[long]$op.length
   while($left -gt 0){$n=$stream.Read($buffer,0,[int][Math]::Min($left,$buffer.Length));Need ($n -gt 0) 'Unexpected end of audio patch input.';$output.Write($buffer,0,$n);$left-=$n}
  }
  $output.Flush($true)
 }finally{foreach($s in @($output,$literal,$source)){if($null -ne $s){$s.Dispose()}}}
 Need ((Get-Item -LiteralPath $Destination).Length -eq 65471820 -and (Hash $Destination)-ceq $fullHash) 'Reconstructed audio bank differs.'
}
function Fail([string]$Point){if($fixture -and $FixtureFailPoint -ceq $Point){throw ('Fixture failure: '+$Point)}}

try{
 Need ([Environment]::Is64BitProcess) 'Use 64-bit Windows PowerShell.'
 Need ($fixture -or $FixtureFailPoint -ceq 'None') 'Failure injection is available only for isolated fixtures.'
 $game=Checked $GamePath;$PackageRoot=Checked $PackageRoot
 Need ([IO.File]::Exists((Checked (Join-Path $game 'F1_25.exe')))) 'Selected folder does not contain F1_25.exe.'
 $target=Checked (Join-Path $game 'audio\2025\speech\en_drivers.nfs');Need ([IO.File]::Exists($target)) 'English driver audio bank is missing.'
 Need ((Get-Item -LiteralPath $target).Length -gt 256 -and (Get-Item -LiteralPath $target).Length -le 268435456) 'Driver audio size rejected.'
 $identity=HashBytes ($utf8.GetBytes($game.ToUpperInvariant()))
 if($fixture){$FixtureRoot=Checked $FixtureRoot;Need (Within $game $FixtureRoot) 'Fixture game must be inside FixtureRoot.';$stateRoot=Checked (Join-Path $FixtureRoot 'state');$running=[IO.File]::Exists((Join-Path $FixtureRoot 'game.running'))}
 else{$stateRoot=Checked (Join-Path $env:ProgramData 'Karsvein\F1MaxVoice');$running=@(Get-Process -ErrorAction SilentlyContinue|Where-Object {$_.ProcessName -like 'F1_25*'}).Count -gt 0}
 $installDir=Checked (Join-Path (Join-Path $stateRoot 'installations') $identity);$stateFile=Checked (Join-Path $installDir 'state.json')
 if($Action -in @('install','restore')){
  GameClosed
  if(-not $fixture){$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent());Need ($principal.IsInRole('Administrator')) 'Administrator approval is required to change game audio.'}
  [void][IO.Directory]::CreateDirectory($installDir);$lock=[IO.File]::Open((Checked (Join-Path $installDir '.lock')),'OpenOrCreate','ReadWrite','None')
 }
 $state=LoadState;$current=Hash $target
 if($null -ne $state -and $state.status -ne 'RESTORED'){
  VerifyBackup $state
  if($Action -eq 'restore'){RestoreState $state;$current=Hash $target;Result 'restored' 'restored' 'Предыдущая озвучка восстановлена. Перевод и голосовые команды сохранены.' 0}
  if($state.status -ne 'APPLIED'){Result 'recovery_required' 'unfinished_transaction' 'Найдена незавершённая установка. Восстановите сохранённую предыдущую озвучку.' 2 $false $false (-not $running)}
  Need ($current -ceq $fullHash) 'Installed Max audio was changed externally.'
  Result 'installed' 'verified' 'Тестовая русская озвучка Макса установлена: 192 реплики. 18 записей отмечены для проверки произношения.' 0 $true $false (-not $running)
 }
 if($Action -eq 'restore'){
  if($null -ne $state -and $state.status -eq 'RESTORED' -and $current -ceq $state.beforeHash){Result 'restored' 'already_restored' 'Предыдущая озвучка уже восстановлена.' 0}
  Result 'not_ready' 'no_owned_backup' 'Этот модуль ещё не устанавливал озвучку; сохранённой им резервной копии нет.' 2
 }
 if($current -ceq $fullHash){Result 'installed_external' 'known_existing_pack' 'Этот тестовый пакет уже установлен ранее. Его исходная резервная копия остаётся у предыдущего установщика.' 0 $true}
 if($current -notin @($originalHash,$previousHash)){Result 'not_ready' 'unknown_audio_bank' 'Обнаружена другая версия или изменённый аудиобанк. Файлы сохранены без изменений.' 2}
 $patch=ReadPatch $current
 if($Action -eq 'verify'){Result 'not_ready' 'not_installed' 'Пакет проверен, но ещё не установлен.' 2 $false (-not $running)}
 if($Action -eq 'status'){Result 'ready' 'ready' 'Доступна тестовая русская озвучка Макса: 192 реплики. Предыдущая озвучка будет сохранена.' 0 $false (-not $running)}
 $transaction=[guid]::NewGuid().ToString('N');$directory=Checked (Join-Path (Join-Path $installDir 'transactions') $transaction);[void][IO.Directory]::CreateDirectory($directory)
 $state=[pscustomobject]@{schema=1;identity=$identity;gamePath=$game;transaction=$transaction;status='PREPARED';createdUtc=[DateTime]::UtcNow.ToString('o');targetRelative='audio/2025/speech/en_drivers.nfs';beforeHash=$current;afterHash=$fullHash;maxSlots=192;testOnly=$true;gameplayTested=$false}
 $backup=BackupPath $state;[IO.File]::Copy($target,$backup,$false);VerifyBackup $state;Need ((Hash $target)-ceq $current) 'Audio source changed while saving its backup.'
 AtomicJson $state $stateFile
 $stage=$target+'.'+$transaction+'.max.tmp';$old=$stage+'.previous'
 try{
  Fail 'AfterJournal';BuildPatch $patch $stage;Fail 'AfterStage'
  GameClosed;Need ((Hash $target)-ceq $current) 'Audio destination changed during reconstruction.'
  [IO.File]::Replace($stage,$target,$old);Fail 'AfterReplace'
  Need ((Hash $target)-ceq $fullHash) 'Installed audio checksum differs.'
  $state.status='APPLIED';AtomicJson $state $stateFile
  [IO.File]::Delete($old)
 }catch{
  $reason=$_.Exception.Message
  try{RestoreState $state}catch{$state.status='ROLLBACK_REQUIRED';AtomicJson $state $stateFile;throw ($reason+'; recovery requires attention: '+$_.Exception.Message)}
  throw ('Installation stopped and previous audio restored: '+$reason)
 }finally{if([IO.File]::Exists($stage)){[IO.File]::Delete($stage)};if([IO.File]::Exists($old) -and (Hash $old)-ceq $state.beforeHash){[IO.File]::Delete($old)}}
 $current=Hash $target;Result 'installed' 'installed' 'Тестовая русская озвучка Макса установлена: 192 реплики. Предыдущая версия сохранена; доступно восстановление.' 0 $true $false $true
}catch{
 Result 'error' 'operation_failed' $_.Exception.Message 1
}finally{if($null -ne $lock){$lock.Dispose()}}
