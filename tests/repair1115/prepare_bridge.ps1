param([Parameter(Mandatory=$true)][string]$Work)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$target=[IO.Path]::GetFullPath($Work)
if(-not $target.StartsWith(([IO.Path]::GetDirectoryName($root)+'\'),[StringComparison]::OrdinalIgnoreCase)-or(Test-Path -LiteralPath $target)){throw 'Fresh bounded work directory required.'}
[void][IO.Directory]::CreateDirectory($target)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=@(Get-Content -LiteralPath (Join-Path $root '.release\package-v0.33.txt') | Where-Object {$_ -match '^[A-Za-z][A-Za-z0-9_]*\.cs$'} | ForEach-Object {Join-Path $root $_})
$exe=Join-Path $target 'PrepareBridgeTests.exe'
$args=@('/nologo','/utf8output','/target:exe','/platform:x64','/optimize+','/warn:4','/warnaserror+','/main:PrepareBridgeTests','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/out:'+$exe))+$source+@((Join-Path $PSScriptRoot 'prepare_bridge.cs'))
& $csc @args
if($LASTEXITCODE-ne0){throw 'Compile failed.'}
& $exe $root (Join-Path $target 'fixture')
if($LASTEXITCODE-ne0){throw 'Tests failed.'}
