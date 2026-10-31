[CmdletBinding()]
param([switch]$PackageOnly)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$r=[IO.Path]::GetFullPath($PSScriptRoot)
$version='0.32'
$out=Join-Path $r ('dist\F1_25_RU_v'+$version)
$exe=Join-Path $r ('dist\F1_25_RU_v'+$version+'.exe')
$allow=Join-Path $r '.release\package-v0.32.txt'
function Need([bool]$ok,[string]$message){if(-not $ok){throw $message}}
function Hash([string]$path){return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Relative([string]$name){
 Need (-not [string]::IsNullOrWhiteSpace($name)-and$name.Length-le220-and-not$name.Contains('\')-and-not$name.StartsWith('/')) 'Invalid package path.'
 foreach($part in $name.Split('/')){Need ($part.Length-gt0-and$part-ne'.'-and$part-ne'..'-and-not$part.EndsWith('.')-and-not$part.EndsWith(' ')-and$part.IndexOfAny([IO.Path]::GetInvalidFileNameChars())-lt0-and-not$part.Contains(':')) 'Invalid package component.'}
 return $name
}
Need ([IO.File]::Exists($allow)) 'Missing explicit v0.32 package allowlist.'
Need (-not [IO.Directory]::Exists($out)-and-not[IO.File]::Exists($exe)) 'Preserve existing outputs; build v0.32 in a fresh checkout.'
$names=@([IO.File]::ReadAllLines($allow)|Where-Object{-not[string]::IsNullOrWhiteSpace($_)})
Need ($names.Count-eq50) 'The reviewed v0.32 package contains exactly 50 files.'
$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
[void][IO.Directory]::CreateDirectory($out)
foreach($name in $names){
 [void](Relative $name);Need ($seen.Add($name)) ('Duplicate package input: '+$name)
 if($name-in@('Launcher.exe','SHA256SUMS.txt','UI_VERIFICATION.txt')){continue}
 $sourceName=if($name-eq'manifest.json'){'manifest.base.json'}else{$name}
 $source=[IO.Path]::GetFullPath((Join-Path $r $sourceName.Replace('/','\')))
 Need ($source.StartsWith($r+'\',[StringComparison]::OrdinalIgnoreCase)-and[IO.File]::Exists($source)) ('Missing source: '+$sourceName)
 Need (([IO.File]::GetAttributes($source)-band[IO.FileAttributes]::ReparsePoint)-eq0) ('Reparse source: '+$sourceName)
 $to=Join-Path $out $name.Replace('/','\');[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to));Copy-Item -LiteralPath $source -Destination $to
}
$m=Get-Content -LiteralPath (Join-Path $out 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
Need ($m.version-eq$version) 'Manifest launcher version differs from v0.32.'
Need ($m.text_version-eq'0.15.6') 'Stable text version changed unexpectedly.'
foreach($field in $m.payload.PSObject.Properties){Need ($field.Name-match'^[a-zA-Z0-9._-]+$') 'Invalid payload name.';Need ((Hash (Join-Path $out ('payload\'+$field.Name)))-ceq$field.Value) ('Payload hash differs: '+$field.Name)}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@('Launcher.cs','GameLocation.cs','LaunchContext.cs','SpeechUI.cs','MaxVoiceUI.cs','TranslationVariants.cs','GameTextProfiles.cs','Updater.cs','Compatibility.cs')
$arguments=@('/nologo','/utf8output','/target:winexe','/platform:x64','/optimize+','/warn:4','/warnaserror+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/out:'+(Join-Path $out 'Launcher.exe')))+@($sources|ForEach-Object{Join-Path $out $_})
& $csc @arguments
Need ($LASTEXITCODE-eq0) 'Native launcher compilation failed.'
$result=Join-Path $out 'UI_VERIFICATION.txt'
$process=Start-Process -FilePath (Join-Path $out 'Launcher.exe') -ArgumentList @('--self-test',('"'+$result+'"')) -WindowStyle Hidden -Wait -PassThru
Need ($process.ExitCode-eq0) 'Native launcher self-test failed.'
Get-Content -LiteralPath $result
$sums=@($names|Where-Object{$_-ne'SHA256SUMS.txt'}|ForEach-Object{(Hash (Join-Path $out $_.Replace('/','\')))+'  '+$_})
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'),$sums,(New-Object Text.UTF8Encoding($false)))
Write-Output 'PACKAGE_STAGE_PASS launcher=0.32 files=50 profiles=1.18,1.20,1.24,1.26 game_writes=0'
if($PackageOnly){return}
& (Join-Path $r 'single_exe\source\Build-SingleExe.ps1') -PackageDirectory $out -OutputExe $exe -Version $version -AllowList $allow -EnableTextUpdates
Need ($LASTEXITCODE-eq0) 'Single EXE build failed.'
Write-Output ('SINGLE_EXE_READY '+$exe)
