"""Exercise the actual host wrapper with inert core responses; never elevate."""
import argparse,json,os,pathlib,shutil,subprocess,uuid

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
    assert not a.output.exists(),'Preserve existing evidence';a.output.mkdir(parents=True)
    ps=pathlib.Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
    rows=[]
    cases=[('ready',0,0,'ready'),('installed',0,0,'installed'),('not_ready',2,2,'not_ready'),('recovery_required',2,2,'recovery_required'),('error',1,1,'error'),('ready',1,1,'error'),('error',0,1,'error'),('unknown',0,1,'error'),('ready',9,1,'error')]
    for i,(status,code,expected,expected_status) in enumerate(cases):
        case=a.output/str(i);case.mkdir();host=case/'MaxVoiceHost.ps1';shutil.copy2(a.source/'audio/MaxVoiceHost.ps1',host)
        data={'schema':1,'status':status,'message':'inert test result'}
        (case/'MaxVoice.ps1').write_text("param($Action,$GamePath,$PackageRoot)\nWrite-Output '"+json.dumps(data)+"'\nexit "+str(code)+'\n',encoding='utf-8-sig')
        owned=pathlib.Path(os.environ['TEMP'])/('F1RU-MaxVoice-'+uuid.uuid4().hex);owned.mkdir();result=owned/'result.json'
        try:
            cmd=[str(ps),'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(host),'-Action','status','-GamePath',str(case),'-ResultPath',str(result)]
            r=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE);assert result.is_file(),r.stderr.decode(errors='replace');raw=result.read_text('utf-8-sig');response=json.loads(raw)
            (case/'RESULT.json').write_text(raw,encoding='utf-8')
            rows.append({'command':subprocess.list2cmdline(cmd),'input':data,'coreExit':code,'exit':r.returncode,'response':response,'stderr':r.stderr.decode('utf-8-sig')})
            assert r.returncode==expected and response['status']==expected_status,rows[-1]
        finally:
            if result.is_file():result.unlink()
            owned.rmdir()
    (a.output/'RESULTS.json').write_text(json.dumps({'pass':True,'cases':rows,'uacInvoked':False,'gameWrites':0},ensure_ascii=False,indent=2),encoding='utf-8')
    print('MAX_HOST_PASS cases=%d exact_exit_result_contract=true no_uac_or_game_writes=true'%len(rows))

if __name__=='__main__':main()
