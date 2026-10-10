param([switch]$SkipArchive,[switch]$PackageOnly)
$ErrorActionPreference='Stop'
# Explicit paths avoid inheriting PowerShell 7 module discovery when a Python
# build runner launches Windows PowerShell 5.1.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
$r=$PSScriptRoot
$out=Join-Path $r 'dist\F1_25_RU_v0.30'
$zip=Join-Path $r 'dist\F1_25_RU_v0.30_text0.15.6.zip'
$code=@('Launcher.cs','TranslationVariants.cs','GameTextProfiles.cs','Updater.cs','Engine.ps1','Compatibility.cs','ROLLBACK.sh','Вернуть оригинал.cmd')
$docs=@('README.txt','CHANGELOG.md','TEST_REPORT.md')
$static=@('background.png','project-links.json','EDITORIAL_NOTES.json')
if(!$PackageOnly){
 if((Test-Path -LiteralPath $out) -or (Test-Path -LiteralPath $zip)){throw 'Output exists. Preserve it; use -PackageOnly only after testing an existing build.'}
 [IO.Directory]::CreateDirectory($out)|Out-Null
 foreach($n in $code+$docs+$static){Copy-Item -LiteralPath (Join-Path $r $n) -Destination (Join-Path $out $n)}
 Copy-Item -LiteralPath (Join-Path $r 'assets') -Destination $out -Recurse
 [IO.Directory]::CreateDirectory((Join-Path $out 'payload'))|Out-Null
 [IO.Directory]::CreateDirectory((Join-Path $out 'evidence'))|Out-Null
 $channel=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'updates\stable.json')|ConvertFrom-Json
 $m=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'manifest.base.json')|ConvertFrom-Json
 if($channel.file -notmatch '^\d+\.\d+(?:\.\d+){0,2}/language\.lng$'){throw 'Invalid channel path'}
 $source=Join-Path (Join-Path $r 'updates') $channel.file
 if((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $channel.sha256){throw 'Channel hash mismatch'}
 if((Get-Item -LiteralPath $source).Length -ne $channel.bytes){throw 'Channel size mismatch'}
 if($m.lng -ne $channel.sha256){$m.previous_lng=@($m.previous_lng)+@($m.lng)}
 $m.lng=$channel.sha256;$m.payload.'language.lng'=$channel.sha256;$m.text_version=$channel.version
 foreach($profile in $m.native_profiles){if(!$profile.language_set){foreach($file in $profile.install_files){if($file.payload -eq 'language.lng'){$file.sha256=$m.lng}}}}
 $m|ConvertTo-Json -Depth 40|Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
 foreach($p in $m.payload.PSObject.Properties){
  if($p.Name -notmatch '^[a-zA-Z0-9._-]+$'){throw 'Payload must have a simple filename'}
  $inputFile=Join-Path $r ('payload\'+$p.Name);if($p.Name -eq 'language.lng'){$inputFile=$source}
  if((Get-FileHash -Algorithm SHA256 -LiteralPath $inputFile).Hash.ToLowerInvariant() -ne $p.Value){throw ('Payload hash mismatch: '+$p.Name)}
  Copy-Item -LiteralPath $inputFile -Destination (Join-Path $out ('payload\'+$p.Name))
 }
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 Push-Location $out
 try{& $csc /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:Launcher.exe Launcher.cs TranslationVariants.cs GameTextProfiles.cs Updater.cs;if($LASTEXITCODE -ne 0){throw 'Build failed'}}finally{Pop-Location}
 $result=Join-Path $out 'UI_VERIFICATION.txt'
 $p=Start-Process -FilePath (Join-Path $out 'Launcher.exe') -ArgumentList ('--self-test "'+$result+'"') -WindowStyle Hidden -Wait -PassThru
 if($p.ExitCode -ne 0){Get-Content -LiteralPath $result;throw 'UI test failed'}
 Get-Content -LiteralPath $result
 Write-Output 'BUILD_PASS launcher=0.30 game_profiles=1.18,1.24,1.26'
}
if($SkipArchive){return}
if(Test-Path -LiteralPath $zip){throw 'Archive already exists; delivered files are never overwritten.'}
foreach($n in $code+$static){
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $r $n)).Hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out $n)).Hash){throw ('Source changed after build: '+$n)}
}
foreach($n in $docs){Copy-Item -LiteralPath (Join-Path $r $n) -Destination (Join-Path $out $n) -Force}
$m=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $out 'manifest.json')|ConvertFrom-Json
$files=@($code+$docs+$static+@('Launcher.exe','manifest.json','UI_VERIFICATION.txt'))
foreach($p in $m.payload.PSObject.Properties){
 if($p.Name -notmatch '^[a-zA-Z0-9._-]+$'){throw 'Invalid payload filename'}
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out ('payload\'+$p.Name))).Hash.ToLowerInvariant() -ne $p.Value){throw ('Payload changed: '+$p.Name)}
 $files+=('payload\'+$p.Name)
}
$files+=@(Get-ChildItem -LiteralPath (Join-Path $out 'assets') -File -Recurse|ForEach-Object {$_.FullName.Substring($out.Length+1)})
$files=@($files|Sort-Object -Unique)
$sums=@($files|ForEach-Object {(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out $_)).Hash.ToLowerInvariant()+'  '+$_.Replace('\','/')})
$sums|Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding UTF8
$files+='SHA256SUMS.txt'
# Explicit allowlist excludes all test fixtures, game binaries and private logs.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=[IO.File]::Open($zip,'CreateNew');$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($name in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $out $name),('F1_25_RU_v0.30/'+$name.Replace('\','/')),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose();$stream.Dispose()}
Write-Output ('PACKAGE_PASS files='+$files.Count+' text='+$m.text_version)
Get-FileHash -Algorithm SHA256 -LiteralPath $zip
