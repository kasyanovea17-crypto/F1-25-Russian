"""Compile production diagnostics, exercise typed Windows errors without triggering any."""
from pathlib import Path
import argparse, json, os, subprocess, sys

TEST = r'''using System;using System.ComponentModel;using System.IO;
internal static class DiagnosticRepairTests {
 static int checks;static void Check(bool b,string n){checks++;if(!b)throw new Exception(n);}
 static int Main(){try{
  foreach(int n in new[]{225,226}){
   Check(StartupDiagnostics.Kind(new Win32Exception(n,"fixture"))=="protection","native detection "+n);
   Check(StartupDiagnostics.Kind(new IOException("fixture",unchecked((int)0x80070000)|n))=="protection","IO detection "+n);
   Check(StartupDiagnostics.Kind(new Exception("outer",new Win32Exception(n,"fixture")))=="protection","wrapped "+n);
   Check(StartupDiagnostics.Message(new Win32Exception(n,"fixture")).Contains("Журнал защиты"),"history "+n);
  }
  Check(StartupDiagnostics.Kind(new Win32Exception(1223,"fixture"))=="cancelled","cancel distinct");
  Check(StartupDiagnostics.Kind(new Win32Exception(740,"fixture"))=="elevation","elevation distinct");
  Check(StartupDiagnostics.Kind(new Win32Exception(577,"fixture"))=="signature","signature distinct");
  Check(StartupDiagnostics.Kind(new Win32Exception(5,"fixture"))=="access","denied not antivirus");
  Check(StartupDiagnostics.Kind(new Win32Exception(32,"fixture"))=="locked","locked not antivirus");
  Check(StartupDiagnostics.Kind(new Win32Exception(1260,"fixture"))=="policy","policy not antivirus");
  Check(StartupDiagnostics.Kind(new InvalidDataException("fixture"))=="other","hash not antivirus");
  Console.WriteLine("DIAGNOSTICS_REPAIR_PASS checks="+checks+" filesystem_writes=0 protection_changes=0");return 0;
 }catch(Exception e){Console.WriteLine("DIAGNOSTICS_REPAIR_FAIL "+e.Message);return 1;}}
}'''

def main():
 p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 a.out.mkdir(parents=True,exist_ok=False);test=a.out/'DiagnosticRepairTests.cs';test.write_text(TEST,encoding='utf-8-sig')
 exe=a.out/'DiagnosticRepairTests.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 commands=[]
 for argv in ([str(csc),'/nologo','/utf8output','/target:exe','/warn:4','/warnaserror+','/out:'+str(exe),str(test),str(a.source/'StartupDiagnostics.cs')],[str(exe)]):
  r=subprocess.run(argv,stdin=subprocess.DEVNULL,capture_output=True,text=True,encoding='utf-8',errors='replace');commands.append(dict(argv=argv,input='synthetic exceptions; stdin empty',stdout=r.stdout,stderr=r.stderr,exit_status=r.returncode))
  (a.out/'COMMANDS.json').write_text(json.dumps(commands,ensure_ascii=False,indent=2),encoding='utf-8');print(r.stdout.strip())
  if r.returncode:print(r.stderr);return r.returncode
 return 0
if __name__=='__main__':sys.exit(main())
