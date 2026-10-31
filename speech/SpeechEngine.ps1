[CmdletBinding()]
param(
 [ValidateSet('status','install','restore','verify','migrate')][string]$Action='status',
 [Parameter(Mandatory=$true)][string]$GamePath,
 [string]$PackageRoot,
 [string]$FixtureRoot,
 [string]$LegacyState,
 [ValidateSet('None','AfterRegistryWrite','AfterXmlWrite','AfterDictionaryStateWrite')][string]$FixtureFailPoint='None'
)
# A PS7 launcher may pass its PSModulePath into Windows PowerShell 5.1.
# Resolve native modules from this interpreter rather than inherited search paths.
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) -ErrorAction Stop
if([string]::IsNullOrWhiteSpace($PackageRoot)){$PackageRoot=$PSScriptRoot}
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$script:utf8=New-Object Text.UTF8Encoding($false)
$script:registryPath='SOFTWARE\Microsoft\Speech Server\v11.0\Recognizers\Tokens'
$script:ruToken='SR_MS_ru-RU_TELE_11.0'
$script:fixture=-not [string]::IsNullOrWhiteSpace($FixtureRoot)
$script:modelInstalled=$false
$script:gameRunning=$false
$script:commandCount=37
$script:lock=$null
$script:tx=$null
$script:owner=$null
$script:failureUsed=$false
$script:migrationDictionaryMatches=$true
$script:reviewedLegacyOriginal='6af1b45b4c4e2aff43a5b55b922db0735652313155e12fb41eb5150858476882'
$script:reviewedLegacyDictionary='28149becebd907103452a3e946aeca6b495ec47dbad09822c4e1e86e556e3074'

function HashBytes([byte[]]$Bytes){
 $sha=[Security.Cryptography.SHA256]::Create()
 try{return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
}
function HashFile([string]$Path){
 $sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path)
 try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()}
}
function CheckedPath([string]$Path){
 if([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)){throw 'An absolute path is required.'}
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 if($full.StartsWith('\\') -or $full.StartsWith('\\?\') -or $full.Substring(2).Contains(':')){throw 'Only local drive paths without alternate streams are supported.'}
 $cursor=$full
 while(-not [string]::IsNullOrWhiteSpace($cursor)){
  if([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)){
   if(([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Reparse path rejected: '+$cursor)}
  }
  $parent=[IO.Path]::GetDirectoryName($cursor)
  if($parent -eq $cursor){break};$cursor=$parent
 }
 return $full
}
function Within([string]$Child,[string]$Parent){return $Child.StartsWith($Parent.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)}
function AtomicBytes([byte[]]$Bytes,[string]$Path){
 [void](CheckedPath $Path)
 $tmp=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp';$old=$tmp+'.previous'
 try{
  [IO.File]::WriteAllBytes($tmp,$Bytes)
  if([IO.File]::Exists($Path)){[IO.File]::Replace($tmp,$Path,$old);[IO.File]::Delete($old)}else{[IO.File]::Move($tmp,$Path)}
 }finally{if([IO.File]::Exists($tmp)){[IO.File]::Delete($tmp)}}
}
function SaveJson($Value,[string]$Path){AtomicBytes ($script:utf8.GetBytes(($Value | ConvertTo-Json -Depth 24 -Compress))) $Path}
function LoadJson([string]$Path){
 [void](CheckedPath $Path)
 if((Get-Item -LiteralPath $Path).Length -gt 4194304){throw 'JSON exceeds 4 MiB.'}
 return ([IO.File]::ReadAllText($Path,$script:utf8) | ConvertFrom-Json)
}
function ReadXml([string]$Path){
 if((Get-Item -LiteralPath $Path).Length -gt 1048576){throw 'Speech XML exceeds 1 MiB.'}
 $s=New-Object Xml.XmlReaderSettings;$s.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$s.XmlResolver=$null;$s.MaxCharactersInDocument=1048576
 $r=[Xml.XmlReader]::Create($Path,$s);$d=New-Object Xml.XmlDocument;$d.PreserveWhitespace=$true;$d.XmlResolver=$null
 try{$d.Load($r)}finally{$r.Dispose()}
 if($d.DocumentElement.LocalName -cne 'SpeechRecognition'){throw 'Unexpected speech XML root.'}
 return ,$d
}
function PrepareXml([string]$Source,[string]$Dictionary){
 $j=LoadJson $Dictionary;$map=@{}
 foreach($p in $j.PSObject.Properties){
  if($p.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($p.Value) -or $p.Value.Length -gt 160 -or $p.Value -match '[\x00-\x1f]'){throw ('Invalid speech phrase: '+$p.Name)}
  $map[$p.Name]=$p.Value
 }
 if($map.Count -lt 1 -or $map.Count -gt 256){throw 'Command dictionary size is invalid.'}
 $original=ReadXml $Source;$doc=$original.CloneNode($true);$used=@{};$rows=0
 foreach($k in $doc.SelectNodes('//Keyword')){
  $name=$k.GetAttribute('name');if($name -ceq 'default'){continue}
  if(-not $map.ContainsKey($name)){throw ('Unknown speech command: '+$name)}
  $k.SetAttribute('en',$map[$name]);$k.SetAttribute('ru',$map[$name]);$used[$name]=$true;$rows++
 }
 if($rows -eq 0 -or @($map.Keys | Where-Object {-not $used.ContainsKey($_)}).Count -ne 0){throw 'Game speech commands do not match the dictionary.'}
 foreach($list in $doc.SelectNodes('//KeywordList')){
  $seen=@{}
  foreach($k in $list.SelectNodes('./Keyword')){
   if($k.GetAttribute('name') -ceq 'default'){continue}
   $phrase=$k.GetAttribute('en').Trim().ToLowerInvariant().Replace([char]0x451,[char]0x435)
   if($seen.ContainsKey($phrase)){throw 'Duplicate speech phrase within one context.'};$seen[$phrase]=$true
  }
 }
 $a=$original.CloneNode($true);$b=$doc.CloneNode($true)
 foreach($d in @($a,$b)){foreach($k in $d.SelectNodes('//Keyword')){if($k.GetAttribute('name') -cne 'default'){$k.RemoveAttribute('en');$k.RemoveAttribute('ru')}}}
 if($a.OuterXml -cne $b.OuterXml){throw 'Non-phrase speech content changed.'}
 $stream=New-Object IO.MemoryStream;$settings=New-Object Xml.XmlWriterSettings
 $settings.Encoding=$script:utf8;$settings.Indent=$false;$settings.NewLineHandling=[Xml.NewLineHandling]::None
 $writer=[Xml.XmlWriter]::Create($stream,$settings)
 try{$doc.Save($writer);$writer.Flush();$bytes=$stream.ToArray()}finally{$writer.Dispose();$stream.Dispose()}
 $script:commandCount=$used.Count
 return ,$bytes
}
function ReadTokens{
 if($script:fixture){
  $parsed=LoadJson $script:registryFile
  if(-not $parsed.runtime){return @()}
  $rows=@($parsed.tokens)
 }else{
  $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64');$key=$null;$rows=@()
  try{
   $key=$base.OpenSubKey($script:registryPath)
   if($null -eq $key){return @()}
   foreach($name in $key.GetSubKeyNames()){
    $attr=$key.OpenSubKey($name+'\Attributes')
    try{
     if($null -eq $attr){continue}
     $exists=$attr.GetValueNames() -contains 'Language';$kind=$null;$value=$null
     if($exists){$kind=$attr.GetValueKind('Language').ToString();$value=$attr.GetValue('Language',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}
     $rows += [pscustomobject]@{token=$name;exists=$exists;kind=$kind;value=$value}
    }finally{if($attr){$attr.Dispose()}}
   }
  }finally{if($key){$key.Dispose()};$base.Dispose()}
 }
 if($rows.Count -gt 256){throw 'Recognizer token count exceeds limit.'}
 $seen=@{}
 foreach($r in $rows){if($r.token -notmatch '^[A-Za-z0-9_.-]{1,160}$' -or $seen.ContainsKey($r.token)){throw 'Invalid or duplicate recognizer token.'};$seen[$r.token]=$true}
 return $rows
}
function SameValue($A,$B){
 if($A.exists -ne $B.exists -or $A.kind -cne $B.kind){return $false}
 return (($A.value | ConvertTo-Json -Compress -Depth 8) -ceq ($B.value | ConvertTo-Json -Compress -Depth 8))
}
function SetToken($Wanted,$Expected){
 $rows=@(ReadTokens);$now=@($rows | Where-Object {$_.token -ceq $Wanted.token})
 if($now.Count -ne 1 -or -not (SameValue $now[0] $Expected)){throw ('Registry write conflict: '+$Wanted.token)}
 if($script:fixture){
  $j=LoadJson $script:registryFile;$match=@($j.tokens | Where-Object {$_.token -ceq $Wanted.token})
  $match[0].exists=$Wanted.exists;$match[0].kind=$Wanted.kind;$match[0].value=$Wanted.value
  SaveJson $j $script:registryFile;return
 }
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64');$key=$null
 try{
  $key=$base.OpenSubKey($script:registryPath+'\'+$Wanted.token+'\Attributes',$true)
  if($null -eq $key){throw 'Recognizer disappeared.'}
  if($Wanted.exists){
   $value=$Wanted.value
   switch($Wanted.kind){'Binary'{$value=[byte[]]$value};'MultiString'{$value=[string[]]$value};'DWord'{$value=[int]$value};'QWord'{$value=[long]$value}}
   $key.SetValue('Language',$value,[Enum]::Parse([Microsoft.Win32.RegistryValueKind],$Wanted.kind))
  }else{$key.DeleteValue('Language',$false)}
 }finally{if($key){$key.Dispose()};$base.Dispose()}
}
function Languages($Token){
 if(-not $Token.exists -or $Token.kind -notin @('String','ExpandString')){return @()}
 $out=@()
 foreach($s in ([string]$Token.value -split ';')){
  $n=$s.Trim();if($n -notmatch '^[0-9A-Fa-f]{1,8}$'){continue}
  $out += [Convert]::ToUInt32($n,16)
 }
 return $out
}
function RouteReady($Rows){
 foreach($lcid in @(0x409,0x809)){
  $matched=@($Rows | Where-Object {$lcid -in @(Languages $_)})
  if($matched.Count -ne 1 -or $matched[0].token -cne $script:ruToken){return $false}
 }
 $ru=@($Rows | Where-Object {$_.token -ceq $script:ruToken})
 return ($ru.Count -eq 1 -and 0x419 -in @(Languages $ru[0]))
}
function PlanRegistry($Rows){
 $ru=@($Rows | Where-Object {$_.token -ceq $script:ruToken})
 if($ru.Count -ne 1 -or -not $ru[0].exists -or $ru[0].kind -notin @('String','ExpandString') -or 0x419 -notin @(Languages $ru[0])){throw 'Russian Speech Platform 11 model with native 419 language is required.'}
 $entries=@()
 foreach($row in $Rows){
  $isRu=$row.token -ceq $script:ruToken;$aliases=@(Languages $row | Where-Object {$_ -in @(0x409,0x809)})
  if(-not $isRu -and $aliases.Count -eq 0){continue}
  if(-not $isRu -and $row.token -notmatch '(^|_)en[-_]'){throw ('English alias belongs to an unexpected recognizer: '+$row.token)}
  if(-not $row.exists -or $row.kind -notin @('String','ExpandString')){throw 'Unsupported Language value type.'}
  $keep=@()
  foreach($part in ([string]$row.value -split ';')){
   $p=$part.Trim();if($p -notmatch '^[0-9a-fA-F]{1,8}$'){throw 'Malformed Language list.'}
   if([Convert]::ToUInt32($p,16) -notin @(0x409,0x809)){$keep+=$part}
  }
  if($isRu){$keep+=@('409','809')}
  $value=if($keep.Count -eq 0){'0'}else{$keep -join ';'}
  $after=[pscustomobject]@{token=$row.token;exists=$true;kind=$row.kind;value=$value}
  if(-not (SameValue $row $after)){$entries += [pscustomobject]@{before=$row;after=$after}}
 }
 return $entries
}
function SaveState($State){SaveJson $State $script:stateFile}
function LoadState{
 if(-not [IO.File]::Exists($script:stateFile)){return $null}
 $s=LoadJson $script:stateFile
 if($s.schema -ne 1 -or $s.identity -cne $script:identity -or $s.gamePath -ine $script:game -or $s.transaction -notmatch '^[0-9a-f]{32}$' -or $s.originalHash -notmatch '^[0-9a-f]{64}$' -or $s.modifiedHash -notmatch '^[0-9a-f]{64}$' -or $s.targetRelative -cne 'audio/2025/speech_recognition.xml' -or $s.commandCount -lt 1 -or $s.commandCount -gt 256){throw 'Saved transaction identity is invalid.'}
 if($s.status -notin @('PREPARED','APPLYING','APPLIED','UPDATING_DICTIONARY','RESTORING','RESTORED','ROLLBACK_REQUIRED','MIGRATION_FAILED')){throw 'Saved transaction status is invalid.'}
 if($s.PSObject.Properties.Name -contains 'dictionaryUpdate'){
  $u=$s.dictionaryUpdate
  if($u.previousHash -notmatch '^[0-9a-f]{64}$' -or $u.backupFile -notmatch '^dictionary_[0-9a-f]{32}\.xml$'){throw 'Saved dictionary update is invalid.'}
 }
 $seen=@{}
 foreach($e in @($s.entries)){
  if($e.before.token -cne $e.after.token -or $e.before.token -notmatch '^[A-Za-z0-9_.-]{1,160}$' -or $seen.ContainsKey($e.before.token)){throw 'Saved token identity is invalid.'}
  $seen[$e.before.token]=$true
 }
 $recomputed=@(PlanRegistry @($s.entries | ForEach-Object {$_.before}))
 if($recomputed.Count -ne @($s.entries).Count){throw 'Saved Language transition count is invalid.'}
 foreach($e in @($s.entries)){
  $want=@($recomputed | Where-Object {$_.before.token -ceq $e.before.token})
  if($want.Count -ne 1 -or -not (SameValue $e.after $want[0].after)){throw 'Saved Language transition is invalid.'}
 }
 $script:commandCount=[int]$s.commandCount
 return $s
}
function BackupPath($State){return (CheckedPath (Join-Path (Join-Path (Join-Path $script:installDir 'transactions') $State.transaction) 'original.xml'))}
function LoadOwner{
 if(-not [IO.File]::Exists($script:ownerFile)){return $null}
 $o=LoadJson $script:ownerFile
 if($o.schema -ne 1 -or $o.identity -notmatch '^[0-9a-f]{64}$' -or $o.transaction -notmatch '^[0-9a-f]{32}$'){throw 'Global ownership record is invalid.'}
 return $o
}
function CheckOwner($State){
 $o=LoadOwner
 if($null -eq $o -or $o.identity -cne $script:identity -or $o.transaction -cne $State.transaction){throw 'Global speech routing belongs to a different or missing transaction.'}
}
function VerifyApplied($State){
 CheckOwner $State
 if((HashFile $script:xml) -cne $State.modifiedHash){throw 'Game speech XML changed externally.'}
 $rows=@(ReadTokens)
 foreach($e in @($State.entries)){
  $now=@($rows | Where-Object {$_.token -ceq $e.after.token})
  if($now.Count -ne 1 -or -not (SameValue $now[0] $e.after)){throw ('Recognizer changed externally: '+$e.after.token)}
 }
 if(-not (RouteReady $rows)){throw 'English-to-Russian recognizer routing is incomplete or ambiguous.'}
 $backup=BackupPath $State
 if(-not [IO.File]::Exists($backup) -or (HashFile $backup) -cne $State.originalHash){throw 'Original speech XML backup is missing or damaged.'}
}
function DictionaryMatches([byte[]]$Candidate){
 $expected=New-Object Xml.XmlDocument;$expected.PreserveWhitespace=$true;$expected.XmlResolver=$null
 $expected.LoadXml($script:utf8.GetString($Candidate));$actual=ReadXml $script:xml
 return $actual.OuterXml -ceq $expected.OuterXml
}
function DictionaryBackup($State){
 if(-not ($State.PSObject.Properties.Name -contains 'dictionaryUpdate')){throw 'Missing dictionary update journal.'}
 $u=$State.dictionaryUpdate
 if($u.previousHash -notmatch '^[0-9a-f]{64}$' -or $u.backupFile -notmatch '^dictionary_[0-9a-f]{32}\.xml$'){throw 'Invalid dictionary update journal.'}
 return (CheckedPath (Join-Path ([IO.Path]::GetDirectoryName((BackupPath $State))) $u.backupFile))
}
function UpdateDictionary($State,[byte[]]$Candidate){
 VerifyApplied $State
 $oldXml=[IO.File]::ReadAllBytes($script:xml);$oldState=[IO.File]::ReadAllBytes($script:stateFile)
 $oldXmlHash=HashBytes $oldXml;$oldStateHash=HashBytes $oldState;$newHash=HashBytes $Candidate
 if($oldXmlHash -cne $State.modifiedHash){throw 'Dictionary update source changed.'}
 $updated=LoadJson $script:stateFile
 $updated.modifiedHash=$newHash;$updated.commandCount=$script:commandCount;$updated.status='UPDATING_DICTIONARY'
 $history=[pscustomobject]@{previousHash=$oldXmlHash;backupFile=('dictionary_'+[guid]::NewGuid().ToString('N')+'.xml')}
 $updated | Add-Member -NotePropertyName dictionaryUpdate -NotePropertyValue $history -Force
 $historyPath=DictionaryBackup $updated
 AtomicBytes $oldXml $historyPath
 if((HashFile $historyPath) -cne $oldXmlHash){throw 'Dictionary rollback copy mismatch.'}
 $knownStateHashes=@($oldStateHash)
 try{
  if((HashFile $script:stateFile) -cne $oldStateHash){throw 'Dictionary journal changed before update.'}
  if(($script:fixture -and [IO.File]::Exists((Join-Path $script:fixtureRoot 'game.running'))) -or (-not $script:fixture -and @(Get-Process -Name F1_25 -ErrorAction SilentlyContinue).Count -gt 0)){throw 'Close F1 25 before updating phrases.'}
  $knownStateHashes+=HashBytes ($script:utf8.GetBytes(($updated | ConvertTo-Json -Depth 24 -Compress)))
  SaveState $updated
  ReplaceGame $Candidate $oldXmlHash;FailPoint 'AfterXmlWrite'
  $updated.status='APPLIED'
  $knownStateHashes+=HashBytes ($script:utf8.GetBytes(($updated | ConvertTo-Json -Depth 24 -Compress)))
  SaveState $updated
  FailPoint 'AfterDictionaryStateWrite'
  VerifyApplied $updated
  if(-not (DictionaryMatches $Candidate)){throw 'Updated phrases do not match this package.'}
 }catch{
  $failure=$_.Exception.Message
  try{
   $now=HashFile $script:xml
   if($now -notin @($oldXmlHash,$newHash)){throw 'XML changed externally during update.'}
   if($now -cne $oldXmlHash){ReplaceGame $oldXml $now}
   if((HashFile $script:stateFile) -notin $knownStateHashes){throw 'Dictionary journal changed externally during recovery.'}
   AtomicBytes $oldState $script:stateFile
   VerifyApplied $State
   if((HashFile $script:xml) -cne $oldXmlHash -or (HashFile $script:stateFile) -cne $oldStateHash){throw 'Dictionary recovery verification failed.'}
  }catch{throw ('Dictionary update failed: '+$failure+'; recovery requires attention: '+$_.Exception.Message)}
  Result 'rolled_back' 'dictionary_update_failed_restored' ('Обновление фраз прервано; прежние команды и журнал восстановлены. '+$failure) 1 $true $true $true @{registryWrites=0;xml='previous_byte_exact';journal='previous_byte_exact';dictionaryMatchesPackage=$false}
 }
 $script:tx=$updated
 Result 'installed' 'dictionary_updated' 'Фразы обновлены: произносите команды как написано в русском радиоменю. Исходный откат сохранён; языковые настройки не изменялись.' 0 $true $false $true @{registryWrites=0;dictionaryMatchesPackage=$true;xml='en,ru';japanese='unchanged';microphoneTest='not_performed_by_module'}
}
function ReplaceGame([byte[]]$Bytes,[string]$ExpectedHash){
 [void](CheckedPath $script:xml)
 if((HashFile $script:xml) -cne $ExpectedHash){throw 'Game XML write conflict.'}
 $tmp=$script:xml+'.'+[guid]::NewGuid().ToString('N')+'.speech.tmp';$old=$tmp+'.previous'
 try{
  [IO.File]::WriteAllBytes($tmp,$Bytes)
  if((HashFile $script:xml) -cne $ExpectedHash){throw 'Game XML changed during transaction.'}
  [IO.File]::Replace($tmp,$script:xml,$old)
  if((HashFile $script:xml) -cne (HashBytes $Bytes)){throw 'Game XML post-write verification failed.'}
  [IO.File]::Delete($old)
 }finally{if([IO.File]::Exists($tmp)){[IO.File]::Delete($tmp)}}
}
function RestoreTransaction($State){
 CheckOwner $State
 $backup=BackupPath $State
 if(-not [IO.File]::Exists($backup) -or (HashFile $backup) -cne $State.originalHash){throw 'Original XML backup is missing or damaged.'}
 $h=HashFile $script:xml
 $allowedHashes=@($State.originalHash,$State.modifiedHash)
 if($State.status -in @('UPDATING_DICTIONARY','ROLLBACK_REQUIRED') -and $State.PSObject.Properties.Name -contains 'dictionaryUpdate'){
  $history=DictionaryBackup $State
  if(-not [IO.File]::Exists($history) -or (HashFile $history) -cne $State.dictionaryUpdate.previousHash){throw 'Dictionary recovery backup mismatch.'}
  $allowedHashes+=$State.dictionaryUpdate.previousHash
 }
 if($h -notin $allowedHashes){throw 'Restore conflict: game XML has external changes.'}
 $rows=@(ReadTokens)
 foreach($e in @($State.entries)){
  $now=@($rows | Where-Object {$_.token -ceq $e.before.token})
  if($now.Count -ne 1 -or (-not (SameValue $now[0] $e.before) -and -not (SameValue $now[0] $e.after))){throw ('Restore conflict: '+$e.before.token)}
 }
 $State.status='RESTORING';SaveState $State
 foreach($e in @($State.entries)){
  $current=@(ReadTokens | Where-Object {$_.token -ceq $e.before.token})[0]
  if(-not (SameValue $current $e.before)){SetToken $e.before $current}
 }
 if($h -cne $State.originalHash){ReplaceGame ([IO.File]::ReadAllBytes($backup)) $h}
 if((HashFile $script:xml) -cne $State.originalHash){throw 'Restore XML verification failed.'}
 $rows=@(ReadTokens)
 foreach($e in @($State.entries)){
  $current=@($rows | Where-Object {$_.token -ceq $e.before.token})
  if($current.Count -ne 1 -or -not (SameValue $current[0] $e.before)){throw 'Restore registry verification failed.'}
 }
 $State.status='RESTORED';SaveState $State
 CheckOwner $State;[IO.File]::Delete($script:ownerFile)
}
function Result([string]$Status,[string]$Code,[string]$Message,[int]$ExitCode,[bool]$Installed=$false,[bool]$CanInstall=$false,[bool]$CanRestore=$false,$Details=$null){
 $checks=[ordered]@{verificationScope='configuration_only';xmlAndRegistryVerified=$Installed;nativeRecognizerInstantiation='not_performed';microphoneCapture='not_performed';allCommandsInGame='not_performed'}
 if($null -ne $Details){foreach($item in $Details.GetEnumerator()){$checks[$item.Key]=$item.Value}}
 $value=[ordered]@{schema=1;status=$Status;code=$Code;message=$Message;modelInstalled=$script:modelInstalled;installed=$Installed;needsRestart=($Action -in @('install','restore') -and $ExitCode -eq 0);gameRunning=$script:gameRunning;canInstall=$CanInstall;canRestore=$CanRestore;commandCount=$script:commandCount;details=$checks}
 [Console]::Out.WriteLine(($value | ConvertTo-Json -Compress -Depth 12))
 if($script:lock){$script:lock.Dispose();$script:lock=$null}
 exit $ExitCode
}
function FailPoint([string]$Name){if($script:fixture -and -not $script:failureUsed -and $FixtureFailPoint -ceq $Name){$script:failureUsed=$true;throw ('Injected fixture failure: '+$Name)}}
function MigrateLegacy($Rows){
 $path=CheckedPath $LegacyState
 if($script:fixture -and -not (Within $path $script:fixtureRoot)){throw 'Fixture legacy state must be inside FixtureRoot.'}
 $legacy=LoadJson $path
 if($legacy.schema -ne 1 -or $legacy.status -cne 'APPLIED' -or $legacy.scope -cne 'live' -or $legacy.target -ine $script:xml -or $legacy.original_sha256 -notmatch '^[0-9a-f]{64}$' -or $legacy.modified_sha256 -notmatch '^[0-9a-f]{64}$'){throw 'Legacy state identity or status is invalid.'}
 $oldOriginal=CheckedPath (Join-Path ([IO.Path]::GetDirectoryName($path)) 'ORIGINAL.xml')
 if((HashFile $oldOriginal) -cne $legacy.original_sha256 -or (HashFile $script:xml) -cne $legacy.modified_sha256){throw 'Legacy original or current XML hash mismatch.'}
 $allowed=@{
  'SR_MS_en-GB_Kinect_11.0'=@('809;9','9')
  'SR_MS_en-US_Kinect_11.0'=@('409;9','9')
  'SR_MS_ru-RU_TELE_11.0'=@('419','419;409;809')
 }
 $entries=@($legacy.entries);$seen=@{}
 if($entries.Count -ne 3){throw 'Legacy migration requires exactly the three known Language transitions.'}
 foreach($e in $entries){
  $name=$e.before.token
  if(-not $allowed.ContainsKey($name) -or $seen.ContainsKey($name) -or $e.after.token -cne $name -or -not $e.before.exists -or -not $e.after.exists -or $e.before.kind -cne 'String' -or $e.after.kind -cne 'String' -or $e.before.value -cne $allowed[$name][0] -or $e.after.value -cne $allowed[$name][1]){throw 'Legacy registry transition is not the reviewed English setup.'}
  $current=@($Rows | Where-Object {$_.token -ceq $name})
  if($current.Count -ne 1 -or -not (SameValue $current[0] $e.after)){throw ('Legacy registry conflict: '+$name)}
  $seen[$name]=$true
 }
 if(-not (RouteReady $Rows)){throw 'Legacy English routes are incomplete or ambiguous.'}
 foreach($u in @($legacy.untouched)){
  $current=@($Rows | Where-Object {$_.token -ceq $u.token})
  if($current.Count -ne 1 -or -not (SameValue $current[0] $u)){throw ('Legacy unrelated recognizer changed: '+$u.token)}
 }
 $package=CheckedPath $PackageRoot;$dictionary=CheckedPath (Join-Path $package 'commands.ru.json')
 $expectedBytes=PrepareXml $oldOriginal $dictionary
 $expected=New-Object Xml.XmlDocument;$expected.PreserveWhitespace=$true;$expected.XmlResolver=$null
 $expected.LoadXml($script:utf8.GetString($expectedBytes));$actual=ReadXml $script:xml
 if($actual.OuterXml -cne $expected.OuterXml){
  # This exact previous dictionary was independently inspected before release.
  if($legacy.original_sha256 -cne $script:reviewedLegacyOriginal -or $legacy.modified_sha256 -cne $script:reviewedLegacyDictionary){throw 'Legacy XML does not contain the reviewed en+ru phrase-only changes.'}
  $script:migrationDictionaryMatches=$false
 }
 # Import journals only. Migration never replays registry writes or changes game XML.
 $transaction=[guid]::NewGuid().ToString('N');$dir=CheckedPath (Join-Path (Join-Path $script:installDir 'transactions') $transaction)
 [void][IO.Directory]::CreateDirectory($dir)
 $state=[pscustomobject]@{schema=1;identity=$script:identity;gamePath=$script:game;transaction=$transaction;status='PREPARED';createdUtc=[DateTime]::UtcNow.ToString('o');originalHash=$legacy.original_sha256;modifiedHash=$legacy.modified_sha256;commandCount=$script:commandCount;entries=$entries;targetRelative='audio/2025/speech_recognition.xml';japanese='unchanged';russianModelRetained=$true;importedLegacyState=$path;importedLegacyHash=(HashFile $path)}
 AtomicBytes ([IO.File]::ReadAllBytes($oldOriginal)) (BackupPath $state)
 SaveState $state
 try{
  SaveJson ([pscustomobject]@{schema=1;identity=$script:identity;gamePath=$script:game;transaction=$transaction}) $script:ownerFile
  VerifyApplied $state
  $state.status='APPLIED';SaveState $state
 }catch{
  $o=LoadOwner
  if($null -ne $o -and $o.identity -ceq $script:identity -and $o.transaction -ceq $transaction){[IO.File]::Delete($script:ownerFile)}
  $state.status='MIGRATION_FAILED';SaveState $state
  throw
 }
 $script:tx=$state
}

try{
 if(-not [Environment]::Is64BitProcess){throw 'Run with 64-bit Windows PowerShell.'}
 if(-not $script:fixture -and $FixtureFailPoint -ne 'None'){throw 'Failure injection is restricted to fixture mode.'}
 $script:game=CheckedPath $GamePath
 if(-not [IO.File]::Exists((CheckedPath (Join-Path $script:game 'F1_25.exe')))){throw 'Selected folder does not contain F1_25.exe.'}
 $script:xml=CheckedPath (Join-Path $script:game 'audio\2025\speech_recognition.xml')
 if(-not [IO.File]::Exists($script:xml)){throw 'Game speech_recognition.xml is missing.'}
 [void](ReadXml $script:xml)
 $script:identity=HashBytes ($script:utf8.GetBytes($script:game.ToUpperInvariant()))
 if($script:fixture){
  $script:fixtureRoot=CheckedPath $FixtureRoot
  if(-not (Within $script:game $script:fixtureRoot)){throw 'Fixture game must be inside FixtureRoot.'}
  $script:stateRoot=CheckedPath (Join-Path $script:fixtureRoot 'state')
  $script:registryFile=CheckedPath (Join-Path $script:fixtureRoot 'registry.json')
  $script:gameRunning=[IO.File]::Exists((Join-Path $script:fixtureRoot 'game.running'))
 }else{
  $script:stateRoot=CheckedPath (Join-Path $env:ProgramData 'Karsvein\F1Speech')
  $script:gameRunning=@(Get-Process -Name F1_25 -ErrorAction SilentlyContinue).Count -gt 0
 }
 $script:ownerFile=CheckedPath (Join-Path $script:stateRoot 'owner.json')
 $script:installDir=CheckedPath (Join-Path (Join-Path $script:stateRoot 'installations') $script:identity)
 $script:stateFile=CheckedPath (Join-Path $script:installDir 'state.json')
 $mutating=$Action -in @('install','restore','migrate')
 if($mutating){
  if($script:gameRunning){Result 'not_ready' 'game_running' 'Закройте F1 25 перед изменением голосовых команд.' 2}
  if(-not $script:fixture){
   $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
   if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){Result 'not_ready' 'administrator_required' 'Для настройки системного распознавания нужны права администратора.' 2}
  }
  [void][IO.Directory]::CreateDirectory($script:stateRoot)
  $script:lock=[IO.File]::Open((CheckedPath (Join-Path $script:stateRoot '.global.lock')),'OpenOrCreate','ReadWrite','None')
 }
 $rows=@(ReadTokens);$model=@($rows | Where-Object {$_.token -ceq $script:ruToken})
 $script:modelInstalled=$model.Count -eq 1
 $script:owner=LoadOwner;$script:tx=LoadState
 if($null -ne $script:owner -and $script:owner.identity -cne $script:identity){Result 'not_ready' 'owned_by_other_installation' 'Голосовой движок настроен для другой папки игры. Сначала восстановите её настройки.' 2 $false $false $false @{ownerGame=$script:owner.gamePath}}
 if($null -ne $script:owner -and ($null -eq $script:tx -or $script:owner.transaction -cne $script:tx.transaction)){throw 'Ownership and installation journal disagree.'}
 if($Action -eq 'restore'){
  if($null -eq $script:owner){
   if($null -ne $script:tx -and $script:tx.status -eq 'RESTORED'){Result 'restored' 'already_restored' 'Исходные настройки уже восстановлены.' 0 $false $script:modelInstalled}
   Result 'not_ready' 'no_owned_installation' 'Этот модуль не создавал текущую настройку; её автоматическое восстановление не выполняется.' 2
  }
  RestoreTransaction $script:tx
  Result 'restored' 'restored' 'Исходный XML и значения языков восстановлены. Русская модель оставлена установленной.' 0 $false $script:modelInstalled $false @{xml='byte_exact';registry='original_types_and_values';model='retained'}
 }
 if($null -ne $script:owner -and $script:tx.status -ne 'APPLIED'){
  Result 'not_ready' 'recovery_required' 'Незавершённая настройка. Выполните восстановление перед новой установкой.' 2 $false $false (-not $script:gameRunning) @{transactionStatus=$script:tx.status}
 }
 if($null -ne $script:owner){
  try{VerifyApplied $script:tx}catch{Result 'conflict' 'external_changes' $_.Exception.Message 1 $false $false (-not $script:gameRunning)}
  $package=CheckedPath $PackageRoot;$dictionary=CheckedPath (Join-Path $package 'commands.ru.json')
  $candidate=PrepareXml $script:xml $dictionary
  if(-not (DictionaryMatches $candidate)){
   if($Action -eq 'install'){UpdateDictionary $script:tx $candidate}
   Result 'update_available' 'dictionary_update_available' 'Настройка сохранена, но фразы относятся к предыдущему словарю. Обновите их, чтобы говорить точно как в русском радиоменю.' 2 $true (-not $script:gameRunning) (-not $script:gameRunning) @{dictionaryMatchesPackage=$false;registryWrites=0;xml='previous_dictionary';microphoneTest='not_performed_by_module'}
  }
  Result 'installed' 'verified' 'Русские команды совпадают со словарём этого выпуска. В заезде удерживайте T (русская Е) и произносите фразу из русского меню.' 0 $true $false (-not $script:gameRunning) @{dictionaryMatchesPackage=$true;xml='en,ru';language='419;409;809';japanese='unchanged';microphoneTest='not_performed_by_module'}
 }
 if($Action -eq 'migrate'){
  MigrateLegacy $rows
  if(-not $script:migrationDictionaryMatches){Result 'update_available' 'dictionary_update_available' 'Прежняя проверенная настройка импортирована без изменения игры и реестра. Теперь нажмите «Обновить фразы из меню».' 0 $true $true $true @{xmlWrites=0;registryWrites=0;legacyFiles='unchanged';dictionaryMatchesPackage=$false;ownership='explicit_import'}}
  Result 'installed' 'migrated' 'Рабочая тестовая настройка импортирована. Игра и реестр сохранены без изменений; восстановление доступно в лаунчере.' 0 $true $false $true @{xmlWrites=0;registryWrites=0;legacyFiles='unchanged';ownership='explicit_import'}
 }
 if(-not $script:modelInstalled){Result 'not_ready' 'missing_russian_model' 'Установите официальный русский пакет Microsoft Speech Platform 11. Отдельная фоновая программа не нужна.' 2}
 if(RouteReady $rows){
  Result 'external_configured' 'external_route' 'Русская маршрутизация уже настроена другим инструментом. Модуль не владеет этой настройкой и не перезаписывает её.' 2 $false $false $false @{externallyManagedRoute=$true;owned=$false}
 }
 # Fail before mutation if a partial external RU alias setup is detected.
 if(@(Languages $model[0] | Where-Object {$_ -in @(0x409,0x809)}).Count -gt 0){Result 'external_configured' 'partial_external_route' 'Найдена внешняя частичная настройка английских языков. Сначала восстановите её исходным инструментом.' 2}
 $entries=@(PlanRegistry $rows)
 if($Action -eq 'verify'){Result 'not_ready' 'not_installed' 'Русские голосовые команды ещё не установлены этим модулем.' 2 $false (-not $script:gameRunning)}
 if($Action -eq 'status'){Result 'ready' 'ready_to_install' 'Можно настроить русские команды на английской базе; японская версия останется без изменений.' 0 $false (-not $script:gameRunning) $false @{xml='en,ru';registryValues=$entries.Count}}
 $package=CheckedPath $PackageRoot;$dictionary=CheckedPath (Join-Path $package 'commands.ru.json')
 $originalBytes=[IO.File]::ReadAllBytes($script:xml);$originalHash=HashBytes $originalBytes
 $candidate=PrepareXml $script:xml $dictionary
 if((HashFile $script:xml) -cne $originalHash){throw 'Game XML changed during preparation.'}
 $transaction=[guid]::NewGuid().ToString('N');$txDir=CheckedPath (Join-Path (Join-Path $script:installDir 'transactions') $transaction)
 [void][IO.Directory]::CreateDirectory($txDir)
 $script:tx=[pscustomobject]@{schema=1;identity=$script:identity;gamePath=$script:game;transaction=$transaction;status='PREPARED';createdUtc=[DateTime]::UtcNow.ToString('o');originalHash=$originalHash;modifiedHash=(HashBytes $candidate);commandCount=$script:commandCount;entries=$entries;targetRelative='audio/2025/speech_recognition.xml';japanese='unchanged';russianModelRetained=$true}
 AtomicBytes $originalBytes (BackupPath $script:tx)
 SaveState $script:tx
 SaveJson ([pscustomobject]@{schema=1;identity=$script:identity;gamePath=$script:game;transaction=$transaction}) $script:ownerFile
 try{
  $script:tx.status='APPLYING';SaveState $script:tx
  foreach($e in $entries){SetToken $e.after $e.before;FailPoint 'AfterRegistryWrite'}
  ReplaceGame $candidate $originalHash;FailPoint 'AfterXmlWrite'
  VerifyApplied $script:tx
  $script:tx.status='APPLIED';SaveState $script:tx
 }catch{
  $reason=$_.Exception.Message
  try{RestoreTransaction $script:tx}catch{
   $script:tx.status='ROLLBACK_REQUIRED';SaveState $script:tx
   throw ('Installation failed: '+$reason+'; recovery requires attention: '+$_.Exception.Message)
  }
  Result 'rolled_back' 'install_failed_restored' ('Настройка прервана; исходные данные восстановлены. '+$reason) 1 $false $true $false @{xml='byte_exact';registry='original'}
 }
 Result 'installed' 'installed' 'Русские команды установлены на английской базе. В заезде удерживайте T (русская Е) и произнесите команду.' 0 $true $false $true @{xml='en,ru';commands=$script:commandCount;japanese='unchanged';microphoneTest='not_performed_by_module'}
}catch{
 Result 'error' 'operation_failed' $_.Exception.Message 1
}finally{if($script:lock){$script:lock.Dispose()}}
