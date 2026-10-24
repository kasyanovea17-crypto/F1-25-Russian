[CmdletBinding()]
param([ValidateSet('status','install')][string]$Action='status')
$ErrorActionPreference='Stop'
foreach($module in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')){
 Import-Module (Join-Path $PSHOME ('Modules\'+$module+'\'+$module+'.psd1')) -ErrorAction Stop
}
Set-StrictMode -Version Latest
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$url='https://download.microsoft.com/download/4/0/d/40d6347a-afa5-417d-a9bb-173d937beed4/MSSpeech_SR_ru-RU_TELE.msi'
$expectedHash='abbd31009914ef3adaa702531403744979524910d579ef27e80666dbd46e3036'
$expectedBytes=18006016
$tokens='SOFTWARE\Microsoft\Speech Server\v11.0\Recognizers\Tokens'
$model='SR_MS_ru-RU_TELE_11.0'
$script:present=$false
$script:running=$false
$script:lock=$null
function Finish([string]$Status,[string]$Code,[string]$Message,[int]$ExitCode,[bool]$Restart=$false){
 [ordered]@{status=$Status;code=$Code;message=$Message;modelInstalled=$script:present;installed=$false;needsRestart=$Restart;gameRunning=$script:running;canInstall=$false;canRestore=$false;commandCount=37;details=@{source=$url;sha256=$expectedHash}}|ConvertTo-Json -Compress -Depth 5|Write-Output
 exit $ExitCode
}
function ModelPresent{
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64');$key=$null
 try{$key=$base.OpenSubKey($tokens+'\'+$model);return $null -ne $key}finally{if($key){$key.Dispose()};$base.Dispose()}
}
function LocalPath([string]$Path){
 $full=[IO.Path]::GetFullPath($Path)
 if(-not [IO.Path]::IsPathRooted($Path) -or $full.StartsWith('\\') -or $full.Substring(2).Contains(':')){throw 'Local path required.'}
 $p=$full
 while($p){if(Test-Path -LiteralPath $p){if(([IO.File]::GetAttributes($p)-band[IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Reparse path rejected.'}};$p=[IO.Path]::GetDirectoryName($p)}
 return $full
}
function VerifiedMsi([string]$Path){
 if((Get-Item -LiteralPath $Path).Length -ne $expectedBytes){throw 'Размер пакета Microsoft не совпадает с проверенным.'}
 if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedHash){throw 'Контрольная сумма пакета Microsoft не совпадает.'}
 $signature=Get-AuthenticodeSignature -LiteralPath $Path
 if($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)'){throw 'Подпись Microsoft не подтверждена.'}
}
try{
 if(-not [Environment]::Is64BitProcess){Finish 'error' 'x64_required' 'Откройте 64-разрядный лаунчер.' 1}
 $script:present=ModelPresent;$script:running=@(Get-Process -Name F1_25 -ErrorAction SilentlyContinue).Count -gt 0
 if($script:present){Finish 'ready' 'model_present' 'Русская модель Microsoft уже установлена.' 0}
 if($Action -eq 'status'){Finish 'not_ready' 'missing_russian_model' 'Русская модель Microsoft ещё не установлена. Загрузка: 18 МБ.' 2}
 if($script:running){Finish 'not_ready' 'game_running' 'Закройте F1 25 перед установкой модели.' 2}
 $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
 if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){Finish 'error' 'administrator_required' 'Подтвердите запрос Windows на установку модели.' 1}
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64');$runtime=$null
 try{$runtime=$base.OpenSubKey($tokens);if($null -eq $runtime){Finish 'not_ready' 'missing_runtime' 'Сначала установите компоненты распознавания речи из установщика F1 25, затем повторите проверку.' 2}}finally{if($runtime){$runtime.Dispose()};$base.Dispose()}
 $cache=LocalPath (Join-Path $env:ProgramData 'Karsvein\F1Speech\downloads');[void][IO.Directory]::CreateDirectory($cache)
 $script:lock=[IO.File]::Open((Join-Path $cache '.download.lock'),'OpenOrCreate','ReadWrite','None')
 $msi=LocalPath (Join-Path $cache 'MSSpeech_SR_ru-RU_TELE.msi')
 if(-not [IO.File]::Exists($msi)){
  $part=LocalPath (Join-Path $cache ([guid]::NewGuid().ToString('N')+'.part'))
  $response=$null;$input=$null;$output=$null
  try{
   [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
   $request=[Net.HttpWebRequest]::Create($url);$request.AllowAutoRedirect=$false;$request.Timeout=30000;$request.ReadWriteTimeout=30000
   $response=$request.GetResponse()
   if([int]$response.StatusCode -ne 200 -or $response.ContentLength -ne $expectedBytes){throw 'Сервер вернул неожиданный пакет.'}
   $input=$response.GetResponseStream();$output=[IO.File]::Open($part,'CreateNew','Write','None');$buffer=New-Object byte[] 65536;$total=0
   while(($read=$input.Read($buffer,0,$buffer.Length)) -gt 0){$total+=$read;if($total -gt $expectedBytes){throw 'Превышен размер загрузки.'};$output.Write($buffer,0,$read)}
   $output.Dispose();$output=$null;VerifiedMsi $part;[IO.File]::Move($part,$msi)
  }finally{if($output){$output.Dispose()};if($input){$input.Dispose()};if($response){$response.Dispose()};if([IO.File]::Exists($part)){[IO.File]::Delete($part)}}
 }
 VerifiedMsi $msi
 $script:running=@(Get-Process -Name F1_25 -ErrorAction SilentlyContinue).Count -gt 0
 if($script:running){Finish 'not_ready' 'game_running' 'Игра запущена во время загрузки. Закройте её и повторите установку.' 2}
 $log=LocalPath (Join-Path $cache ('install-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.log'))
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList ('/i "'+$msi+'" /qn /norestart /L*v "'+$log+'"') -WindowStyle Hidden -Wait -PassThru
 if($p.ExitCode -notin @(0,3010)){throw ('Установщик Microsoft завершился с кодом '+$p.ExitCode+'. Журнал: '+$log)}
 $script:present=ModelPresent
 if(-not $script:present){throw 'Установщик завершился, но русская модель не найдена.'}
 Finish 'ready' 'model_installed' 'Русская модель установлена. Теперь включите голосовые команды.' 0 ($p.ExitCode -eq 3010)
}catch{Finish 'error' 'model_error' $_.Exception.Message 1}finally{if($script:lock){$script:lock.Dispose()}}
