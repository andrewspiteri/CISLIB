"""Literal independent verification of staged interruption states and actual runtime restarts."""
import argparse,base64,copy,hashlib,importlib.util,json,os,re,subprocess,tempfile
from pathlib import Path
MODES=[kind+'-'+stage for kind in ('rotation','recovery','human') for stage in ('intent','renamed','published','created')]+['ambiguous-both','mismatch-bytes','nonempty-fresh','retirement-no-intent']
ACTIVE='logs/\u03a9-\U0001f642.log'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
decoded=lambda b:base64.b64decode(b,validate=True)
def require(condition,reason):
    if not condition:raise AssertionError(reason)
spec=importlib.util.spec_from_file_location('human_reader',Path(__file__).with_name('verify-file-rotation-policy.py'));reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(reader)
def check(mode,a):
    recovery=mode.startswith('recovery');human=mode.startswith('human');failure=mode in ('ambiguous-both','mismatch-bytes','nonempty-fresh','retirement-no-intent')
    require(a['mode']==mode and a['composition']=='staged-physical-transition-to-configured-runtime','composition')
    require(a['leaseReleased'] and a['dataReleased'],'in-process release')
    group=a['group'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity')
    name=lambda n,state:'.cis-log-'+group+'-'+str(n).zfill(20)+'.'+state
    old_id=2 if recovery else 3;state='damaged' if recovery else 'closed';archived='logs/'+name(old_id,state);pending=ACTIVE+'.cis.transition.json';meta=ACTIVE+'.cis.inventory.json'
    old=[dict(id=1,fileName=name(1,'damaged'),state='damaged')]
    if not recovery:old.append(dict(id=2,fileName=name(2,'closed'),state='closed'))
    old.append(dict(id=old_id,fileName=ACTIVE[5:],state='active'))
    new=(old[:-1] if recovery else old[1:-1])+[dict(id=old_id,fileName=name(old_id,state),state=state),dict(id=old_id+1,fileName=ACTIVE[5:],state='active')]
    head=dict(schema=1,groupId=group,format='human' if human else 'jsonl',encoding='utf-8',bom=False,newline='lf')
    original=b'{"earlier":true}\n{"partial":' if recovery else b'earlier=true\n' if human else b'{"earlier":true}\n'
    stage=mode.split('-',1)[1];stage='renamed' if mode=='mismatch-bytes' else 'created' if mode=='nonempty-fresh' else stage
    before=a['before'];after=a['after']
    pre={'neighbor.bin':b'\xff\x00\xfe','logs/'+name(50,'closed'):b'unlisted neighbor\n',ACTIVE+'.cis.lock':b''}
    pre['logs/'+name(1,'damaged') if recovery else 'logs/'+name(2,'closed')]=b'older damage' if recovery else b'older=true\n' if human else b'{"older":true}\n'
    if stage in ('renamed','published','created'):pre[archived]=b'{"other":true}\n' if mode=='mismatch-bytes' else original
    else:pre[ACTIVE]=original
    if mode=='ambiguous-both':pre[archived]=original
    if stage=='created':pre[ACTIVE]=b'{"fresh":true}\n' if mode=='nonempty-fresh' else b''
    require(json.loads(decoded(before[meta]))==dict(head,segments=new if stage in ('published','created') else old),'initial inventory')
    pre[meta]=decoded(before[meta])
    if mode!='retirement-no-intent':
        raw=decoded(before[pending]);wanted=dict(schema=1,recovery=recovery,retainedLimit=2,originalBytes=len(original),originalSha256=digest(original),previous=dict(head,segments=old),next=dict(head,segments=new))
        require(raw.endswith(b'\n') and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf') and json.loads(raw)==wanted,'pending intent');pre[pending]=raw
    require({key:decoded(value) for key,value in before.items()}==pre,'literal stage preimage')
    if failure:
        code='FileInventoryIncompatible' if mode=='retirement-no-intent' else 'FileTransitionIncompatible'
        require(a['error']==dict(code=code,output='file',message="Logging output 'file' could not initialize ("+code+").",inner=False,dataCount=0),'safe failure')
        require(before==after,'unchanged failure')
        require(a['outcome'] is None and a['final'] is None and not a['recovered'] and not a['restartRecovered'],'failed publication')
        return
    require(a['error'] is None and a['outcome']=='Accepted','successful admission')
    require(a['recovered']==recovery and not a['restartRecovered'] and a['final']['recovered']==recovery,'recovery diagnostics')
    require(a['final']==dict(state='Stopped',accepted=1,written=1,failed=0,abandoned=0,pending=0,recovered=recovery,fault=None),'delivery accounting')
    raw=decoded(after[ACTIVE]);require(raw.endswith(b'\n') and b'\r' not in raw and raw.count(b'\n')==1 and len(raw)<=16384 and not raw.startswith(b'\xef\xbb\xbf'),'fresh framing')
    record=reader.human(raw) if human else json.loads(raw)
    for key,value in dict(component='Transition',event_name='transition.fresh',message='\u03a9 \U0001f642',service='Console.App',env='test',version='1.2.3',timestamp='2026-10-13T10:11:12.0000000Z',attributes=dict(producer_pid=a['pid'])).items():require(record[key]==value,'fresh record')
    require(record['severity' if human else 'status']=='info' and 'truncated' not in record,'fresh record')
    if not human:require(record['schema_version']==1 and record['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','fresh record')
    metadata=decoded(after[meta]);require(metadata.endswith(b'\n') and b'\r' not in metadata and not metadata.startswith(b'\xef\xbb\xbf') and json.loads(metadata)==dict(head,segments=new),'next inventory')
    expected=dict(pre);expected.pop(pending);expected[archived]=original;expected[ACTIVE]=raw;expected[meta]=metadata
    require({key:decoded(value) for key,value in after.items()}==expected,'exact physical transition')
def main():
    p=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):p.add_argument('--'+key,required=True)
    a=p.parse_args();require((os.name=='nt')==(a.family=='windows'),'native OS')
    host=Path(a.host).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
    core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];controls=[];samples={}
    with tempfile.TemporaryDirectory(prefix='cislib-transition-') as temporary:
        root=Path(temporary)
        for mode in MODES:
            work=root/mode;work.mkdir();command=[a.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-transition',mode,str(work)]
            env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            stdout=b'';stderr=b''
            try:
                stdout,stderr=process.communicate(timeout=30);require(process.returncode==0 and stderr==b'','child completion')
                result=json.loads(stdout);require((result['pid'],result['runtime'],result['os'],result['coreSha256'],result['fixtureSha256'])==(process.pid,'.NET 10.0.12',a.family,core,fixture),'actual producer')
                require(result['path']==str(work/'logs'/ACTIVE[5:]),'captured path');check(mode,result)
                physical={file.relative_to(work).as_posix():file.read_bytes() for file in work.rglob('*') if file.is_file()}
                require(physical=={key:decoded(value) for key,value in result['after'].items()},'physical original bytes')
                cases.append(dict(id=mode,pid=process.pid,exitCode=process.returncode,command=command,report=result,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)));samples[mode]=result
            finally:
                if process.poll() is None:process.kill();stdout,stderr=process.communicate(timeout=15)
                (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
                process.stdout.close();process.stderr.close()
        reasons={'composition':'composition','release':'in-process release','intent':'pending intent','code':'safe failure','failure-growth':'unchanged failure','flag':'recovery diagnostics','restart-flag':'recovery diagnostics','accounting':'delivery accounting','record':'fresh record','inventory':'next inventory','archive':'exact physical transition','neighbor':'exact physical transition'}
        for key,reason in reasons.items():
            mode='ambiguous-both' if key in ('code','failure-growth') else 'recovery-renamed';bad=copy.deepcopy(samples[mode]);meta=ACTIVE+'.cis.inventory.json'
            if key=='composition':bad['composition']='other'
            elif key=='release':bad['leaseReleased']=False
            elif key=='intent':
                pending=ACTIVE+'.cis.transition.json';value=json.loads(decoded(bad['before'][pending]));value['originalSha256']='0'*64;bad['before'][pending]=base64.b64encode((json.dumps(value)+'\n').encode()).decode()
            elif key=='code':bad['error']['code']='Other'
            elif key=='failure-growth':bad['after'][ACTIVE]=base64.b64encode(b'changed').decode()
            elif key=='flag':bad['recovered']=False
            elif key=='restart-flag':bad['restartRecovered']=True
            elif key=='accounting':bad['final']['written']=0
            elif key=='record':
                value=json.loads(decoded(bad['after'][ACTIVE]));value['event_name']='other';bad['after'][ACTIVE]=base64.b64encode((json.dumps(value,ensure_ascii=False)+'\n').encode()).decode()
            elif key=='inventory':
                value=json.loads(decoded(bad['after'][meta]));value['segments'][-1]['id']=99;bad['after'][meta]=base64.b64encode((json.dumps(value)+'\n').encode()).decode()
            elif key=='archive':bad['after'][next(name for name in bad['after'] if name.endswith('00000000000000000002.damaged'))]=base64.b64encode(b'changed').decode()
            else:bad['after']['neighbor.bin']=base64.b64encode(b'changed').decode()
            try:check(mode,bad)
            except AssertionError as error:require(str(error)==reason,'specific corruption assertion');controls.append(dict(id=key,rejected=True,reason=reason))
            else:raise AssertionError('accepted corruption '+key)
    report=dict(schema='cis.logging.file-transition-native/v1',task='WP05-S07-T03',family=a.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(family=a.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
