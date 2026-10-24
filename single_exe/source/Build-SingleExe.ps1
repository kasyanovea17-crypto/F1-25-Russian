[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PackageDirectory,
 [Parameter(Mandatory=$true)][string]$OutputExe,
 [Parameter(Mandatory=$true)][ValidatePattern('^[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?$')][string]$Version,
 [string]$AllowList,
 [switch]$EnableTextUpdates
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) -ErrorAction Stop
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$utf8=New-Object Text.UTF8Encoding($false)
function Need([bool]$ok,[string]$message){if(-not $ok){throw $message}}
function RealPath([string]$path){
 $full=[IO.Path]::GetFullPath($path);Need (-not $full.StartsWith('\\')) 'UNC paths are not supported.'
 for($cursor=$full;-not [string]::IsNullOrWhiteSpace($cursor);$cursor=[IO.Path]::GetDirectoryName($cursor)){
  if([IO.File]::Exists($cursor)-or[IO.Directory]::Exists($cursor)){Need (([IO.File]::GetAttributes($cursor)-band[IO.FileAttributes]::ReparsePoint)-eq 0) ('Reparse path rejected: '+$cursor)}
 };return $full
}
function Hash([string]$path){$algorithm=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($path);try{return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$algorithm.Dispose()}}
function SafeRelative([string]$name){
 Need (-not [string]::IsNullOrEmpty($name)-and$name.Length-le220-and-not$name.Contains('\')-and-not$name.StartsWith('/')) 'Invalid archive path.'
 foreach($part in ($name-split '/')){
  Need ($part.Length-gt0-and$part-ne'.'-and$part-ne'..'-and-not$part.EndsWith('.')-and-not$part.EndsWith(' ')) 'Invalid archive component.'
  Need ($part.IndexOfAny([IO.Path]::GetInvalidFileNameChars())-lt0-and-not$part.Contains(':')) 'Invalid archive character.'
  Need (($part-split '\.')[0]-notmatch'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') 'Reserved Windows filename.'
 };return $name
}
$package=(RealPath $PackageDirectory).TrimEnd('\');$output=RealPath $OutputExe
Need ([IO.Directory]::Exists($package)) 'PackageDirectory does not exist.'
Need (-not [IO.File]::Exists($output)) 'Output already exists; preserve it and choose a fresh path.'
Need (-not $output.StartsWith($package+'\',[StringComparison]::OrdinalIgnoreCase)) 'Output must be outside PackageDirectory.'
$parent=[IO.Path]::GetDirectoryName($output);[IO.Directory]::CreateDirectory($parent)|Out-Null
$work=Join-Path $parent ([IO.Path]::GetFileNameWithoutExtension($output)+'.build')
Need (-not [IO.Directory]::Exists($work)-and-not[IO.File]::Exists($work)) 'Build evidence directory already exists.'
[IO.Directory]::CreateDirectory($work)|Out-Null
$names=@()
if($AllowList){$names=@([IO.File]::ReadAllLines((RealPath $AllowList),$utf8)|Where-Object{-not [string]::IsNullOrWhiteSpace($_)})}
else{
 $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($package)
 while($pending.Count){$dir=RealPath $pending.Pop();foreach($item in [IO.Directory]::EnumerateFileSystemEntries($dir)){$full=RealPath $item;if([IO.Directory]::Exists($full)){$pending.Push($full)}else{$names+=$full.Substring($package.Length+1).Replace('\','/')}}}
}
$names=@($names|Sort-Object -CaseSensitive)
Need ($names.Count-ge1-and$names.Count-le4096) 'Expected 1-4096 allowlisted files.'
$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$files=@();[long]$total=0
foreach($name in $names){
 [void](SafeRelative $name);Need ($seen.Add($name)) ('Duplicate archive path: '+$name)
 $path=RealPath (Join-Path $package $name.Replace('/','\'));Need ($path.StartsWith($package+'\',[StringComparison]::OrdinalIgnoreCase)-and[IO.File]::Exists($path)) ('Missing input file: '+$name)
 $info=Get-Item -LiteralPath $path;Need ($info.Length-le268435456) ('Input exceeds file limit: '+$name);$total+=$info.Length;Need ($total-le1073741824) 'Package exceeds 1 GiB.'
 $files+=[ordered]@{path=$name;bytes=[long]$info.Length;sha256=(Hash $path)}
}
Need ($seen.Contains('Launcher.exe')) 'Package needs an internal Launcher.exe.'
if($EnableTextUpdates){Need ($seen.Contains('manifest.json')-and$seen.Contains('payload/language.lng')) 'Text-update base files missing.'}
$archivePath=Join-Path $work 'bundle.zip';$stream=[IO.File]::Open($archivePath,'CreateNew');$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in $files){$entry=$archive.CreateEntry($file.path,[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=New-Object DateTimeOffset(2000,1,1,0,0,0,[TimeSpan]::Zero);$input=[IO.File]::OpenRead((Join-Path $package $file.path.Replace('/','\')));$destination=$entry.Open();try{$input.CopyTo($destination)}finally{$input.Dispose();$destination.Dispose()}}
}finally{$archive.Dispose();$stream.Dispose()}
Need ((Get-Item -LiteralPath $archivePath).Length-le536870912) 'Compressed archive exceeds 512 MiB.'
foreach($file in $files){Need ((Hash (Join-Path $package $file.path.Replace('/','\')))-ceq$file.sha256) ('Input changed during build: '+$file.path)}
$manifest=[ordered]@{schema=1;version=$Version;entryPoint='Launcher.exe';archiveSha256=(Hash $archivePath);totalBytes=$total;textUpdates=[bool]$EnableTextUpdates;files=$files}
$manifestPath=Join-Path $work 'bundle.json';[IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 16),$utf8)
$pinsPath=Join-Path $work 'BundlePins.cs';$pins='internal static class BundlePins { internal const string ManifestSha256="'+(Hash $manifestPath)+'", ArchiveSha256="'+$manifest.archiveSha256+'", Version="'+$Version+'"; }'
[IO.File]::WriteAllText($pinsPath,$pins,$utf8)
$applicationManifest=Join-Path $work 'app.manifest'
[IO.File]::WriteAllText($applicationManifest,'<?xml version="1.0" encoding="utf-8"?><assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1"><assemblyIdentity version="1.0.0.0" name="Karsvein.F1RU.SingleExe"/><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false"/></requestedPrivileges></security></trustInfo><compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/></application></compatibility></assembly>',$utf8)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Need ([IO.File]::Exists($csc)) '.NET Framework compiler is missing.'
$compiler=@('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/warnaserror+','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',('/win32manifest:'+$applicationManifest),('/out:'+$output),('/resource:'+$archivePath+',F1RU.Bundle.zip'),('/resource:'+$manifestPath+',F1RU.Bundle.json'),(Join-Path $PSScriptRoot 'Bootstrap.cs'),(Join-Path $PSScriptRoot 'BundleContract.cs'),$pinsPath)
& $csc @compiler
Need ($LASTEXITCODE-eq0) 'Single-executable compilation failed.'
$receipt=[ordered]@{status='PASS';version=$Version;exe=$output;exeSha256=(Hash $output);archiveSha256=$manifest.archiveSha256;manifestSha256=(Hash $manifestPath);files=$files.Count;uncompressedBytes=$total;exeBytes=(Get-Item -LiteralPath $output).Length;textUpdates=[bool]$EnableTextUpdates;requestedExecutionLevel='asInvoker';persistentCache='%LOCALAPPDATA%\Karsvein\F1RU\packages\<version>-<archive-sha256>';inputs=@(@{path=(Join-Path $PSScriptRoot 'Bootstrap.cs');sha256=(Hash (Join-Path $PSScriptRoot 'Bootstrap.cs'))},@{path=(Join-Path $PSScriptRoot 'BundleContract.cs');sha256=(Hash (Join-Path $PSScriptRoot 'BundleContract.cs'))})}
[IO.File]::WriteAllText((Join-Path $work 'BUILD_RECEIPT.json'),($receipt|ConvertTo-Json -Depth 8),$utf8)
Write-Output ('SINGLE_EXE_BUILD_PASS files='+$files.Count+' bytes='+$receipt.exeBytes+' sha256='+$receipt.exeSha256+' text_updates='+[bool]$EnableTextUpdates)
