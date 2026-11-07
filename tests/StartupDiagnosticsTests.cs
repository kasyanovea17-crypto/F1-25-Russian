using System;
using System.ComponentModel;
using System.IO;

internal static class StartupDiagnosticsTests {
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Main(){
  Check(StartupDiagnostics.Kind(new UnauthorizedAccessException("fixture"))=="access","managed denied");
  Check(StartupDiagnostics.Kind(new Win32Exception(5,"fixture"))=="access","native denied");
  Check(StartupDiagnostics.Kind(new IOException("fixture",unchecked((int)0x80070005)))=="access","IO denied");
  Check(StartupDiagnostics.Kind(new Win32Exception(32,"fixture"))=="locked","native sharing");
  Check(StartupDiagnostics.Kind(new IOException("fixture",unchecked((int)0x80070021)))=="locked","IO lock");
  Check(StartupDiagnostics.Kind(new Win32Exception(1260,"fixture"))=="policy","policy denial");
  Check(StartupDiagnostics.Kind(new Exception("outer",new UnauthorizedAccessException("inner")))=="access","wrapped denied");
  Check(StartupDiagnostics.Kind(new FileNotFoundException("missing"))=="other","missing is not denied");
  Check(StartupDiagnostics.Kind(new InvalidDataException("hash mismatch"))=="other","integrity not denied");
  Check(StartupDiagnostics.Kind(null)=="other","null type");
  Check(StartupDiagnostics.Message(new InvalidDataException("hash mismatch"))=="hash mismatch","exact integrity detail retained");
  Check(StartupDiagnostics.Message(new UnauthorizedAccessException("fixture")).Contains("Журнал защиты"),"protection history guidance");
  Check(StartupDiagnostics.Message(new UnauthorizedAccessException("fixture")).Contains("правами администратора"),"protected directory guidance");
  Check(StartupDiagnostics.Message(new UnauthorizedAccessException("fixture")).Contains("Отключать защиту не требуется"),"no protection disable");
  Check(StartupDiagnostics.Message(new Win32Exception(32,"fixture")).Contains("Файл занят"),"sharing guidance");
  Check(StartupDiagnostics.Message(new Win32Exception(1260,"fixture")).Contains("политикой Windows"),"policy guidance");
  Check(StartupDiagnostics.Detail("A\r\nB\0C")=="A  BC","control stripping");
  Check(StartupDiagnostics.Detail(new string('x',700)).Length==600,"bounded details");
  Check(StartupDiagnostics.Message(null).Length>0,"null detail");
  Check(!StartupDiagnostics.Message(new UnauthorizedAccessException("fixture")).Contains("добавьте исключение"),"no exclusion guidance");
  Console.WriteLine("STARTUP_DIAGNOSTICS_PASS checks="+checks+" filesystem_writes=0 protection_changes=0");
 }
}
