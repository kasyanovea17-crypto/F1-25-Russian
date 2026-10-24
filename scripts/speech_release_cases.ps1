[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PackageRoot,
 [Parameter(Mandatory=$true)][string]$EnginePath,
 [Parameter(Mandatory=$true)][string]$PreviousPackageRoot,
 [Parameter(Mandatory=$true)][string]$RunRoot,
 [Parameter(Mandatory=$true)][string]$ReportPath
)
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) -ErrorAction Stop
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$utf8=New-Object Text.UTF8Encoding($false)
$engine=[IO.Path]::GetFullPath($EnginePath)
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$run=[IO.Path]::GetFullPath($RunRoot)
[void][IO.Directory]::CreateDirectory($run)
$results=New-Object Collections.Generic.List[object]
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash}
function Save($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 20),$utf8)}
function NewFixture([string]$Name){
 $root=Join-Path $run $Name;$game=Join-Path $root 'game';$audio=Join-Path $game 'audio\2025'
 [void][IO.Directory]::CreateDirectory($audio)
 [IO.File]::WriteAllBytes((Join-Path $game 'F1_25.exe'),[byte[]]@(77,90))
 Copy-Item -LiteralPath (Join-Path $PackageRoot 'ORIGINAL.xml') -Destination (Join-Path $audio 'speech_recognition.xml')
 $reg=[pscustomobject]@{runtime=$true;tokens=@(
  [pscustomobject]@{token='SR_MS_ru-RU_TELE_11.0';exists=$true;kind='String';value='419'},
  [pscustomobject]@{token='SR_MS_en-US_Kinect_11.0';exists=$true;kind='String';value='409'},
  [pscustomobject]@{token='SR_MS_en-GB_Kinect_11.0';exists=$true;kind='ExpandString';value='809;1009'},
  [pscustomobject]@{token='SR_MS_ja-JP_Kinect_11.0';exists=$true;kind='String';value='411'},
  [pscustomobject]@{token='TEST_Unrelated';exists=$true;kind='DWord';value=123},
  [pscustomobject]@{token='TEST_Missing';exists=$false;kind=$null;value=$null}
 )}
 Save $reg (Join-Path $root 'registry.json')
 return [pscustomobject]@{root=$root;game=$game;xml=(Join-Path $audio 'speech_recognition.xml');registry=(Join-Path $root 'registry.json');originalHash=(Hash (Join-Path $audio 'speech_recognition.xml'));originalRegistry=($reg|ConvertTo-Json -Depth 20 -Compress)}
}
function Call($Fixture,[string]$Action,[string]$Fail='None',[string]$Package=$PackageRoot,[string]$Legacy=''){
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$engine,'-Action',$Action,'-GamePath',$Fixture.game,'-PackageRoot',$Package,'-FixtureRoot',$Fixture.root,'-FixtureFailPoint',$Fail)
 if($Legacy){$arguments+=@('-LegacyState',$Legacy)}
 $out=& $powershell @arguments 2>&1
 $exit=$LASTEXITCODE;$text=($out -join "`n")
 try{$parsed=$text | ConvertFrom-Json}catch{throw ('Invalid engine JSON: '+$text)}
 return [pscustomobject]@{exit=$exit;data=$parsed;literal=$text}
}
function Record([string]$Name,$Result){$results.Add([pscustomobject]@{test=$Name;passed=$true;exit=$Result.exit;output=$Result.literal});Write-Output ('PASS '+$Name)}
function SameRegistry($Fixture){return (((Get-Content -LiteralPath $Fixture.registry -Raw -Encoding UTF8|ConvertFrom-Json)|ConvertTo-Json -Depth 20 -Compress) -ceq $Fixture.originalRegistry)}
function MakeLegacy([string]$Name,[string]$LegacyPackage=$PackageRoot){
 $f=NewFixture $Name;$j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json
 $j.tokens[1].value='409;9';$j.tokens[2].kind='String';$j.tokens[2].value='809;9';Save $j $f.registry
 $f.originalRegistry=$j|ConvertTo-Json -Depth 20 -Compress
 $r=Call $f 'install' 'None' $LegacyPackage;Assert ($r.exit -eq 0) 'Legacy fixture setup failed.'
 $ownerPath=Join-Path $f.root 'state\owner.json';$owner=Get-Content -LiteralPath $ownerPath -Raw -Encoding UTF8|ConvertFrom-Json
 $installDir=Join-Path $f.root ('state\installations\'+$owner.identity);$statePath=Join-Path $installDir 'state.json';$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 $legacyDir=Join-Path $f.root 'legacy';[void][IO.Directory]::CreateDirectory($legacyDir)
 Copy-Item -LiteralPath (Join-Path $installDir ('transactions\'+$state.transaction+'\original.xml')) -Destination (Join-Path $legacyDir 'ORIGINAL.xml')
 $legacy=[pscustomobject]@{schema=1;scope='live';status='APPLIED';target=$f.xml;original_sha256=$state.originalHash;modified_sha256=$state.modifiedHash;entries=$state.entries;untouched=@($j.tokens|Where-Object {$_.token -notin @('SR_MS_en-US_Kinect_11.0','SR_MS_en-GB_Kinect_11.0','SR_MS_ru-RU_TELE_11.0')})}
 $legacyPath=Join-Path $legacyDir 'live_state.json';Save $legacy $legacyPath
 # Only these two fixture-created journal files are removed; game and registry stay installed.
 [IO.File]::Delete($ownerPath);[IO.File]::Delete($statePath)
 $f | Add-Member -NotePropertyName legacy -NotePropertyValue $legacyPath
 return $f
}

try{
 $f=NewFixture 'roundtrip'
 $r=Call $f 'status';Assert ($r.exit -eq 0 -and $r.data.code -eq 'ready_to_install') 'Baseline not ready.'
 Assert (-not (Test-Path -LiteralPath (Join-Path $f.root 'state'))) 'Status wrote state.';Record 'status_readonly' $r
 $r=Call $f 'install';Assert ($r.exit -eq 0 -and $r.data.installed) ('Install failed: '+$r.literal)
 Assert ((Hash $f.xml) -ne $f.originalHash) 'XML unchanged after install.'
 $doc=New-Object Xml.XmlDocument;$doc.Load($f.xml)
 foreach($k in $doc.SelectNodes('//Keyword')){if($k.GetAttribute('name') -ne 'default'){Assert ($k.GetAttribute('en') -ceq $k.GetAttribute('ru')) 'RU and EN mismatch.'}}
 $orig=New-Object Xml.XmlDocument;$orig.Load((Join-Path $PackageRoot 'ORIGINAL.xml'))
 Assert ($orig.SelectNodes('//Action').Count -eq $doc.SelectNodes('//Action').Count) 'Action count changed.'
 $j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json
 Assert (@($j.tokens|Where-Object token -eq 'SR_MS_ja-JP_Kinect_11.0')[0].value -ceq '411') 'Japanese changed.'
 Assert (@($j.tokens|Where-Object token -eq 'TEST_Unrelated')[0].kind -ceq 'DWord') 'Unrelated type changed.';Record 'install_en_ru_preserves_game_actions_japanese' $r
 $installedHash=Hash $f.xml
 $r=Call $f 'install';Assert ($r.exit -eq 0 -and $r.data.code -eq 'verified' -and (Hash $f.xml) -eq $installedHash) 'Idempotent install failed.';Record 'install_idempotent' $r
 $r=Call $f 'verify';Assert ($r.exit -eq 0 -and $r.data.installed) 'Verify failed.';Record 'verify' $r
 $other=Join-Path $f.root 'second_game';[void][IO.Directory]::CreateDirectory((Join-Path $other 'audio\2025'))
 Copy-Item -LiteralPath (Join-Path $f.game 'F1_25.exe') -Destination (Join-Path $other 'F1_25.exe')
 Copy-Item -LiteralPath (Join-Path $PackageRoot 'ORIGINAL.xml') -Destination (Join-Path $other 'audio\2025\speech_recognition.xml')
 $g=[pscustomobject]@{root=$f.root;game=$other}
 $r=Call $g 'install';Assert ($r.exit -eq 2 -and $r.data.code -eq 'owned_by_other_installation') 'Second installation stole aliases.';Record 'global_owner_second_installation_rejected' $r
 $r=Call $f 'restore' 'None' (Join-Path $run 'missing_package')
 Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Restore not byte/type exact.'
 Assert (-not (Test-Path -LiteralPath (Join-Path $f.root 'state\owner.json'))) 'Owner not released.';Record 'restore_without_candidate_or_dictionary' $r
 $r=Call $f 'restore';Assert ($r.exit -eq 0 -and $r.data.code -eq 'already_restored') 'Repeated restore failed.';Record 'restore_idempotent' $r

 foreach($point in @('AfterRegistryWrite','AfterXmlWrite')){
  $f=NewFixture $point;$r=Call $f 'install' $point
  Assert ($r.exit -eq 1 -and $r.data.code -eq 'install_failed_restored') ('Injection failed: '+$r.literal)
  Assert ((Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Exception rollback changed original.'
  Assert (-not (Test-Path -LiteralPath (Join-Path $f.root 'state\owner.json'))) 'Failed install retained owner.';Record ('exception_rollback_'+$point) $r
 }
 $f=NewFixture 'external_xml';$r=Call $f 'install';Assert ($r.exit -eq 0) 'Setup failed.'
 [IO.File]::AppendAllText($f.xml,"`r`n<!--external-->",$utf8);$changed=Hash $f.xml;$regHash=Hash $f.registry
 $r=Call $f 'restore';Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $changed -and (Hash $f.registry) -eq $regHash) 'External XML overwritten.';Record 'restore_conflict_xml_no_partial_writes' $r
 $f=NewFixture 'external_registry';$r=Call $f 'install';Assert ($r.exit -eq 0) 'Setup failed.'
 $j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$j.tokens[1].value='409;999';Save $j $f.registry
 $xmlHash=Hash $f.xml;$regHash=Hash $f.registry;$r=Call $f 'restore'
 Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $xmlHash -and (Hash $f.registry) -eq $regHash) 'External registry overwritten.';Record 'restore_conflict_registry_no_partial_writes' $r
 $f=NewFixture 'missing_model';$j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$j.tokens=@($j.tokens|Where-Object token -ne 'SR_MS_ru-RU_TELE_11.0');Save $j $f.registry
 $r=Call $f 'install';Assert ($r.exit -eq 2 -and $r.data.code -eq 'missing_russian_model' -and (Hash $f.xml) -eq $f.originalHash) 'Missing model mishandled.';Record 'missing_model_no_install' $r
 $f=NewFixture 'external_route';$j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$j.tokens[0].value='419;409;809';$j.tokens[1].value='0';$j.tokens[2].value='1009';Save $j $f.registry
 $regHash=Hash $f.registry;$r=Call $f 'install';Assert ($r.exit -eq 2 -and $r.data.code -eq 'external_route' -and -not $r.data.installed -and -not $r.data.canRestore -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $f.originalHash) 'External route falsely adopted.';Record 'external_route_not_owned' $r
 $f=NewFixture 'game_running';[IO.File]::WriteAllText((Join-Path $f.root 'game.running'),'running')
 $r=Call $f 'install';Assert ($r.exit -eq 2 -and $r.data.code -eq 'game_running' -and (Hash $f.xml) -eq $f.originalHash) 'Running game not rejected.';Record 'running_game_rejected' $r
 $f=NewFixture 'dictionary_conflict';$pkg=Join-Path $f.root 'package';[void][IO.Directory]::CreateDirectory($pkg)
 $dictionary=Get-Content -LiteralPath (Join-Path $PackageRoot 'commands.ru.json') -Raw -Encoding UTF8|ConvertFrom-Json;$dictionary.Box=$dictionary.Ahead;Save $dictionary (Join-Path $pkg 'commands.ru.json')
 $r=Call $f 'install' 'None' $pkg;Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Duplicate dictionary mutated install.';Record 'duplicate_phrase_rejected' $r
 $f=NewFixture 'unexpected_alias';$j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$j.tokens[3].value='411;409';Save $j $f.registry;$regHash=Hash $f.registry
 $r=Call $f 'install';Assert ($r.exit -eq 1 -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $f.originalHash) 'Japanese alias mutated.';Record 'unexpected_alias_no_japanese_mutation' $r
 $f=MakeLegacy 'migration';$regHash=Hash $f.registry;$xmlHash=Hash $f.xml;$legacyHash=Hash $f.legacy
 $r=Call $f 'migrate' 'None' $PackageRoot $f.legacy
 Assert ($r.exit -eq 0 -and $r.data.code -eq 'migrated' -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $xmlHash -and (Hash $f.legacy) -eq $legacyHash) ('Migration altered inputs or failed: '+$r.literal);Record 'explicit_migration_preserves_existing_game_registry_legacy' $r
 $r=Call $f 'migrate' 'None' $PackageRoot $f.legacy;Assert ($r.exit -eq 0 -and $r.data.code -eq 'verified') 'Migration not idempotent.';Record 'migration_idempotent' $r
 $r=Call $f 'restore' 'None' (Join-Path $run 'missing_package');Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f) -and (Hash $f.legacy) -eq $legacyHash) 'Migrated restore failed.';Record 'migrated_restore_exact_without_package' $r
 foreach($problem in @('wrong_target','wrong_before','wrong_hash','registry_conflict')){
  $f=MakeLegacy ('migration_'+$problem);$legacy=Get-Content -LiteralPath $f.legacy -Raw -Encoding UTF8|ConvertFrom-Json
  switch($problem){
   'wrong_target'{$legacy.target=$legacy.target+'.other'}
   'wrong_before'{$legacy.entries[0].before.value='666'}
   'wrong_hash'{$legacy.modified_sha256=('0'*64)}
   'registry_conflict'{$j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$j.tokens[1].value='9;999';Save $j $f.registry}
  }
  Save $legacy $f.legacy;$regHash=Hash $f.registry;$xmlHash=Hash $f.xml;$legacyHash=Hash $f.legacy
  $r=Call $f 'migrate' 'None' $PackageRoot $f.legacy
  Assert ($r.exit -eq 1 -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $xmlHash -and (Hash $f.legacy) -eq $legacyHash -and -not (Test-Path -LiteralPath (Join-Path $f.root 'state\owner.json'))) ('Bad legacy accepted: '+$problem);Record ('migration_rejects_'+$problem) $r
 }
 $f=NewFixture 'interrupted';$r=Call $f 'install';Assert ($r.exit -eq 0) 'Setup failed.'
 $owner=Get-Content -LiteralPath (Join-Path $f.root 'state\owner.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $statePath=Join-Path $f.root ('state\installations\'+$owner.identity+'\state.json');$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 $state.status='APPLYING';Save $state $statePath
 $j=Get-Content -LiteralPath $f.registry -Raw -Encoding UTF8|ConvertFrom-Json;$entry=$state.entries[0];$v=@($j.tokens|Where-Object {$_.token -eq $entry.before.token})[0];$v.value=$entry.before.value;Save $j $f.registry
 $r=Call $f 'status';Assert ($r.exit -eq 2 -and $r.data.code -eq 'recovery_required') 'Interrupted state not detected.';Record 'interrupted_transaction_detected' $r
 $r=Call $f 'restore';Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Interrupted recovery failed.';Record 'interrupted_partial_recovery' $r
 $f=NewFixture 'concurrent';[void][IO.Directory]::CreateDirectory((Join-Path $f.root 'state'))
 $lock=[IO.File]::Open((Join-Path $f.root 'state\.global.lock'),'OpenOrCreate','ReadWrite','None')
 try{$r=Call $f 'install'}finally{$lock.Dispose()}
 Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Concurrent mutation ignored lock.';Record 'global_lock_rejects_concurrency' $r
 $f=NewFixture 'state_tamper';$r=Call $f 'install';Assert ($r.exit -eq 0) 'Setup failed.'
 $owner=Get-Content -LiteralPath (Join-Path $f.root 'state\owner.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $statePath=Join-Path $f.root ('state\installations\'+$owner.identity+'\state.json');$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 $state.entries[0].after.value='999';Save $state $statePath;$regHash=Hash $f.registry;$xmlHash=Hash $f.xml
 $r=Call $f 'restore';Assert ($r.exit -eq 1 -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $xmlHash) 'Tampered transition accepted.';Record 'tampered_journal_rejected' $r
 $f=NewFixture 'bad_backup';$r=Call $f 'install';Assert ($r.exit -eq 0) 'Setup failed.'
 $owner=Get-Content -LiteralPath (Join-Path $f.root 'state\owner.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $backup=Join-Path $f.root ('state\installations\'+$owner.identity+'\transactions\'+$owner.transaction+'\original.xml');[IO.File]::AppendAllText($backup,'bad',$utf8)
 $regHash=Hash $f.registry;$xmlHash=Hash $f.xml;$r=Call $f 'restore';Assert ($r.exit -eq 1 -and (Hash $f.registry) -eq $regHash -and (Hash $f.xml) -eq $xmlHash) 'Damaged backup used.';Record 'corrupt_backup_rejected' $r
 $f=NewFixture 'reparse';$alias=Join-Path $f.root 'alias';[void](New-Item -ItemType Junction -Path $alias -Target $f.game)
 $g=[pscustomobject]@{root=$f.root;game=$alias};$r=Call $g 'install';Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Reparse game accepted.';Record 'reparse_game_rejected' $r
 $f=NewFixture 'dtd';[IO.File]::WriteAllText($f.xml,'<!DOCTYPE SpeechRecognition [<!ENTITY test SYSTEM "file:///C:/Windows/win.ini">]><SpeechRecognition name="test">&test;</SpeechRecognition>',$utf8)
 $xmlHash=Hash $f.xml;$r=Call $f 'install';Assert ($r.exit -eq 1 -and (Hash $f.xml) -eq $xmlHash -and (SameRegistry $f)) 'DTD accepted.';Record 'xml_dtd_rejected' $r
 $oldPackage=[IO.Path]::GetFullPath($PreviousPackageRoot)
 $f=NewFixture 'dictionary_upgrade';$r=Call $f 'install' 'None' $oldPackage
 Assert ($r.exit -eq 0) 'Old dictionary setup failed.'
 $oldXml=Hash $f.xml;$oldRegistry=Hash $f.registry
 $ownerPath=Join-Path $f.root 'state\owner.json';$owner=Get-Content -LiteralPath $ownerPath -Raw -Encoding UTF8|ConvertFrom-Json
 $statePath=Join-Path $f.root ('state\installations\'+$owner.identity+'\state.json')
 $oldState=Hash $statePath;$before=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 $r=Call $f 'status';Assert ($r.exit -eq 2 -and $r.data.code -eq 'dictionary_update_available' -and $r.data.installed -and $r.data.canInstall -and -not $r.data.details.dictionaryMatchesPackage -and (Hash $f.xml) -eq $oldXml -and (Hash $statePath) -eq $oldState) 'Old dictionary falsely verified.';Record 'dictionary_status_detects_menu_mismatch_readonly' $r
 $r=Call $f 'verify';Assert ($r.exit -eq 2 -and $r.data.code -eq 'dictionary_update_available') 'Old dictionary verify falsely passed.';Record 'dictionary_verify_detects_menu_mismatch' $r
 $r=Call $f 'install';Assert ($r.exit -eq 0 -and $r.data.code -eq 'dictionary_updated' -and $r.data.details.registryWrites -eq 0 -and (Hash $f.registry) -eq $oldRegistry -and (Hash $f.xml) -ne $oldXml) ('Dictionary upgrade failed: '+$r.literal)
 $after=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 Assert ($after.originalHash -ceq $before.originalHash -and $after.transaction -ceq $before.transaction -and ($after.entries|ConvertTo-Json -Depth 10 -Compress) -ceq ($before.entries|ConvertTo-Json -Depth 10 -Compress)) 'Upgrade lost original restore chain.'
 $doc=New-Object Xml.XmlDocument;$doc.Load($f.xml);$map=Get-Content -LiteralPath (Join-Path $PackageRoot 'commands.ru.json') -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($k in $doc.SelectNodes('//Keyword')){if($k.GetAttribute('name') -cne 'default'){Assert ($k.GetAttribute('en') -ceq $map.($k.GetAttribute('name')) -and $k.GetAttribute('ru') -ceq $map.($k.GetAttribute('name'))) 'Upgraded phrase mismatches menu.'}}
 Record 'dictionary_upgrade_phrase_only_preserves_original_chain_and_registry' $r
 $newHash=Hash $f.xml;$r=Call $f 'install';Assert ($r.exit -eq 0 -and $r.data.code -eq 'verified' -and (Hash $f.xml) -eq $newHash) 'Dictionary update not idempotent.';Record 'dictionary_upgrade_idempotent' $r
 $r=Call $f 'restore' 'None' (Join-Path $run 'missing_package');Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Upgraded restore did not recover original XML and registry.';Record 'dictionary_upgrade_full_original_restore_without_package' $r
 foreach($point in @('AfterXmlWrite','AfterDictionaryStateWrite')){
  $f=NewFixture ('dictionary_failure_'+$point);$r=Call $f 'install' 'None' $oldPackage;Assert ($r.exit -eq 0) 'Old dictionary failure setup failed.'
  $owner=Get-Content -LiteralPath (Join-Path $f.root 'state\owner.json') -Raw -Encoding UTF8|ConvertFrom-Json
  $statePath=Join-Path $f.root ('state\installations\'+$owner.identity+'\state.json');$oldState=Hash $statePath;$oldXml=Hash $f.xml;$oldRegistry=Hash $f.registry
  $r=Call $f 'install' $point
  Assert ($r.exit -eq 1 -and $r.data.code -eq 'dictionary_update_failed_restored' -and (Hash $f.xml) -eq $oldXml -and (Hash $statePath) -eq $oldState -and (Hash $f.registry) -eq $oldRegistry) ('Dictionary exception did not restore byte exact: '+$r.literal);Record ('dictionary_failure_recovers_previous_'+$point) $r
 }
 $f=NewFixture 'dictionary_interrupted';$r=Call $f 'install' 'None' $oldPackage;Assert ($r.exit -eq 0) 'Interrupted dictionary setup failed.'
 $r=Call $f 'install';Assert ($r.exit -eq 0) 'Interrupted dictionary upgrade setup failed.'
 $owner=Get-Content -LiteralPath (Join-Path $f.root 'state\owner.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $installDir=Join-Path $f.root ('state\installations\'+$owner.identity);$statePath=Join-Path $installDir 'state.json';$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
 $state.status='UPDATING_DICTIONARY';Save $state $statePath
 Copy-Item -LiteralPath (Join-Path $installDir ('transactions\'+$state.transaction+'\'+$state.dictionaryUpdate.backupFile)) -Destination $f.xml -Force
 $r=Call $f 'status';Assert ($r.exit -eq 2 -and $r.data.code -eq 'recovery_required') 'Interrupted dictionary update not detected.';Record 'dictionary_interrupted_detected' $r
 $r=Call $f 'restore' 'None' (Join-Path $run 'missing_package');Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f)) 'Interrupted dictionary restore failed.';Record 'dictionary_interrupted_original_restore' $r
 $f=MakeLegacy 'legacy_old_dictionary_import' $oldPackage;$oldRegistry=Hash $f.registry;$oldXml=Hash $f.xml;$oldLegacy=Hash $f.legacy
 $r=Call $f 'migrate' 'None' $PackageRoot $f.legacy
 Assert ($r.exit -eq 0 -and $r.data.code -eq 'dictionary_update_available' -and $r.data.canInstall -and (Hash $f.xml) -eq $oldXml -and (Hash $f.registry) -eq $oldRegistry -and (Hash $f.legacy) -eq $oldLegacy) ('Known legacy old dictionary import failed: '+$r.literal);Record 'known_legacy_old_dictionary_import_readonly' $r
 $r=Call $f 'install';Assert ($r.exit -eq 0 -and $r.data.code -eq 'dictionary_updated' -and (Hash $f.registry) -eq $oldRegistry) 'Imported old dictionary update failed.';Record 'known_legacy_old_dictionary_updates_after_explicit_import' $r
 $r=Call $f 'restore' 'None' (Join-Path $run 'missing_package');Assert ($r.exit -eq 0 -and (Hash $f.xml) -eq $f.originalHash -and (SameRegistry $f) -and (Hash $f.legacy) -eq $oldLegacy) 'Imported old dictionary lost original restore.';Record 'known_legacy_old_dictionary_restores_original' $r
 Save ([pscustomobject]@{status='PASS';count=$results.Count;fixtureRoot=$run;liveGameWrites=0;realRegistryWrites=0;tests=$results.ToArray()}) $ReportPath
 Write-Output ('ENGINE TESTS PASS; tests='+$results.Count+'; live_game_writes=0; real_registry_writes=0; fixture_root='+$run)
 exit 0
}catch{
 Save ([pscustomobject]@{status='FAIL';count=$results.Count;fixtureRoot=$run;error=$_.Exception.Message;tests=$results.ToArray()}) $ReportPath
 Write-Output ('ENGINE TESTS FAIL; '+$_.Exception.Message);exit 1
}
