"""Compile production wrapper; simulate transport outcomes, never UAC/game writes."""
import argparse,json,os,re,subprocess,sys
from pathlib import Path

def main():
 p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 a.output.mkdir(parents=True,exist_ok=False);source=(a.source/'Launcher.cs').read_text('utf-8-sig')
 start=source.index(' internal static Outcome Execute(string home,string action,string game,string variant){')
 end=source.index(' static Outcome ExecuteCore(',start);wrapper=source[start:end]
 assertions=[
  ('no child exit invented', 'int observedExit=-1;' in wrapper),
  ('native exception retained in Outcome', 'DiagnosticError=ex' in source),
  ('native1223 existing UI result retained', 'Code=1223,DiagnosticError=ex' in source),
  ('wrapper logs original diagnostic exception', 'result.Output,result.DiagnosticError' in wrapper),
  ('catch preserves observed exit', 'End(operation,observedExit,"' in wrapper),
  ('mutation exit captured before parse', bool(re.search(r'process\.WaitForExit\(\);observedExit=process\.ExitCode;SupportDiagnostics\.Stage\(operation,"result.validate"[^\n]+return ParseResult',source))),
  ('read-only exit captured before return', 'process.WaitForExit();observedExit=process.ExitCode;return new Outcome' in source),
 ]
 assert all(ok for _,ok in assertions),assertions
 body=r'''using System;using System.IO;using System.ComponentModel;
static class SupportDiagnostics{
 internal static int Code;internal static Exception Error;internal static int Calls;
 internal static string Begin(string c,string a,string g){return "fixture";}
 internal static void End(string id,int code,string detail,Exception e){Code=code;Error=e;Calls++;}
}
static class TextWrapperTests{
 internal sealed class Outcome{internal int Code;internal string Output;internal Exception DiagnosticError;}
 static Exception Expected;
 WRAPPER
 static Outcome ExecuteCore(string h,string a,string g,string v,string op,ref int observed){
  if(a=="cancel"){Expected=new Win32Exception(1223,"fixture");return new Outcome{Code=1223,Output="cancelled",DiagnosticError=Expected};}
  if(a=="invalid-response"){observed=0;Expected=new InvalidDataException("fixture result");throw Expected;}
  if(a=="before-launch"){Expected=new IOException("fixture package");throw Expected;}
  observed=5;return new Outcome{Code=5,Output="backend failure"};
 }
 static int checks;static void Check(bool b,string m){if(!b)throw new Exception(m);checks++;}
 static int Main(){try{
  var r=Execute("","cancel","","");Check(r.Code==1223,"UI cancel unchanged");Check(SupportDiagnostics.Code==-1,"cancel not process exit");Check(Object.ReferenceEquals(SupportDiagnostics.Error,Expected),"native instance retained");Check(((Win32Exception)SupportDiagnostics.Error).NativeErrorCode==1223,"native code retained");
  try{Execute("","invalid-response","","");throw new Exception("expected throw");}catch(InvalidDataException e){Check(Object.ReferenceEquals(e,Expected),"same parse exception rethrown");}Check(SupportDiagnostics.Code==0,"observed zero retained after invalid response");Check(Object.ReferenceEquals(SupportDiagnostics.Error,Expected),"parse exception retained in end");
  try{Execute("","before-launch","","");throw new Exception("expected throw");}catch(IOException e){Check(Object.ReferenceEquals(e,Expected),"same package exception rethrown");}Check(SupportDiagnostics.Code==-1,"prelaunch not invented");
  r=Execute("","backend","","");Check(r.Code==5&&SupportDiagnostics.Code==5,"backend exact exit");Check(SupportDiagnostics.Error==null,"backend response not synthetic exception");Check(SupportDiagnostics.Calls==4,"one end per operation");
  Console.WriteLine("TEXT_JOURNAL_WRAPPER_PASS checks="+checks+" production_wrapper=true simulated_transports=4 uac_invoked=false game_writes=0");return 0;
 }catch(Exception e){Console.WriteLine("TEXT_JOURNAL_WRAPPER_FAIL "+e);return 1;}}
}'''.replace(' WRAPPER',wrapper)
 test=a.output/'TextWrapperTests.cs';test.write_text(body,encoding='utf-8-sig');exe=a.output/'TextWrapperTests.exe'
 csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';commands=[]
 for argv in [[str(csc),'/nologo','/utf8output','/target:exe','/warn:4','/warnaserror+','/out:'+str(exe),str(test)],[str(exe)]]:
  r=subprocess.run(argv,capture_output=True,encoding='utf-8',errors='replace');commands.append(dict(argv=argv,input='production wrapper, controlled transport outcomes; stdin unused',stdout=r.stdout,stderr=r.stderr,exit_status=r.returncode));print(r.stdout.strip())
  (a.output/'COMMANDS.json').write_text(json.dumps({'sourceAssertions':assertions,'commands':commands},ensure_ascii=False,indent=2),encoding='utf-8')
  if r.returncode:print(r.stderr);return r.returncode
 print('TEXT_JOURNAL_SOURCE_PASS checks='+str(len(assertions)))
 return 0
if __name__=='__main__':sys.exit(main())
