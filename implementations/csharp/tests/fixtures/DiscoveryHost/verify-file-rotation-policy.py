"""Owned physical rotation faults; controlled storage errors and Linux process-only write cap."""
import argparse,base64,copy,datetime,hashlib,json,os,queue,re,signal,subprocess,tempfile,threading
from pathlib import Path

MODES=['collision-jsonl-file','collision-human-file','collision-jsonl-directory','collision-human-directory','metadata-jsonl-directory','metadata-human-directory','write-jsonl-failure','write-human-failure']
ACTIVE='\u03a9-\U0001f642.log';sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest();digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def decoded(value):return base64.b64decode(value,validate=True)
def human(raw):
    text=raw.decode('utf-8',errors='strict');require(raw.endswith(b'\n') and text.count('\n')==1 and '\r' not in text and not raw.startswith(b'\xef\xbb\xbf'),'Human framing');text=text[:-1];decoder=json.JSONDecoder();pos=0;result={}
    while pos<len(text):
        while pos<len(text) and text[pos]==' ':pos+=1
        if pos==len(text):break
        equal=text.index('=',pos);key=text[pos:equal];value,pos=decoder.raw_decode(text,equal+1);require(key not in result,'duplicate Human field');result[key]=value;require(pos==len(text) or text[pos]==' ','Human separator')
    return result

def record(raw,index,pid,view=False):
    require(raw.endswith(b'\n') and b'\r' not in raw and raw.count(b'\n')==1 and len(raw)<=16384 and not raw.startswith(b'\xef\xbb\xbf'),'record framing/cap');a=human(raw) if view else json.loads(raw)
    for key,wanted in {'component':'Policy','event_name':'policy.'+str(index),'message':'\u03a9 \U0001f642 e\u0301\nline','service':'Console.App','env':'test','version':'1.2.3','timestamp':'2026-10-13T10:11:12.0000000Z','severity' if view else 'status':'info'}.items():require(a[key]==wanted,'literal record')
    require(a['attributes']==dict(producer_pid=pid) and 'truncated' not in a,'producer/loss')
    if not view:require(a['schema_version']==1 and a['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','protected facts')

def check(mode,a):
    write=mode.startswith('write');limited=mode=='write-jsonl-os-limit';metadata=mode.startswith('metadata');human_format='human' in mode;written=int(write and not limited)
    require(a['mode']==mode and a['composition']==('configured-runtime-os-capacity' if limited else 'controlled-write-with-real-first-record' if write else 'configured-runtime'),'composition')
    require(a['directoryInitiallyMissing'] is True and a['leaseReleased'] is True and a['dataReleased'] is True,'physical creation/release')
    require(a['borrowedDisposes']==0 and a['factories']==4 and a['launches']==2 and a['stopped']=='Stopped','lifecycle')
    require(a['exceptionType'] in (('ArgumentOutOfRangeException',) if limited else ('IOException','UnauthorizedAccessException')) and a['restartError']==('FileTransitionInitializationFailed' if metadata else None),'safe IO/restart')
    for phase in ('fault','final'):
        d=a[phase];require(d['state']==('Running' if phase=='fault' else 'Stopped'),'runtime state');outputs={x['name']:x for x in d['outputs']};require(set(outputs)=={'console','file'},'output names');f=outputs['file'];c=outputs['console']
        require(f['state']==('Faulted' if phase=='fault' else 'Stopped') and f['faultCode']=='WriteFailed' and datetime.datetime.fromisoformat(f['faultAt']).utcoffset()==datetime.timedelta(0),'terminal fault')
        require({k:f[k] for k in ('accepted','written','failed','abandoned','pending','unavailable')}==dict(accepted=3,written=written,failed=1,abandoned=2-written,pending=0,unavailable=int(phase=='final')),'file accounting')
        require(c['faultCode'] is None and c['faultAt'] is None and c['failed']==c['abandoned']==c['unavailable']==0 and c['state']==('Running' if phase=='fault' else 'Stopped'),'healthy console')
        require((c['accepted']==3 and c['written']+c['pending']==3) if phase=='fault' else (c['accepted']==c['written']==4 and c['pending']==0),'console progress')
    require(a['fault']['outputs'][1]['faultAt']==a['final']['outputs'][1]['faultAt'],'retained fault')
    wanted=[dict(outcome='PartiallyAccepted' if i==3 else 'Accepted',truncated=False,outputs=[dict(name='console',status='Accepted',sequence=i+1),dict(name='file',status='Faulted' if i==3 else 'Accepted',sequence=None if i==3 else i+1)]) for i in range(4)];require(a['results']==wanted,'admission tickets')
    lines=decoded(a['consoleBytes']).splitlines(keepends=True);require(len(lines)==4,'console records')
    for index,line in enumerate(lines):record(line,index,a['pid'])
    group=a['group'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity');active='logs/'+ACTIVE;meta=active+'.cis.inventory.json';closed='logs/.cis-log-'+group+'-00000000000000000002.closed';oldest=closed.replace('00000000000000000002.closed','00000000000000000001.closed');before=a['before'];after=a['after'];expected=copy.deepcopy(before)
    neighbors=['archive/keep.log','logs/other.log','logs/.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed','logs/.cis-log-'+group+'-00000000000000000050.closed']
    for name in neighbors:require(decoded(before[name])==b'untouched neighbor\n','literal neighbor')
    head=dict(schema=1,groupId=group,format='human' if human_format else 'jsonl',encoding='utf-8',bom=False,newline='lf');keys=set(neighbors+[active,meta+'/keep.txt' if metadata else meta]);candidate=None
    if write:
        require(decoded(before[active])==b'' and json.loads(decoded(before[meta]))==dict(head,segments=[dict(id=1,fileName=ACTIVE,state='active')]),'empty valid preimage')
        if not limited:require(a['controlledWriteAttempts']==2 and a['firstRecordBytes'] is not None,'controlled attempts');record(decoded(a['firstRecordBytes']),0,a['pid'],human_format);expected[active]=a['firstRecordBytes']
        else:require(a['controlledWriteAttempts'] is None and a['firstRecordBytes'] is None,'OS capacity composition')
    else:
        require(a['controlledWriteAttempts'] is None and a['firstRecordBytes'] is None,'configured writer');keys.add(oldest);require(decoded(before[oldest])==(b'retained=true\n' if human_format else b'{"retained":true}\n'),'literal oldest preimage');require(decoded(before[active])==(b'seed="'+b'a'*65528+b'"\n' if human_format else b'{"seed":"'+b'a'*65524+b'"}\n'),'full segment preimage')
        if metadata:
            require(decoded(before[meta+'/keep.txt'])==b'metadata collision\n','metadata collision');expected[closed]=expected.pop(active);expected.pop(oldest)
            candidates=[x for x in after if x.startswith(meta+'.') and x.endswith('.tmp')];require(len(candidates)==1,'private inventory candidate');candidate=candidates[0];require(re.fullmatch(re.escape(meta)+r'\.[0-9a-f]{32}\.tmp',candidate),'candidate name');raw=decoded(after[candidate]);require(raw.endswith(b'\n') and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'),'candidate framing');require(json.loads(raw)==dict(head,segments=[dict(id=2,fileName=closed[5:],state='closed'),dict(id=3,fileName=ACTIVE,state='active')]),'candidate inventory');expected[candidate]=after[candidate]
            pending=active+'.cis.transition.json';raw=decoded(after[pending]);prior=decoded(before[active]);previous=dict(head,segments=[dict(id=1,fileName=oldest[5:],state='closed'),dict(id=2,fileName=ACTIVE,state='active')]);updated=dict(head,segments=[dict(id=2,fileName=closed[5:],state='closed'),dict(id=3,fileName=ACTIVE,state='active')])
            require(raw.endswith(b'\n') and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf') and json.loads(raw)==dict(schema=1,recovery=False,retainedLimit=1,originalBytes=len(prior),originalSha256=digest(prior),previous=previous,next=updated),'transition intent');expected[pending]=after[pending]
        else:
            require(json.loads(decoded(before[meta]))==dict(head,segments=[dict(id=1,fileName=oldest[5:],state='closed'),dict(id=2,fileName=ACTIVE,state='active')]),'literal initial inventory');collision=closed if mode.endswith('file') else closed+'/keep.txt';keys.add(collision);require(decoded(before[collision])==b'unrelated collision\n','collision bytes')
    require(set(before)==keys,'exact preimage membership');require(after==expected,'exact filesystem transition')
    names=lambda items:sorted({x[5:].split('/')[0] for x in items if x.startswith('logs/')}|{ACTIVE+'.cis.lock'})
    require(a['namesBefore']==names(before) and a['namesAfter']==names(after),'exact membership')

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];controls=[];limits=[];modes=MODES+(['write-jsonl-os-limit'] if args.family=='linux' else [])
    with tempfile.TemporaryDirectory(prefix='cislib-rotation-policy-') as temp:
        root=Path(temp).resolve()
        for mode in modes:
            work=root/mode;work.mkdir();limited=mode=='write-jsonl-os-limit';command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-rotation-policy',mode,str(work)];env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
            options=dict(preexec_fn=lambda:signal.signal(signal.SIGXFSZ,signal.SIG_IGN)) if limited else {};p=subprocess.Popen(command,cwd=work,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,**options);lines=queue.Queue();original=[];stderr=[];reports=[];capacity=None
            def drain():
                for raw in iter(p.stdout.readline,b''):original.append(raw);lines.put(raw)
                lines.put(b'')
            def drain_error():stderr.append(p.stderr.read())
            reader=threading.Thread(target=drain,daemon=True);errors=threading.Thread(target=drain_error,daemon=True);reader.start();errors.start()
            def next_report():
                raw=lines.get(timeout=30);require(raw!=b'','unexpected child exit');value=json.loads(raw.decode('utf-8',errors='strict'));reports.append(value);return value
            try:
                if limited:
                    import resource
                    prepared=next_report();require(prepared==dict(stage='prepared',mode=mode,pid=p.pid,path=str(work/'logs'/ACTIVE)),'prepared owned child');old=resource.prlimit(p.pid,resource.RLIMIT_FSIZE);resource.prlimit(p.pid,resource.RLIMIT_FSIZE,(0,old[1]));require(resource.prlimit(p.pid,resource.RLIMIT_FSIZE)==(0,old[1]),'owned process file-size cap');capacity=dict(id=mode,softLimit=0,verifiedBeforeRelease=True);p.stdin.write(b'continue\n');p.stdin.flush()
                a=next_report();p.wait(timeout=30);reader.join(timeout=10);errors.join(timeout=10);require(not reader.is_alive() and not errors.is_alive() and p.returncode==0 and b''.join(stderr)==b'','child failure/drain');require((a['pid'],a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==(p.pid,'.NET 10.0.12',args.family,core,fixture),'actual process/runtime/assembly');check(mode,a);require(a['path']==str(work/'logs'/ACTIVE),'captured path')
                for name,value in a['after'].items():
                    path=work/name;require(path.resolve().is_relative_to(work) and path.read_bytes()==decoded(value),'physical original bytes')
                require(sorted(x.name for x in (work/'logs').iterdir())==a['namesAfter'],'physical exact membership');require({x.relative_to(work).as_posix() for x in work.rglob('*') if x.is_file() and x!=work/'logs'/(ACTIVE+'.cis.lock')}==set(a['after']),'physical leaf membership')
                stdout=b''.join(original);errorbytes=b''.join(stderr);cases.append(dict(id=mode,pid=p.pid,command=command,exitCode=p.returncode,reports=reports,stdoutSha256=digest(stdout),stderrSha256=digest(errorbytes)))
                if mode=='collision-jsonl-file':sample=copy.deepcopy(a)
                if mode=='metadata-jsonl-directory':publication=copy.deepcopy(a)
            finally:
                try:
                    if p.poll() is None:p.kill();p.wait(timeout=15)
                finally:
                    reader.join(timeout=10);errors.join(timeout=10);(out/(mode+'.stdout.bin')).write_bytes(b''.join(original));(out/(mode+'.stderr.bin')).write_bytes(b''.join(stderr));p.stdin.close();p.stdout.close();p.stderr.close()
            if capacity is not None:require(p.poll() is not None,'owned cap process still live');capacity['processExited']=True;limits.append(capacity)
        reasons={'filesystem':'exact filesystem transition','membership':'exact membership','code':'terminal fault','console':'console progress','release':'physical creation/release','accounting':'file accounting','candidate-content':'candidate inventory','transition-content':'transition intent'}
        for key,reason in reasons.items():
            bad=copy.deepcopy(publication if key in ('candidate-content','transition-content') else sample);mode='metadata-jsonl-directory' if key in ('candidate-content','transition-content') else 'collision-jsonl-file'
            if key=='filesystem':bad['after'][next(x for x in bad['after'] if x.endswith('00000000000000000001.closed') and 'ffffffff' not in x)]=''
            elif key=='membership':bad['namesAfter'].append('new.closed')
            elif key=='code':bad['fault']['outputs'][1]['faultCode']='Other'
            elif key=='console':bad['final']['outputs'][0]['written']=0
            elif key=='release':bad['leaseReleased']=False
            elif key=='accounting':bad['final']['outputs'][1]['abandoned']=0
            elif key=='transition-content':
                pending=next(x for x in bad['after'] if x.endswith('.cis.transition.json'));value=json.loads(decoded(bad['after'][pending]));value['originalSha256']='0'*64;bad['after'][pending]=base64.b64encode((json.dumps(value)+'\n').encode()).decode('ascii')
            else:
                candidate=next(x for x in bad['after'] if x.endswith('.tmp'));value=json.loads(decoded(bad['after'][candidate]));value['segments'][-1]['id']=1;bad['after'][candidate]=base64.b64encode((json.dumps(value)+'\n').encode()).decode('ascii')
            try:check(mode,bad)
            except AssertionError as error:require(str(error)==reason,'control reached wrong assertion');controls.append(dict(id=key,rejected=True,reason=str(error)))
            else:raise AssertionError('corruption accepted: '+key)
    report=dict(schema='cis.logging.file-rotation-policy-native/v1',task='WP05-S06-T04',family=args.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,limits=limits,controls=controls);(out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=len(cases),processCapacityLimits=len(limits),controls=8,passed=True)))
if __name__=='__main__':main()
