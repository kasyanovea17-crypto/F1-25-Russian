$ErrorActionPreference='Stop'
$r=$PSScriptRoot
$out=Join-Path $r 'dist\F1_25_RU_v0.23'
if(Test-Path -LiteralPath $out){throw 'Output already exists. Preserve or rename dist before rebuilding.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
foreach($n in @('Launcher.cs','Updater.cs','Engine.ps1','background.png','EDITORIAL_NOTES.json')){Copy-Item -LiteralPath (Join-Path $r $n) -Destination (Join-Path $out $n)}
Copy-Item -LiteralPath (Join-Path $r 'README.md') -Destination (Join-Path $out 'README.txt')
Copy-Item -LiteralPath (Join-Path $r 'CHANGELOG.md') -Destination (Join-Path $out 'CHANGELOG.md')
Copy-Item -LiteralPath (Join-Path $r 'assets') -Destination $out -Recurse
Copy-Item -LiteralPath (Join-Path $r 'payload') -Destination $out -Recurse
$channel=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'updates\stable.json')|ConvertFrom-Json
$m=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $r 'manifest.base.json')|ConvertFrom-Json
$source=Join-Path (Join-Path $r 'updates') $channel.file
if((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $channel.sha256){throw 'Channel hash mismatch'}
$m.previous_lng=@($m.previous_lng)+@($m.lng)
$m.lng=$channel.sha256;$m.payload.'language.lng'=$channel.sha256;$m.text_version=$channel.version
$m|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Copy-Item -LiteralPath $source -Destination (Join-Path $out 'payload\language.lng')
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $out
try{& $csc /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:Launcher.exe Launcher.cs Updater.cs;if($LASTEXITCODE -ne 0){throw 'Build failed'}}finally{Pop-Location}
$p=Start-Process -FilePath (Join-Path $out 'Launcher.exe') -ArgumentList ('--self-test "'+(Join-Path $out 'UI_TEST.txt')+'"') -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode -ne 0){throw 'UI test failed'}
Compress-Archive -LiteralPath $out -DestinationPath (Join-Path $r 'dist\F1_25_RU_v0.23.zip')
Write-Output 'BUILD_PASS launcher=0.23'
