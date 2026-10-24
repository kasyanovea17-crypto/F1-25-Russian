[CmdletBinding()]
param([string]$RepositoryRoot,[string]$ReportPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
foreach($module in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management')){
 Import-Module (Join-Path $PSHOME ('Modules\'+$module+'\'+$module+'.psd1')) -ErrorAction Stop
}
if($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess){throw 'Run this fixture-only test with x64 Windows PowerShell 5.1.'}
if([string]::IsNullOrWhiteSpace($RepositoryRoot)){$RepositoryRoot=Split-Path $PSScriptRoot -Parent}
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$utf8=New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding=$utf8
$source=Join-Path $repo 'speech\SpeechEngine.ps1'
$cases=Join-Path $repo 'scripts\speech_release_cases.ps1'
$contextPath=Join-Path $repo 'scripts\fixtures\speech_contexts.json'
$previous=Join-Path $repo 'scripts\fixtures\speech_previous_commands.json'
$dictionary=Join-Path $repo 'speech\commands.ru.json'
foreach($file in @($source,$cases,$contextPath,$previous,$dictionary)){if(-not [IO.File]::Exists($file)){throw ('Missing test input: '+$file)}}
if([string]::IsNullOrWhiteSpace($ReportPath)){$ReportPath=Join-Path $repo 'evidence\SPEECH_RELEASE_TESTS.json'}
$report=[IO.Path]::GetFullPath($ReportPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($report))
$root=Join-Path ([IO.Path]::GetDirectoryName($report)) ('speech-ci-'+[guid]::NewGuid().ToString('N'))
$fixturePackage=Join-Path $root 'package';$oldPackage=Join-Path $root 'previous';$runtime=Join-Path $root 'runtime'
foreach($dir in @($fixturePackage,$oldPackage,$runtime)){[void][IO.Directory]::CreateDirectory($dir)}
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function SaveJson($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 32),$utf8)}
function XmlBytes([Xml.XmlDocument]$Doc){
 $stream=New-Object IO.MemoryStream;$settings=New-Object Xml.XmlWriterSettings
 $settings.Encoding=$utf8;$settings.Indent=$false;$settings.NewLineHandling=[Xml.NewLineHandling]::None
 $writer=[Xml.XmlWriter]::Create($stream,$settings)
 try{$Doc.Save($writer);$writer.Flush();return ,$stream.ToArray()}finally{$writer.Dispose();$stream.Dispose()}
}
$sourceHash=Hash $source
try{
 $contexts=Get-Content -LiteralPath $contextPath -Raw -Encoding UTF8|ConvertFrom-Json
 $current=Get-Content -LiteralPath $dictionary -Raw -Encoding UTF8|ConvertFrom-Json
 $old=Get-Content -LiteralPath $previous -Raw -Encoding UTF8|ConvertFrom-Json
 if($contexts.schema -ne 1 -or -not $contexts.synthetic -or @($contexts.contexts).Count -ne 5 -or @($current.PSObject.Properties).Count -ne 37 -or @($old.PSObject.Properties).Count -ne 37){throw 'Unexpected synthetic fixture schema.'}
 # The fixture contains no original game XML, binaries, user paths or recorded audio.
 $doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true;$doc.XmlResolver=$null
 [void]$doc.AppendChild($doc.CreateXmlDeclaration('1.0','utf-8',$null))
 $top=$doc.CreateElement('SpeechRecognition');$top.SetAttribute('name','SYNTHETIC CI FIXTURE');$top.SetAttribute('version','1.0');[void]$doc.AppendChild($top)
 $row=0
 foreach($context in $contexts.contexts){
  $group=$doc.CreateElement('KeywordList');$group.SetAttribute('name',[string]$context.name);$group.SetAttribute('type','standard');[void]$top.AppendChild($group)
  foreach($name in $context.commands){
   if($current.PSObject.Properties.Name -notcontains $name -or $old.PSObject.Properties.Name -notcontains $name){throw 'Synthetic fixture command is missing from dictionary.'}
   $keyword=$doc.CreateElement('Keyword');$keyword.SetAttribute('name',[string]$name);$keyword.SetAttribute('delay','0');$keyword.SetAttribute('confidence','40');$keyword.SetAttribute('en',('fixture phrase '+$row));$keyword.SetAttribute('ru',('fixture-ru-'+$row));$keyword.SetAttribute('ja',('fixture-ja-'+$row));[void]$group.AppendChild($keyword)
   $action=$doc.CreateElement('Action');$action.SetAttribute('type','FixtureCommand');$action.SetAttribute('param',('fixture-action-'+$row));[void]$keyword.AppendChild($action)
   if(($row%3)-eq 0){$filter=$doc.CreateElement('Filter');$filter.SetAttribute('inclusion_id',[string]($row%4));[void]$keyword.AppendChild($filter)}
   $row++
  }
  if($context.defaultHandler){$default=$doc.CreateElement('Keyword');$default.SetAttribute('name','default');$a=$doc.CreateElement('Action');$a.SetAttribute('type','FixtureDefault');$a.SetAttribute('param','unchanged');[void]$default.AppendChild($a);[void]$group.AppendChild($default)}
 }
 if($row -ne 104){throw 'Expected 104 synthetic command entries.'}
 $original=Join-Path $fixturePackage 'ORIGINAL.xml';[IO.File]::WriteAllBytes($original,(XmlBytes $doc))
 Copy-Item -LiteralPath $dictionary -Destination (Join-Path $fixturePackage 'commands.ru.json')
 Copy-Item -LiteralPath $previous -Destination (Join-Path $oldPackage 'commands.ru.json')
 $oldDoc=$doc.CloneNode($true)
 foreach($keyword in $oldDoc.SelectNodes('//Keyword')){if($keyword.GetAttribute('name') -cne 'default'){$phrase=[string]$old.($keyword.GetAttribute('name'));$keyword.SetAttribute('en',$phrase);$keyword.SetAttribute('ru',$phrase)}}
 $oldXml=Join-Path $oldPackage 'prepared.xml';[IO.File]::WriteAllBytes($oldXml,(XmlBytes $oldDoc))
 # Only the reviewed-profile identifiers are substituted in an isolated copy,
 # so legacy migration's exact-hash branch is tested without publishing game data.
 $engineText=[IO.File]::ReadAllText($source,$utf8)
 $originalPattern="(?m)^\`$script:reviewedLegacyOriginal='[0-9a-f]{64}'\r?$"
 $dictionaryPattern="(?m)^\`$script:reviewedLegacyDictionary='[0-9a-f]{64}'\r?$"
 if([regex]::Matches($engineText,$originalPattern).Count -ne 1 -or [regex]::Matches($engineText,$dictionaryPattern).Count -ne 1){throw 'Expected exactly two reviewed-profile constants.'}
 $engineText=[regex]::Replace($engineText,$originalPattern,('$script:reviewedLegacyOriginal='''+(Hash $original)+''''))
 $engineText=[regex]::Replace($engineText,$dictionaryPattern,('$script:reviewedLegacyDictionary='''+(Hash $oldXml)+''''))
 $engine=Join-Path $runtime 'SpeechEngine.ps1';[IO.File]::WriteAllText($engine,$engineText,(New-Object Text.UTF8Encoding($true)))
 $caseReport=Join-Path $root 'cases.json'
 $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 & $powershell -NoProfile -ExecutionPolicy Bypass -File $cases -PackageRoot $fixturePackage -EnginePath $engine -PreviousPackageRoot $oldPackage -RunRoot (Join-Path $root 'fixtures') -ReportPath $caseReport
 $exit=$LASTEXITCODE
 if($exit -ne 0){throw ('Synthetic backend tests failed; exit '+$exit)}
 $result=Get-Content -LiteralPath $caseReport -Raw -Encoding UTF8|ConvertFrom-Json
 if($result.status -cne 'PASS' -or $result.count -ne 42 -or $result.liveGameWrites -ne 0 -or $result.realRegistryWrites -ne 0){throw 'Unexpected backend test result.'}
 if((Hash $source) -cne $sourceHash){throw 'Production engine changed during fixture tests.'}
 SaveJson ([ordered]@{status='PASS';count=42;syntheticFixture=$true;originalGameDataRequired=$false;speechModelInstallationRequired=$false;microphoneUsed=$false;liveGameWrites=0;realRegistryWrites=0;sourceEngineHash=$sourceHash;sourceUnchanged=$true;fixtureEngineHash=(Hash $engine);profileHashSubstitution='two named constants in isolated copy only';fixtureOriginalHash=(Hash $original);fixturePreviousHash=(Hash $oldXml);caseReport=$caseReport;fixtureRoot=$root;tests=$result.tests}) $report
 Write-Output 'SPEECH_RELEASE_TESTS_PASS tests=42 synthetic_fixture=true original_game_required=false speech_model_required=false live_writes=0'
 exit 0
}catch{
 SaveJson ([ordered]@{status='FAIL';error=$_.Exception.Message;syntheticFixture=$true;fixtureRoot=$root;sourceEngineHash=$sourceHash;liveGameWrites=0;realRegistryWrites=0}) $report
 Write-Output ('SPEECH_RELEASE_TESTS_FAIL '+$_.Exception.Message)
 exit 1
}
