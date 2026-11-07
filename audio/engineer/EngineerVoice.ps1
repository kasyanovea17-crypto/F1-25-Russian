[CmdletBinding()]
param(
 [ValidateSet('status','verify','russian','english','restore')][string]$Action='status',
 [Parameter(Mandatory=$true)][string]$GamePath,
 [string]$PackageRoot=$PSScriptRoot,
 [string]$FixtureRoot,
 [ValidateSet('None','AfterJournal','AfterStage','AfterFirstReplace','AfterBothReplace')][string]$FixtureFailPoint='None'
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if([string]::IsNullOrWhiteSpace($PackageRoot)){$PackageRoot=Split-Path -Parent $PSCommandPath}
$utf8=New-Object Text.UTF8Encoding($false);[Console]::OutputEncoding=$utf8
$manifestHash='542e5747041528f06fe42a06e025b502ceb3369747c357bc7dfccc0151b65e83'
$literalHash='63258512143ae981dc9a8cea2f689ddee41593013ea18586edfafc58c322258b'
$coreHash='df90ac59993611d2143225f914d2a8674df22a64b695ab18e9f1321908b4ee73'
$bankNames=@('en_engineer1_startup.nfs','en_engineer1.nfs')
$fixture=-not [string]::IsNullOrWhiteSpace($FixtureRoot);$state=$null;$lock=$null;$running=$false;$selection='unknown';$current=@();$txRoot=$null

function Need([bool]$OK,[string]$Message){if(-not $OK){throw $Message}}
function Checked([string]$Path){
 Need ([IO.Path]::IsPathRooted($Path)) 'Нужен полный локальный путь.'
 $p=[IO.Path]::GetFullPath($Path).TrimEnd('\');Need ($p.Length -ge 3 -and -not $p.StartsWith('\\') -and -not $p.Substring(2).Contains(':')) 'Сетевой путь или дополнительный поток не поддерживается.'
 $cursor=$p;while($cursor){if([IO.File]::Exists($cursor)-or[IO.Directory]::Exists($cursor)){Need (([IO.File]::GetAttributes($cursor)-band[IO.FileAttributes]::ReparsePoint)-eq 0) ('Путь является ссылкой: '+$cursor)};$parent=[IO.Path]::GetDirectoryName($cursor);if($parent -eq $cursor){break};$cursor=$parent};return $p
}
function Within([string]$Child,[string]$Parent){return $Child.StartsWith($Parent.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)}
function Hash([string]$Path){[void](Checked $Path);$h=[Security.Cryptography.SHA256]::Create();$s=[IO.File]::OpenRead($Path);try{return ([BitConverter]::ToString($h.ComputeHash($s))).Replace('-','').ToLowerInvariant()}finally{$s.Dispose();$h.Dispose()}}
function HashText([string]$Text){$h=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($h.ComputeHash($utf8.GetBytes($Text)))).Replace('-','').ToLowerInvariant()}finally{$h.Dispose()}}
function ReadJson([string]$Path){[void](Checked $Path);Need ((Get-Item -LiteralPath $Path).Length -le 262144) 'Сохранённое состояние превышает допустимый размер.';return [IO.File]::ReadAllText($Path,$utf8)|ConvertFrom-Json}
function AtomicJson($Value,[string]$Path){
 [void](Checked $Path);$temp=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp';$old=$temp+'.previous'
 try{[IO.File]::WriteAllText($temp,($Value|ConvertTo-Json -Depth 20 -Compress),$utf8);if([IO.File]::Exists($Path)){[IO.File]::Replace($temp,$Path,$old);[IO.File]::Delete($old)}else{[IO.File]::Move($temp,$Path)}}finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function GameClosed{
 if($fixture){Need (-not [IO.File]::Exists((Join-Path $FixtureRoot 'game.running'))) 'Закройте F1 25 перед изменением озвучки.';return}
 Need (@(Get-Process -ErrorAction SilentlyContinue|Where-Object{$_.ProcessName -like 'F1_25*'}).Count -eq 0) 'Закройте F1 25 перед изменением озвучки.'
}
function Target([string]$Name){Need ($Name -cin $bankNames) 'Неизвестный аудиобанк.';return Checked (Join-Path $game ('audio\2025\speech\'+$Name))}
function Backup($S,[string]$Name){Need ($S.transaction -cmatch '^[0-9a-f]{32}$') 'Неверный номер резервной копии.';return Checked (Join-Path (Join-Path (Join-Path $installDir 'transactions') $S.transaction) ('before-'+$Name))}
function Result([string]$Status,[string]$Code,[string]$Message,[int]$Exit,[bool]$CanApply=$false,[bool]$CanRestore=$false){
 $backup=$null;if($null-ne $state){$backup=Checked (Join-Path (Join-Path $installDir 'transactions') $state.transaction)}
 [ordered]@{schema=1;status=$Status;code=$Code;message=$Message;selection=$selection;installed=($selection -ceq 'russian');canApply=$CanApply;canRestore=$CanRestore;gameRunning=$running;engineerSlots=1596;testOnly=$true;gameplayTested=$false;backup=$backup;banks=$current;qualityReview='Короткие пять, семь, девять и составные сводки требуют прослушивания в игре.'}|ConvertTo-Json -Depth 8 -Compress
 exit $Exit
}
function LoadState{
 if(-not [IO.File]::Exists($stateFile)){return $null};$s=ReadJson $stateFile
 Need ($s.schema-eq 1 -and $s.identity-ceq $identity -and $s.gamePath-ieq $game -and $s.transaction-cmatch '^[0-9a-f]{32}$') 'Сохранённая настройка относится к другой папке игры.'
 Need ($s.status -cin @('PREPARED','APPLIED','RESTORING','RESTORED','ROLLBACK_REQUIRED') -and $s.selection -cin @('english','russian') -and @($s.banks).Count-eq 2) 'Неверное сохранённое состояние озвучки.'
 for($i=0;$i-lt 2;$i++){ $b=$s.banks[$i];Need ($b.name-ceq $bankNames[$i] -and $b.beforeHash-cmatch '^[0-9a-f]{64}$' -and $b.afterHash-cmatch '^[0-9a-f]{64}$') 'Привязка аудиобанка в резервной копии изменилась.' }
 return $s
}
function VerifyBackups($S){foreach($b in $S.banks){$p=Backup $S $b.name;Need ([IO.File]::Exists($p)-and(Hash $p)-ceq $b.beforeHash) ('Резервная копия отсутствует или изменена: '+$b.name);[void]([EngineerNefs]::Inspect($p,$b.name))}}
function Swap([string]$Source,[string]$Destination,[string]$OldHash,[string]$NewHash){
 GameClosed;Need ((Hash $Destination)-ceq $OldHash -and (Hash $Source)-ceq $NewHash) 'Аудиофайлы изменились перед заменой.'
 $temp=$Destination+'.'+[guid]::NewGuid().ToString('N')+'.engineer.tmp';$old=$temp+'.previous'
 try{[IO.File]::Copy($Source,$temp,$false);Need ((Hash $temp)-ceq $NewHash) 'Копия аудиобанка не прошла проверку.';GameClosed;Need ((Hash $Destination)-ceq $OldHash) 'Назначение изменилось во время копирования.';[IO.File]::Replace($temp,$Destination,$old);Need ((Hash $Destination)-ceq $NewHash) 'Установленный аудиобанк не прошёл проверку.';[IO.File]::Delete($old)}finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function RestoreState($S){
 VerifyBackups $S
 foreach($b in $S.banks){Need ((Hash (Target $b.name))-cin @($b.beforeHash,$b.afterHash)) 'Аудиобанк изменён другим модом после настройки; автоматический откат остановлен.'}
 $S.status='RESTORING';AtomicJson $S $stateFile
 foreach($b in @($S.banks)[1..0]){$target=Target $b.name;$hash=Hash $target;if($hash-cne $b.beforeHash){Swap (Backup $S $b.name) $target $hash $b.beforeHash}}
 foreach($b in $S.banks){Need ((Hash (Target $b.name))-ceq $b.beforeHash) 'Предыдущая настройка озвучки восстановлена не полностью.'}
 $S.status='RESTORED';AtomicJson $S $stateFile
}
function Analyze{
 $script:current=@();$en=0;$ru=0
 foreach($name in $bankNames){$a=[EngineerNefs]::Inspect((Target $name),$name);$script:current+=@([ordered]@{name=$name;sha256=$a.Hash;selected=$a.Selected;english=$a.English;russian=$a.Russian});$en+=$a.English;$ru+=$a.Russian}
 $script:selection=if($en-eq 1596){'english'}elseif($ru-eq 1596){'russian'}else{'mixed'}
}
function Fail([string]$Point){if($fixture-and$FixtureFailPoint-ceq $Point){throw ('Проверочная остановка: '+$Point)}}

try{
 Need ([Environment]::Is64BitProcess) 'Запустите 64-разрядный Windows PowerShell.'
 Need ($fixture-or$FixtureFailPoint-ceq 'None') 'Проверочные остановки доступны только на отдельных копиях.'
 $game=Checked $GamePath;$PackageRoot=Checked $PackageRoot;Need ([IO.File]::Exists((Checked (Join-Path $game 'F1_25.exe')))) 'Выбранная папка не содержит F1_25.exe.'
 foreach($name in $bankNames){Need ([IO.File]::Exists((Target $name))) ('Не найден английский аудиобанк инженера: '+$name)}
 $identity=HashText $game.ToUpperInvariant()
 if($fixture){$FixtureRoot=Checked $FixtureRoot;Need (Within $game $FixtureRoot) 'Проверочная игра должна находиться в FixtureRoot.';Need ([IO.File]::ReadAllText((Join-Path $game 'F1_25.exe'),$utf8) -ceq "INERT ENGINEER VOICE TEST FIXTURE - NEVER EXECUTE`n") 'Проверочный режим требует инертный EXE-маркер; настоящая игра отклонена.';$stateRoot=Checked (Join-Path $FixtureRoot 'state');$running=[IO.File]::Exists((Join-Path $FixtureRoot 'game.running'))}
 else{$stateRoot=Checked (Join-Path $env:ProgramData 'Karsvein\F1EngineerVoice');$running=@(Get-Process -ErrorAction SilentlyContinue|Where-Object{$_.ProcessName-like 'F1_25*'}).Count-gt 0}
 $installDir=Checked (Join-Path (Join-Path $stateRoot 'installations') $identity);$stateFile=Checked (Join-Path $installDir 'state.json')
 $mutate=$Action-cin @('russian','english','restore')
 if($mutate){GameClosed;if(-not $fixture){$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent());Need ($principal.IsInRole('Administrator')) 'Для изменения озвучки нужно подтверждение Windows.'};[void][IO.Directory]::CreateDirectory($installDir);$lock=[IO.File]::Open((Checked (Join-Path $installDir '.lock')),'OpenOrCreate','ReadWrite','None')}
 $core=Checked (Join-Path $PackageRoot 'EngineerNefs.cs');Need ((Hash $core)-ceq $coreHash) 'Модуль аудиобанков не прошёл проверку. Переоткройте исходный EXE лаунчера.'
 Add-Type -Path $core -ReferencedAssemblies System.Web.Extensions
 [EngineerNefs]::Configure((Checked (Join-Path $PackageRoot 'payload\engineer-voices.json')),(Checked (Join-Path $PackageRoot 'payload\engineer-voices.bin')),$manifestHash,$literalHash)
 $state=LoadState
 if($null-ne $state -and $state.status-cne 'RESTORED'){
  VerifyBackups $state
  if($Action-ceq 'restore'){RestoreState $state}
  elseif($state.status-cne 'APPLIED'){Result 'recovery_required' 'unfinished_transaction' 'Найдена незавершённая настройка. Нажмите «Вернуть предыдущую настройку».' 2 $false (-not $running)}
  else{foreach($b in $state.banks){Need ((Hash (Target $b.name))-ceq $b.afterHash) 'Аудиобанк изменён после настройки. Файлы сохранены; проверьте установленные моды.'}}
 }
 Analyze
 if($Action-ceq 'restore'){
  if($null-ne $state -and $state.status-ceq 'RESTORED'){Result 'restored' 'restored' 'Предыдущая настройка озвучки восстановлена. Другие звуки, перевод и голос Макса сохранены.' 0 (-not $running)}
  Result 'not_ready' 'no_owned_backup' 'Этот модуль ещё не менял озвучку. Выберите «Оригинальный инженер» или «Русский инженер — тест» и примените выбор.' 2 (-not $running)
 }
 $canRestore=$null-ne $state -and $state.status-ceq 'APPLIED' -and -not $running
 if($Action-cin @('status','verify')){Result 'ready' 'verified' 'Аудиофайлы проверены. Выберите озвучку и нажмите «Применить». Игра использует язык English; русский текст остаётся отдельно.' 0 (-not $running) $canRestore}
 if($selection-ceq $Action){Result 'ready' 'already_selected' 'Выбранная озвучка уже установлена. Файлы не изменены.' 0 (-not $running) $canRestore}
 $transaction=[guid]::NewGuid().ToString('N');$txRoot=Checked (Join-Path (Join-Path $installDir 'transactions') $transaction);[void][IO.Directory]::CreateDirectory($txRoot)
 $plans=@()
 foreach($name in $bankNames){$target=Target $name;$before=Hash $target;$backup=Checked (Join-Path $txRoot ('before-'+$name));[IO.File]::Copy($target,$backup,$false);Need ((Hash $backup)-ceq $before -and(Hash $target)-ceq $before) 'Резервная копия не совпала с исходным банком.';$stage=Checked (Join-Path $txRoot ('after-'+$name));$a=[EngineerNefs]::Build($backup,$name,$Action,$stage);[EngineerNefs]::VerifyPair($backup,$stage,$name,$Action);$plans+=@([pscustomobject]@{name=$name;beforeHash=$before;afterHash=$a.Hash;changedEntries=$a.Changed})}
 $state=[pscustomobject]@{schema=1;identity=$identity;gamePath=$game;transaction=$transaction;status='PREPARED';selection=$Action;createdUtc=[DateTime]::UtcNow.ToString('o');banks=$plans;testOnly=$true;gameplayTested=$false}
 AtomicJson $state $stateFile
 try{
  Fail 'AfterJournal';Fail 'AfterStage';$n=0
  foreach($b in $state.banks){$target=Target $b.name;$stage=Checked (Join-Path $txRoot ('after-'+$b.name));if($b.beforeHash-cne $b.afterHash){Swap $stage $target $b.beforeHash $b.afterHash};$n++;if($n-eq 1){Fail 'AfterFirstReplace'}}
  Fail 'AfterBothReplace'
  foreach($b in $state.banks){Need ((Hash (Target $b.name))-ceq $b.afterHash) 'Проверка установленной озвучки не завершена.';[EngineerNefs]::VerifyPair((Backup $state $b.name),(Target $b.name),$b.name,$Action)}
  $state.status='APPLIED';AtomicJson $state $stateFile
 }catch{
  $reason=$_.Exception.Message
  try{RestoreState $state}catch{$state.status='ROLLBACK_REQUIRED';AtomicJson $state $stateFile;throw ($reason+'; для восстановления нужна сохранённая резервная копия: '+$_.Exception.Message)}
  throw ('Настройка остановлена, оба прежних аудиобанка восстановлены: '+$reason)
 }
 Analyze;Need ($selection-ceq $Action) 'Проверка выбранного языка не завершена.'
 Result 'ready' 'applied' $(if($selection-ceq 'russian'){'Тестовая русская озвучка инженера установлена: 1596 фрагментов. Проверьте ответы меню E и короткие числа в гонке.'}else{'Оригинальные английские записи инженера восстановлены для 1596 выбранных фрагментов. Остальные звуки и моды сохранены.'}) 0 (-not $running) (-not $running)
}catch{
 $message=$_.Exception.Message
 if($message-match 'UnauthorizedAccess|denied|отказано|доступ'){ $message='Windows отклонила доступ к аудиофайлам. Проверьте журнал защиты и доступ к папке игры. Защиту отключать не требуется. '+$message }
 [ordered]@{schema=1;status='error';code='operation_stopped';message=$message;diagnosticLine=$_.InvocationInfo.ScriptLineNumber;selection=$selection;installed=($selection-ceq 'russian');canApply=$false;canRestore=($null-ne $state -and $state.status-cne 'RESTORED' -and -not $running);gameRunning=$running;engineerSlots=1596;testOnly=$true;gameplayTested=$false;backup=$txRoot;banks=$current;qualityReview='Тестовая озвучка; прослушивание в игре требуется.'}|ConvertTo-Json -Depth 8 -Compress
 exit 1
}finally{if($null-ne $lock){$lock.Dispose()}}
