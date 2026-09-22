$ErrorActionPreference='Stop'
$r=$PSScriptRoot
$out=Join-Path $r 'dist\F1_25_RU_v0.27'
$zip=Join-Path $r 'dist\F1_25_RU_v0.27_text0.15.5.zip'
if((Test-Path -LiteralPath $out) -or (Test-Path -LiteralPath $zip)){throw 'Output exists. Preserve or rename it before rebuilding.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
foreach($n in @('Launcher.cs','Updater.cs','Addons.cs','Engine.ps1','Compatibility.cs','background.png','project-links.json','EDITORIAL_NOTES.json','README.txt','CHANGELOG.md','Вернуть оригинал.cmd','ROLLBACK.sh','TEST_REPORT.md')){
 Copy-Item -LiteralPath (Join-Path $r $n) -Destination (Join-Path $out $n)
}
Copy-Item -LiteralPath (Join-Path $r 'assets') -Destination $out -Recurse
Copy-Item -LiteralPath (Join-Path $r 'addons') -Destination $out -Recurse
[IO.Directory]::CreateDirectory((Join-Path $out 'evidence'))|Out-Null
[IO.Directory]::CreateDirectory((Join-Path $out 'payload'))|Out-Null
$channel=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'updates\stable.json')|ConvertFrom-Json
$m=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'manifest.base.json')|ConvertFrom-Json
if($channel.file -notmatch '^\d+\.\d+(?:\.\d+){0,2}/language\.lng$'){throw 'Invalid channel path'}
$source=Join-Path (Join-Path $r 'updates') $channel.file
if((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $channel.sha256){throw 'Channel hash mismatch'}
if((Get-Item -LiteralPath $source).Length -ne $channel.bytes){throw 'Channel size mismatch'}
if($m.lng -ne $channel.sha256){$m.previous_lng=@($m.previous_lng)+@($m.lng)}
$m.lng=$channel.sha256;$m.payload.'language.lng'=$channel.sha256;$m.text_version=$channel.version
$m|ConvertTo-Json -Depth 30|Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Copy-Item -LiteralPath $source -Destination (Join-Path $out 'payload\language.lng')
foreach($n in @('fonts_russian.erp','fonts_russian_r_p.erp')){Copy-Item -LiteralPath (Join-Path $r ('payload\'+$n)) -Destination (Join-Path $out ('payload\'+$n))}
foreach($p in $m.payload.PSObject.Properties){
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out ('payload\'+$p.Name))).Hash.ToLowerInvariant() -ne $p.Value){throw ('Payload hash mismatch: '+$p.Name)}
}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $out
try{& $csc /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:Launcher.exe Launcher.cs Updater.cs Addons.cs;if($LASTEXITCODE -ne 0){throw 'Build failed'}}finally{Pop-Location}
$result=Join-Path $out 'VERIFICATION.txt'
$p=Start-Process -FilePath (Join-Path $out 'Launcher.exe') -ArgumentList ('--self-test "'+$result+'"') -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode -ne 0){throw 'UI test failed'}
Get-Content -LiteralPath $result
# The UI test creates only these known, disposable detection markers.
# Remove exact files and then empty directories, never recursively delete a computed path.
$fixture=Join-Path $out 'evidence\client-detection'
$marker=Join-Path $fixture 'steamapps\appmanifest_3059520.acf'
if(Test-Path -LiteralPath $marker){[IO.File]::Delete($marker)}
foreach($n in @('steamapps\common\F1 25\_Installer','steamapps\common\F1 25','steamapps\common','steamapps','ea\_Installer','ea','')){
 $d=$fixture;if($n){$d=Join-Path $fixture $n};if([IO.Directory]::Exists($d)){[IO.Directory]::Delete($d,$false)}
}
$evidence=Join-Path $out 'evidence';if([IO.Directory]::Exists($evidence)){[IO.Directory]::Delete($evidence,$false)}
$lines=Get-ChildItem -LiteralPath $out -Recurse -File|Sort-Object FullName|ForEach-Object{
 (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($out.Length+1).Replace('\','/')
}
$lines|Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding UTF8
Compress-Archive -LiteralPath $out -DestinationPath $zip
Write-Output ('BUILD_PASS launcher=0.27 text='+$m.text_version+' clients=Steam,EA')
Get-FileHash -Algorithm SHA256 -LiteralPath $zip
