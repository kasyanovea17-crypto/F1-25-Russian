[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PackageDirectory,
 [Parameter(Mandatory=$true)][string]$WorkDirectory,
 [string]$Version='0.31-ci',
 [string]$UpdaterSourceDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
Import-Module ([IO.Path]::Combine($PSHOME,'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) -ErrorAction Stop
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$utf8=New-Object Text.UTF8Encoding($false)
$package=[IO.Path]::GetFullPath($PackageDirectory);$work=[IO.Path]::GetFullPath($WorkDirectory)
if([IO.Directory]::Exists($work)-or[IO.File]::Exists($work)){throw 'Use a fresh WorkDirectory; previous evidence is preserved.'}
if($work.StartsWith($package.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'WorkDirectory must be outside PackageDirectory.'}
$tests=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests'))
foreach($name in @('FixtureLauncher.cs','BootstrapTests.cs')){if(-not[IO.File]::Exists((Join-Path $tests $name))){throw ('Missing relative test source: '+$name)}}
foreach($name in @('manifest.json','payload\language.lng')){if(-not[IO.File]::Exists((Join-Path $package $name))){throw ('Missing runtime input: '+$name)}}
$fixture=Join-Path $work 'fixture';$output=Join-Path $work 'output';$results=Join-Path $work 'results'
foreach($dir in @($fixture,(Join-Path $fixture 'payload'),$output,$results)){[IO.Directory]::CreateDirectory($dir)|Out-Null}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:x64 /warn:4 /warnaserror+ /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $fixture 'Launcher.exe')) (Join-Path $tests 'FixtureLauncher.cs')
if($LASTEXITCODE-ne0){throw 'Fixture compilation failed.'}
foreach($name in @('manifest.json','payload\language.lng')){[IO.File]::Copy((Join-Path $package $name),(Join-Path $fixture $name),$false)}
$exe=Join-Path $output 'SingleExeFixture.exe'
& (Join-Path $PSScriptRoot 'Build-SingleExe.ps1') -PackageDirectory $fixture -OutputExe $exe -Version $Version -EnableTextUpdates
if($LASTEXITCODE-ne0){throw 'Fixture wrapper compilation failed.'}
$build=Join-Path $output 'SingleExeFixture.build';$unit=Join-Path $output 'BootstrapTests.exe'
$common=@('/nologo','/target:exe','/platform:x64','/warn:4','/warnaserror+','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',('/resource:'+(Join-Path $build 'bundle.zip')+',F1RU.Bundle.zip'),('/resource:'+(Join-Path $build 'bundle.json')+',F1RU.Bundle.json'))
$sources=@((Join-Path $PSScriptRoot 'Bootstrap.cs'),(Join-Path $PSScriptRoot 'BundleContract.cs'),(Join-Path $build 'BundlePins.cs'))
& $csc @common /main:BootstrapTests ('/out:'+$unit) @sources (Join-Path $tests 'BootstrapTests.cs')
if($LASTEXITCODE-ne0){throw 'Unit test compilation failed.'}
foreach($phase in @('baseline','suite','rollback')){
 if($phase-eq'suite'){& $unit $results}else{& $unit $results $phase}
 if($LASTEXITCODE-ne0){throw ('Unit phase failed: '+$phase)}
}
$bundleReport=Join-Path $results 'BUNDLE_SELF_TEST.json'
$p=Start-Process -FilePath $exe -ArgumentList ('--bundle-self-test "'+$bundleReport+'"') -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode-ne0){throw 'Wrapper extract/verify self-test failed.'}
$forwardReport=Join-Path $results 'FORWARDED_ARGS.json'
$p=Start-Process -FilePath $exe -ArgumentList ('--self-test "'+$forwardReport+'" "radio check" "русская фраза" "a\"b" "tail\\"') -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode-ne0){throw 'Process forwarding test failed.'}
$forward=Get-Content -LiteralPath $forwardReport -Raw -Encoding UTF8|ConvertFrom-Json
$want=@('--self-test',$forwardReport,'radio check','русская фраза','a"b','tail\')
if(($forward.args|ConvertTo-Json -Compress)-cne($want|ConvertTo-Json -Compress)){throw 'Forwarded arguments differ.'}
$p=Start-Process -FilePath $exe -ArgumentList '--exit 37' -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode-ne37){throw 'Child exit code was not propagated.'}
$integration=$false
if(-not[string]::IsNullOrWhiteSpace($UpdaterSourceDirectory)){
 $updater=[IO.Path]::GetFullPath($UpdaterSourceDirectory);$integrationExe=Join-Path $output 'UpdaterIntegrationTests.exe'
 foreach($name in @('Updater.cs','GameTextProfiles.cs','TranslationVariants.cs')){if(-not[IO.File]::Exists((Join-Path $updater $name))){throw ('Missing actual updater source: '+$name)}}
 & $csc @common /main:UpdaterIntegrationTests ('/out:'+$integrationExe) @sources (Join-Path $tests 'UpdaterIntegrationTests.cs') (Join-Path $updater 'Updater.cs') (Join-Path $updater 'GameTextProfiles.cs') (Join-Path $updater 'TranslationVariants.cs')
 if($LASTEXITCODE-ne0){throw 'Actual updater integration compilation failed.'}
 & $integrationExe $exe $results
 if($LASTEXITCODE-ne0){throw 'Actual updater integration test failed.'};$integration=$true
}
$unitReport=Get-Content -LiteralPath (Join-Path $results 'UNIT_RESULTS.json') -Raw -Encoding UTF8|ConvertFrom-Json
$summary=[ordered]@{status='PASS';checks=$unitReport.checks;actualUpdaterIntegration=$integration;argumentForwarding=$true;exitCodePropagation=$true;rollbackCopyRestored=$true;gameModified=$false;registryModified=$false;workDirectory=$work}
[IO.File]::WriteAllText((Join-Path $results 'CI_RESULTS.json'),($summary|ConvertTo-Json -Depth 8),$utf8)
Write-Output ('SINGLE_EXE_CI_PASS checks='+$unitReport.checks+' actual_updater_integration='+$integration+' args_forwarded=true exit_propagated=true game_modified=false')
