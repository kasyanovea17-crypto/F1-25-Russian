[CmdletBinding()]
param(
 [ValidateSet('status','verify','install','restore')][string]$Action='status',
 [Parameter(Mandatory=$true)][string]$GamePath,
 [Parameter(Mandatory=$true)][string]$ResultPath
)
$ErrorActionPreference='Stop';$utf8=New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding=$utf8
function ResultDestination([string]$Path){
 if(-not [IO.Path]::IsPathRooted($Path)){throw 'An absolute result path is required.'}
 $p=[IO.Path]::GetFullPath($Path);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(-not $p.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)){throw 'Result must be in the current user temporary folder.'}
 if($p.Substring($temp.Length) -notmatch '^F1RU-MaxVoice-[a-fA-F0-9]{32}\\result\.json$'){throw 'Invalid result filename.'}
 if(-not [IO.Directory]::Exists([IO.Path]::GetDirectoryName($p))){throw 'Result directory is missing.'}
 $cursor=$p
 while($cursor){if([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)){if(([IO.File]::GetAttributes($cursor)-band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Linked result destination rejected.'}};$cursor=[IO.Path]::GetDirectoryName($cursor)}
 if([IO.File]::Exists($p)){throw 'Result file already exists.'}
 return $p
}
$destination=ResultDestination $ResultPath
$code=1
try{
 $output=@(& (Join-Path $PSScriptRoot 'MaxVoice.ps1') -Action $Action -GamePath $GamePath -PackageRoot $PSScriptRoot)
 $code=$LASTEXITCODE;$json=$output -join "`n"
 if($json.Length -gt 262144){throw 'Audio response exceeds its bound.'}
 $parsed=$json|ConvertFrom-Json
 if($parsed.schema -ne 1 -or [string]::IsNullOrWhiteSpace($parsed.status)){throw 'Invalid audio response.'}
}catch{
 $json=[ordered]@{schema=1;status='error';code='host_failed';message=$_.Exception.Message;installed=$false;canInstall=$false;canRestore=$false;gameRunning=$false;testOnly=$true;maxSlots=192;pronunciationReview=18;gameplayTested=$false;backup=$null;targetHash=$null;expectedHash='b3202523d49c3cd173a7dd4172b78f071fd5e0b9967bdb59cbcaae9719dd50a5'}|ConvertTo-Json -Compress
 $code=1
}
[void](ResultDestination $destination)
$stream=[IO.File]::Open($destination,'CreateNew','Write','None')
try{$bytes=$utf8.GetBytes($json);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
exit $code
