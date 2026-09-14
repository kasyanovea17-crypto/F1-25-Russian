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
 $p=[IO.Path]::GetFullPath((Join-Path $root $rel))
 if(!$p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Путь выходит за папку игры.'}
 $q=$p
 while($q -and $q -ne $root){if(Test-Path -LiteralPath $q){if((Get-Item -LiteralPath $q -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Ссылки в путях установки не поддерживаются.'}};$q=[IO.Path]::GetDirectoryName($q)}
 return $p
}
$lock=$null;$txn=$null
try{
 $root=(Resolve-Path -LiteralPath $GamePath).Path.TrimEnd('\')
 if($TestMode -and !$root.StartsWith(([IO.Path]::GetFullPath($PSScriptRoot)+'\test-game'),[StringComparison]::OrdinalIgnoreCase)){throw 'Тестовый режим разрешён только для изолированной тестовой папки лаунчера.'}
 if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Выберите реальную папку игры, не ссылку.'}
 $m=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'manifest.json')|ConvertFrom-Json
 $dat=Inside 'game.dat';$font=Inside '2025_asset_groups\ui_package\fonts_japanese.erp'
 $jp=Inside 'localisation\2025_russian\language_jap.lng';$en=Inside 'localisation\2025_russian\language_eng.lng'
 $backup=Inside '.f1ru-v10';$statePath=Inside '.f1ru-v10\state.json'
 foreach($p in $m.protected.PSObject.Properties){if((Hash (Inside $p.Name)) -ne $p.Value){throw ('Версия или состояние файла не поддерживается: '+$p.Name)}}
 if(!$TestMode -and (Get-Process -Name F1_25 -ErrorAction SilentlyContinue)){throw 'Сначала закройте F1 25.'}
 $dh=Hash $dat;$fh=Hash $font
 if($dh -notin @($m.dat_original,$m.dat_modified) -or $fh -notin @($m.font_original,$m.font_modified)){throw 'Нужны исходные файлы поддерживаемой версии. Сохраните моды и восстановите файлы через Steam.'}
 $state=$null;if(Test-Path -LiteralPath $statePath){$state=Get-Content -Raw -LiteralPath $statePath|ConvertFrom-Json;if($state.root -ne $root -or $state.schema -ne 10){throw 'Резервная копия относится к другой установке.'}}
 if(!$state -and ($dh -ne $m.dat_original -or $fh -ne $m.font_original)){throw 'Для этой изменённой установки нет проверенной исходной копии.'}
 $allowedLanguageHashes=@($m.lng)+@($m.previous_lng)
 foreach($p in @($jp,$en)){if(Test-Path -LiteralPath $p){$lh=Hash $p;if(!$state -or $lh -notin $allowedLanguageHashes){throw 'Языковой файл изменён другим инструментом. Он сохранён без изменений.'}}}
 if($state){if((Hash (Inside '.f1ru-v10\game.dat')) -ne $m.dat_original -or (Hash (Inside '.f1ru-v10\fonts_japanese.erp')) -ne $m.font_original){throw 'Резервная копия повреждена.'}}
 if($Action -eq 'prepare'){Write-Output 'PREPARE PASS; версия совместима. Выберите японский язык в Steam, дождитесь загрузки и закройте игру. Можно устанавливать.';exit 0}
 if($Action -eq 'restore' -and !$state){throw 'Нет резервной копии этого лаунчера. Файлы не изменены.'}
 foreach($p in $m.payload.PSObject.Properties){if((Hash (Join-Path $PSScriptRoot ('payload\'+$p.Name))) -ne $p.Value){throw 'Комплект повреждён. Распакуйте его заново.'}}
 [IO.Directory]::CreateDirectory($backup)|Out-Null
 $lock=[IO.File]::Open((Inside '.f1ru-v10\operation.lock'),'OpenOrCreate','ReadWrite','None')
 $txn=Inside ('.f1ru-transaction-'+[guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($txn)|Out-Null
 # Originals are only captured from a verified clean installation. Never replace an existing original.
 foreach($pair in @(@($dat,(Inside '.f1ru-v10\game.dat'),$m.dat_original),@($font,(Inside '.f1ru-v10\fonts_japanese.erp'),$m.font_original))){
  if(!(Test-Path -LiteralPath $pair[1])){if((Hash $pair[0]) -ne $pair[2]){throw 'Нет исходного файла для резервной копии.'};Put $pair[0] $pair[1]}
  if((Hash $pair[1]) -ne $pair[2]){throw 'Исходная копия не прошла проверку.'}
 }
 $patched=Join-Path $txn 'patched.dat'
 if($Action -eq 'install'){
  [IO.File]::Copy((Inside '.f1ru-v10\game.dat'),$patched)
  $stream=[IO.File]::Open($patched,'Open','ReadWrite');try{$bytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'payload\config.patch'));$stream.Position=[long]$m.patch_offset;$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
  if((Hash $patched) -ne $m.dat_modified){throw 'Проверка изменённого архива не пройдена.'}
 }
 $targets=@($dat,$font,$jp,$en,$statePath);$journal=@();$committed=$false
 foreach($p in $targets){$saved=Join-Path $txn ('snapshot-'+$journal.Count);$exists=Test-Path -LiteralPath $p;if($exists){[IO.File]::Copy($p,$saved)};$journal+=@{path=$p;saved=$saved;exists=$exists}}
 try{
  if($Action -eq 'install'){
   Put $patched $dat;Put (Join-Path $PSScriptRoot 'payload\fonts_japanese.erp') $font
   Put (Join-Path $PSScriptRoot 'payload\language.lng') $jp;Put (Join-Path $PSScriptRoot 'payload\language.lng') $en
   if((Hash $dat) -ne $m.dat_modified -or (Hash $font) -ne $m.font_modified -or (Hash $jp) -ne $m.lng -or (Hash $en) -ne $m.lng){throw 'Проверка установки не пройдена.'}
  }else{
   Put (Inside '.f1ru-v10\game.dat') $dat;Put (Inside '.f1ru-v10\fonts_japanese.erp') $font
   foreach($p in @($jp,$en)){if(Test-Path -LiteralPath $p){[IO.File]::Delete($p)}}
   if((Hash $dat) -ne $m.dat_original -or (Hash $font) -ne $m.font_original -or (Test-Path -LiteralPath $jp) -or (Test-Path -LiteralPath $en)){throw 'Проверка восстановления не пройдена.'}
  }
  foreach($p in $m.protected.PSObject.Properties){if((Hash (Inside $p.Name)) -ne $p.Value){throw 'Состояние игры изменилось во время операции.'}}
  @{schema=10;root=$root;status=$Action;version=$m.version;online_verified=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $txn 'state.json') -Encoding UTF8
  Put (Join-Path $txn 'state.json') $statePath;$committed=$true
 }finally{
  if(!$committed){foreach($j in $journal){if($j.exists){if(!(Test-Path -LiteralPath $j.path) -or (Hash $j.path) -ne (Hash $j.saved)){Put $j.saved $j.path}}elseif(Test-Path -LiteralPath $j.path){[IO.File]::Delete($j.path)}}}
 }
 if($Action -eq 'install'){Write-Output 'INSTALL PASS; русский текст и шрифт установлены; EXE и античит не изменены.'}else{Write-Output 'RESTORE PASS; исходные game.dat и шрифт восстановлены; два файла русского текста удалены; EXE и античит не изменены.'}
 exit 0
}catch{Write-Output ('ОШИБКА: '+$_.Exception.Message);exit 1}
finally{
 if($lock){$lock.Dispose()}
 # Keep transaction snapshots for recovery/inspection instead of deleting recursively.
}

