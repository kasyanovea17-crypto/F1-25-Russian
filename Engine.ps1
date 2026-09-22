param([ValidateSet('prepare','install','restore')][string]$Action='prepare',[Parameter(Mandatory=$true)][string]$GamePath,[switch]$TestMode)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
function Hash([string]$p){$a=[Security.Cryptography.SHA256]::Create();$s=[IO.File]::OpenRead($p);try{return ([BitConverter]::ToString($a.ComputeHash($s))).Replace('-','').ToLowerInvariant()}finally{$s.Dispose();$a.Dispose()}}
function Put([string]$src,[string]$dst){
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dst))|Out-Null
 $temp=$dst+'.f1ru-'+[guid]::NewGuid().ToString('N')
 try{[IO.File]::Copy($src,$temp);if((Hash $temp) -ne (Hash $src)){throw 'Ошибка копирования.'}
 if([IO.File]::Exists($dst)){[IO.File]::Replace($temp,$dst,$temp+'.old');[IO.File]::Delete($temp+'.old')}else{[IO.File]::Move($temp,$dst)}}finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Inside([string]$rel){
 if([IO.Path]::IsPathRooted($rel)){throw 'Ожидается относительный путь.'}
 $p=[IO.Path]::GetFullPath((Join-Path $root $rel))
 if(!$p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Путь выходит за папку игры.'}
 $q=$p
 while($q -and $q -ne $root){if(Test-Path -LiteralPath $q){if((Get-Item -LiteralPath $q -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Ссылки в путях установки не поддерживаются.'}};$q=[IO.Path]::GetDirectoryName($q)}
 return $p
}
function Payload([string]$rel){
 $base=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'payload'))
 $p=[IO.Path]::GetFullPath((Join-Path $base $rel))
 if([IO.Path]::IsPathRooted($rel) -or !$p.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Неверный путь комплекта.'}
 return $p
}
function Hex([string]$s){if($s.Length % 2 -ne 0 -or $s -notmatch '^[0-9a-f]+$'){throw 'Неверные данные патча.'};[byte[]]$b=New-Object byte[] ($s.Length/2);for($i=0;$i -lt $b.Length;$i++){$b[$i]=[Convert]::ToByte($s.Substring($i*2,2),16)};return ,$b}
function Patch([string]$path,[bool]$install){
 $s=[IO.File]::Open($path,'Open','ReadWrite');try{
  $end=0L
  foreach($p in $profile.patches){$before=Hex $p.before;$after=Hex $p.after;$offset=[long]$p.offset
   if($before.Length -ne $after.Length -or $offset -lt $end -or $offset+$before.Length -gt $s.Length){throw 'Некорректный диапазон патча.'}
   $s.Position=$offset;$b=$before;if($install){$b=$after};$s.Write($b,0,$b.Length);$end=$offset+$b.Length
  }
 }finally{$s.Dispose()}
}
function AssertClosed{if(!$TestMode -and (Get-Process -Name F1_25 -ErrorAction SilentlyContinue)){throw 'Сначала закройте F1 25.'}}
function TargetNames{return @('game.dat')+@($profile.stock_files.PSObject.Properties|ForEach-Object {$_.Name})+@($profile.install_files|ForEach-Object {$_.target})+@($profile.backup_dir+'\state.json')}
function CurrentHash([string]$p){if([IO.File]::Exists($p)){return (Hash $p)};if(Test-Path -LiteralPath $p){throw 'Вместо файла обнаружена папка.'};return $null}
function SaveJson([string]$path,$value){
 $encoding=New-Object Text.UTF8Encoding($true);$bytes=$encoding.GetBytes(($value|ConvertTo-Json -Depth 12));$preamble=$encoding.GetPreamble()
 $s=[IO.File]::Open($path,'Create','Write','None');try{$s.Write($preamble,0,$preamble.Length);$s.Write($bytes,0,$bytes.Length);$s.Flush($true)}finally{$s.Dispose()}
}
function CleanupTxn([string]$name){
 if($name -notmatch '^\.f1ru-transaction-[a-f0-9]{32}$'){throw 'Неверное имя временной папки.'}
 $dir=Inside $name
 $files=@('stock.dat','patched.dat','state-next.json','pending-next.json')
 for($i=0;$i -lt @(TargetNames).Count;$i++){$files+=('snapshot-'+$i)}
 foreach($file in $files){$p=Inside ($name+'\'+$file);if([IO.File]::Exists($p)){[IO.File]::Delete($p)}}
 # Delete only the now-empty exact directory; never recurse over unknown files.
 if([IO.Directory]::Exists($dir) -and [IO.Directory]::GetFileSystemEntries($dir).Length -eq 0){[IO.Directory]::Delete($dir,$false)}
}
function ValidateJournal($j){
 if($j.schema -ne 1 -or $j.root -ne $root -or $j.backup_dir -ne $profile.backup_dir -or $j.action -notin @('install','restore') -or $j.transaction -notmatch '^\.f1ru-transaction-[a-f0-9]{32}$'){throw 'Журнал незавершённой операции не прошёл проверку.'}
 $dir=Inside $j.transaction
 if(![IO.Directory]::Exists($dir)){throw 'Резервные данные незавершённой операции отсутствуют.'}
 $names=@(TargetNames);$entries=@($j.entries)
 if($entries.Count -ne $names.Count -or @($names|Select-Object -Unique).Count -ne $names.Count){throw 'Неверный список файлов в журнале.'}
 for($i=0;$i -lt $names.Count;$i++){
  $e=$entries[$i]
  if($e.target -cne $names[$i] -or $e.snapshot -cne ('snapshot-'+$i) -or $e.existed -isnot [bool]){throw 'Пути журнала не соответствуют комплекту.'}
  $null=Inside $e.target;$saved=Inside ($j.transaction+'\'+$e.snapshot)
  if($e.existed){if($e.before -notmatch '^[0-9a-f]{64}$' -or !(Test-Path -LiteralPath $saved) -or (Hash $saved) -ne $e.before){throw 'Снимок исходного файла повреждён.'}}
  elseif($null -ne $e.before -and $e.before -ne ''){throw 'Неверное исходное состояние журнала.'}
  if($null -ne $e.after -and $e.after -ne '' -and $e.after -notmatch '^[0-9a-f]{64}$'){throw 'Неверная сумма результата операции.'}
  if($e.target -eq 'game.dat'){
   if($e.before -notin @($profile.dat_original,$profile.dat_installed)+@($profile.accepted_dat)){throw 'Исходный архив журнала неизвестен.'}
   $want=@($profile.dat_original);if($j.action -eq 'install'){$want=@($profile.dat_installed)+@($profile.accepted_dat)}
   if($e.after -notin $want){throw 'Результат архива в журнале неизвестен.'}
  }elseif($profile.stock_files.PSObject.Properties.Name -contains $e.target){
   $stock=$profile.stock_files.($e.target);$known=@($stock)
   if($e.target -eq '2025_asset_groups\ui_package\fonts_japanese.erp'){$known+=@($m.font_modified)}
   if($e.before -notin $known -or $e.after -ne $stock){throw 'Состояние исходного шрифта в журнале неизвестно.'}
  }elseif($e.target -ne ($profile.backup_dir+'\state.json')){
   $f=@($profile.install_files|Where-Object {$_.target -ceq $e.target})[0]
   $known=@($f.sha256)+@($f.previous_sha256);if($f.payload -eq 'language.lng'){$known=@($m.lng)+@($m.previous_lng)}
   foreach($hash in $known){if($null -ne $hash -and $hash -notmatch '^[0-9a-f]{64}$'){throw 'Некорректная история контрольных сумм.'}}
   if($e.existed -and $e.before -notin $known){throw 'Предыдущее состояние ресурса в журнале неизвестно.'}
   if($j.action -eq 'install'){$want=@($f.sha256);if($f.payload -eq 'language.lng'){$want=@($m.lng)+@($m.previous_lng)};if($e.after -notin $want){throw 'Новое состояние ресурса в журнале неизвестно.'}}elseif($null -ne $e.after -and $e.after -ne ''){throw 'Удаляемый ресурс имеет неверное состояние.'}
  }
 }
}
function RecoverPending{
 if(!(Test-Path -LiteralPath $pendingPath)){return}
 AssertClosed
 $j=Get-Content -LiteralPath $pendingPath -Raw|ConvertFrom-Json
 ValidateJournal $j
 # Preflight every destination before touching any of them. Unknown files are preserved.
 foreach($e in $j.entries){$h=CurrentHash (Inside $e.target);if($h -and $h -ne $e.before -and $h -ne $e.after){throw ('Незавершённая операция: файл изменён извне, сохранён без изменений: '+$e.target)}}
 foreach($e in $j.entries){
  $path=Inside $e.target;$h=CurrentHash $path
  if($h -and $h -ne $e.before -and $h -ne $e.after){throw ('Файл изменился во время восстановления: '+$e.target)}
  if($e.existed){if($h -ne $e.before){Put (Inside ($j.transaction+'\'+$e.snapshot)) $path}}
  elseif($h){[IO.File]::Delete($path)}
 }
 foreach($e in $j.entries){$h=CurrentHash (Inside $e.target);if($e.existed){if($h -ne $e.before){throw 'Восстановленный файл не прошёл проверку.'}}elseif($h){throw 'Лишний файл остался после восстановления.'}}
 [IO.File]::Delete($pendingPath)
 try{CleanupTxn $j.transaction}catch{Write-Output ('Восстановление завершено; временные файлы сохранены: '+$_.Exception.Message)}
 Write-Output 'RECOVERY PASS; незавершённая операция отменена, исходное состояние восстановлено.'
}
function LoadCompatibility {
 if(!('ResourceCompatibility' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'Compatibility.cs')}
}
function ValidPublisher([string]$file,[string]$organization){
 # Explicit Windows PowerShell module path also works when launched by PowerShell 7.
 Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
 $sig=Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $file
 if($sig.Status -ne 'Valid' -or !$sig.SignerCertificate){return $false}
 # Match the exact organization RDN, not an arbitrary substring or a certificate name.
 return $sig.SignerCertificate.Subject -match ('(?:^|,\s*)O="'+[regex]::Escape($organization)+'"(?:,|$)')
}
function HeaderCompatible($p,[string]$file){
 if(!$p.micro_compatibility -or $p.micro_compatibility.schema -ne 1){return $false}
 LoadCompatibility
 return [ResourceCompatibility]::HeaderMatches($file,[int]$p.micro_compatibility.exe_header_bytes,[string]$p.micro_compatibility.exe_header_sha256)
}
function ResolveMicroArchive {
 $datPath=Inside 'game.dat';$current=Hash $datPath
 if($current -in @($profile.dat_original,$profile.dat_installed)+@($profile.accepted_dat)){return}
 if(!$profile.micro_compatibility){throw 'Неизвестный архив: требуется профиль совместимости.'}
 LoadCompatibility;$a=$profile.micro_compatibility
 $result=[ResourceCompatibility]::Analyze($datPath,[long]$a.dat_bytes,[int[]]@($profile.patches|ForEach-Object {$_.offset}),[string[]]@($profile.patches|ForEach-Object {$_.before}),[string[]]@($profile.patches|ForEach-Object {$_.after}),[int[]]@($a.critical_ranges|ForEach-Object {$_.offset}),[int[]]@($a.critical_ranges|ForEach-Object {$_.length}),[string[]]@($a.critical_ranges|ForEach-Object {$_.sha256}))
 if($result[2] -ne $current){throw 'Архив изменился во время проверки. Повторите операцию.'}
 if($current -ne $result[0] -and $current -ne $result[1]){throw 'Обнаружено смешанное состояние ресурсов. Восстановите оригинальные файлы.'}
 $profile.dat_original=$result[0];$profile.dat_installed=$result[1];$profile.accepted_dat=@($current)
 $profile.backup_dir='.f1ru-v25-'+$result[0]
 Write-Output 'COMPATIBILITY PASS; изменены посторонние данные, ресурсы перевода совпадают. Резервная копия привязана к текущему архиву.'
}
$lock=$null;$txn=$null;$txnName=$null;$pendingPath=$null
try{
 $root=(Resolve-Path -LiteralPath $GamePath).Path.TrimEnd('\')
 if($TestMode -and !$root.StartsWith(([IO.Path]::GetFullPath($PSScriptRoot)+'\test-game'),[StringComparison]::OrdinalIgnoreCase)){throw 'Тестовый режим разрешён только для изолированной тестовой папки лаунчера.'}
 if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Выберите реальную папку игры, не ссылку.'}
 $m=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'manifest.json')|ConvertFrom-Json
 $exeHash=Hash (Inside 'F1_25.exe')
 $profiles=@($m.native_profiles|Where-Object {$_.exe_sha256 -eq $exeHash})
 if($profiles.Count -eq 0){
  $profiles=@($m.native_profiles|Where-Object { $_.micro_compatibility -and (ValidPublisher (Inside 'F1_25.exe') $_.micro_compatibility.publisher_organization) -and (HeaderCompatible $_ (Inside 'F1_25.exe')) })
  if($profiles.Count -eq 1){Write-Output 'COMPATIBILITY PASS; подпись EA действительна, индекс игровых ресурсов не изменён.'}
 }
 if($profiles.Count -ne 1){throw 'Структура игровых ресурсов изменилась. Нужен новый профиль совместимости, файлы сохранены.'}
 $profile=$profiles[0]
 AssertClosed
 ResolveMicroArchive
 if($profile.backup_dir -notmatch '^(\.f1ru-v24-build[0-9]+|\.f1ru-v25-[a-f0-9]{64})$'){throw 'Неверный профиль резервной копии.'}
 AssertClosed
 $backup=Inside $profile.backup_dir;$statePath=Inside ($profile.backup_dir+'\state.json');$pendingPath=Inside ($profile.backup_dir+'\pending.json')
 [IO.Directory]::CreateDirectory($backup)|Out-Null
 $lock=[IO.File]::Open((Inside ($profile.backup_dir+'\operation.lock')),'OpenOrCreate','ReadWrite','None')
 RecoverPending
 $protected=@{}
 foreach($p in $profile.protected.PSObject.Properties){$h=Hash (Inside $p.Name);if($h -notin @($p.Value)){
  if(!$profile.micro_compatibility -or !(ValidPublisher (Inside $p.Name) $profile.micro_compatibility.publisher_organization)){throw ('Подпись обновлённого файла не прошла проверку: '+$p.Name)}
  if($p.Name -eq 'F1_25.exe' -and !(HeaderCompatible $profile (Inside $p.Name))){throw 'Индекс игровых ресурсов изменён.'}
  Write-Output ('COMPATIBILITY PASS; обновлённый подписанный файл сохранён: '+$p.Name)
 };$protected[$p.Name]=$h}
 $dat=Inside 'game.dat';$dh=Hash $dat
 if($dh -notin @($profile.dat_original,$profile.dat_installed)+@($profile.accepted_dat)){throw 'Состояние game.dat не распознано. Файл сохранён без изменений.'}
 $original=Inside ($profile.backup_dir+'\game.dat')
 if(Test-Path -LiteralPath $original){if((Hash $original) -ne $profile.dat_original){throw 'Исходная копия game.dat повреждена.'}}
 if(Test-Path -LiteralPath $statePath){$state=Get-Content -Raw -LiteralPath $statePath|ConvertFrom-Json;if($state.root -ne $root -or $state.schema -ne 24){throw 'Резервная копия относится к другой установке.'}}
 $stockSources=@{};$stockHashes=@{}
 foreach($p in $profile.stock_files.PSObject.Properties){
  $path=Inside $p.Name;$h=Hash $path;$stockHashes[$p.Name]=$h
  if($h -eq $p.Value){$stockSources[$p.Name]=$path;continue}
  if($p.Name -ne '2025_asset_groups\ui_package\fonts_japanese.erp' -or $h -ne $m.font_modified){throw ('Исходный ресурс изменён: '+$p.Name)}
  $found=$null
  foreach($dir in @('.f1ru-v10',('.f1ru-v10-build'+$profile.steam_build),$profile.backup_dir)){
   $candidate=Inside ($dir+'\fonts_japanese.erp')
   if((Test-Path -LiteralPath $candidate) -and (Hash $candidate) -eq $p.Value){$found=$candidate;break}
  }
  if(!$found){throw 'Для переноса старой установки нужен исходный японский шрифт. Восстановите файлы через клиент игры (Steam / EA app).'}
  $stockSources[$p.Name]=$found
 }
 $allowedLanguageHashes=@($m.lng)+@($m.previous_lng)
 foreach($file in $profile.install_files){
  $p=Inside $file.target
  $allowed=@($file.sha256)+@($file.previous_sha256);if($file.payload -eq 'language.lng'){$allowed=$allowedLanguageHashes}
  foreach($hash in $allowed){if($null -ne $hash -and $hash -notmatch '^[0-9a-f]{64}$'){throw 'Некорректная история контрольных сумм.'}}
  if(Test-Path -LiteralPath $p){$h=Hash $p;if($h -notin $allowed){throw ('Файл изменён другим инструментом: '+$file.target)}}
 }
 if($Action -ne 'restore'){foreach($p in $m.payload.PSObject.Properties){if((Hash (Payload $p.Name)) -ne $p.Value){throw ('Комплект повреждён: '+$p.Name)}}}
 if($Action -eq 'prepare'){Write-Output 'PREPARE PASS; комплект и сборка совместимы. Отдельные русские шрифты; исходные шрифты, EXE и античит сохраняются.';exit 0}
 $txnName='.f1ru-transaction-'+[guid]::NewGuid().ToString('N');$txn=Inside $txnName;[IO.Directory]::CreateDirectory($txn)|Out-Null
 # Recover exact stock only from known complete input hashes and locally packaged patch bytes.
 if(!(Test-Path -LiteralPath $original)){
  $clean=Join-Path $txn 'stock.dat';[IO.File]::Copy($dat,$clean);Patch $clean $false
  if((Hash $clean) -ne $profile.dat_original){throw 'Исходный архив не прошёл проверку. Игра не изменена.'}
  Put $clean $original
 }
 foreach($name in $stockSources.Keys){$save=Inside ($profile.backup_dir+'\'+[IO.Path]::GetFileName($name));if(!(Test-Path -LiteralPath $save)){Put $stockSources[$name] $save};if((Hash $save) -ne $profile.stock_files.$name){throw 'Исходный шрифт в резервной копии повреждён.'}}
 $patched=Join-Path $txn 'patched.dat'
 if($Action -eq 'install'){
  [IO.File]::Copy($original,$patched);Patch $patched $true
  if((Hash $patched) -ne $profile.dat_installed){throw 'Изменённый архив не прошёл проверку.'}
 }
 AssertClosed
 if((Hash $dat) -ne $dh){throw 'Игра обновилась во время подготовки. Повторите проверку.'}
 foreach($name in $stockHashes.Keys){if((Hash (Inside $name)) -ne $stockHashes[$name]){throw 'Исходные ресурсы изменились во время подготовки.'}}
 $nextState=Join-Path $txn 'state-next.json'
 SaveJson $nextState @{schema=24;root=$root;status=$Action;version=$m.version;steam_build=$profile.steam_build;online_verified=$false;audio_files_changed=$false}
 $entries=@();$committed=$false
 foreach($name in @(TargetNames)){
  $p=Inside $name;$before=CurrentHash $p;$exists=$null -ne $before;$saved=Join-Path $txn ('snapshot-'+$entries.Count)
  if($exists){[IO.File]::Copy($p,$saved);if((Hash $saved) -ne $before){throw 'Снимок изменился во время копирования.'}}
  $after=$null
  if($name -eq 'game.dat'){$after=$profile.dat_original;if($Action -eq 'install'){$after=$profile.dat_installed}}
  elseif($profile.stock_files.PSObject.Properties.Name -contains $name){$after=$profile.stock_files.$name}
  elseif($name -eq ($profile.backup_dir+'\state.json')){$after=Hash $nextState}
  elseif($Action -eq 'install'){$f=@($profile.install_files|Where-Object {$_.target -ceq $name})[0];$after=$f.sha256;if($f.payload -eq 'language.lng'){$after=$m.lng}}
  $entries+=@{target=$name;snapshot=('snapshot-'+$entries.Count);existed=$exists;before=$before;after=$after}
 }
 $journal=@{schema=1;root=$root;backup_dir=$profile.backup_dir;transaction=$txnName;action=$Action;entries=$entries}
 SaveJson (Join-Path $txn 'pending-next.json') $journal
 $checked=Get-Content -LiteralPath (Join-Path $txn 'pending-next.json') -Raw|ConvertFrom-Json;ValidateJournal $checked
 foreach($e in $checked.entries){if((CurrentHash (Inside $e.target)) -ne $e.before){throw 'Файлы изменились до начала установки.'}}
 Put (Join-Path $txn 'pending-next.json') $pendingPath
 try{
  if($Action -eq 'install'){
   Put $patched $dat
   foreach($file in $profile.install_files){Put (Payload $file.payload) (Inside $file.target)}
  }else{
   Put $original $dat
   foreach($file in $profile.install_files){$p=Inside $file.target;if(Test-Path -LiteralPath $p){[IO.File]::Delete($p)}}
  }
  foreach($name in $stockSources.Keys){$dst=Inside $name;if((Hash $dst) -ne $profile.stock_files.$name){Put (Inside ($profile.backup_dir+'\'+[IO.Path]::GetFileName($name))) $dst};if((Hash $dst) -ne $profile.stock_files.$name){throw 'Исходный шрифт не прошёл проверку.'}}
  $expected=$profile.dat_original;if($Action -eq 'install'){$expected=$profile.dat_installed}
  if((Hash $dat) -ne $expected){throw 'Архив после записи не прошёл проверку.'}
  foreach($file in $profile.install_files){$p=Inside $file.target;$fileHash=$file.sha256;if($file.payload -eq 'language.lng'){$fileHash=$m.lng};if($Action -eq 'install'){if((Hash $p) -ne $fileHash){throw 'Ресурс после записи не прошёл проверку.'}}elseif(Test-Path -LiteralPath $p){throw 'Добавленный ресурс остался после восстановления.'}}
  foreach($name in $protected.Keys){if((Hash (Inside $name)) -ne $protected[$name]){throw 'Состояние исполняемых файлов изменилось во время операции.'}}
  Put $nextState $statePath
  foreach($e in $checked.entries){if((CurrentHash (Inside $e.target)) -ne $e.after){throw 'Финальное состояние операции не прошло проверку.'}}
  [IO.File]::Delete($pendingPath);$committed=$true
 }finally{
  if(!$committed){RecoverPending}
 }
 if($Action -eq 'install'){Write-Output 'INSTALL PASS; русский текст и отдельные шрифты установлены; исходные шрифты, озвучка, EXE и античит не изменены.'}else{Write-Output 'RESTORE PASS; исходный game.dat и шрифты восстановлены; добавленные шрифты и два файла русского текста удалены; озвучка, EXE и античит не изменены.'}
 exit 0
}catch{Write-Output ('ОШИБКА: '+$_.Exception.Message);exit 1}
finally{
 if($txnName -and $pendingPath -and !(Test-Path -LiteralPath $pendingPath)){try{CleanupTxn $txnName}catch{Write-Output ('Операция завершена; временные файлы сохранены: '+$_.Exception.Message)}}
 if($lock){$lock.Dispose()}
}
