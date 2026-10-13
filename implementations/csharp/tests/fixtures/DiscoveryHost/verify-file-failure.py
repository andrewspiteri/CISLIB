"""Real file-policy faults, protected preimages and owned native deletion-denial controls."""
import argparse,base64,copy,datetime,hashlib,json,os,queue,re,stat,subprocess,tempfile,threading
from pathlib import Path

MODES=['delete-jsonl-directory','delete-human-directory','render-empty','render-active','delete-jsonl-permission','delete-human-permission']
ACTIVE='\u03a9-\U0001f642.log';sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest();digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def human(raw):
    text=raw.decode('utf-8',errors='strict');require(raw.endswith(b'\n') and text.count('\n')==1 and '\r' not in text and not raw.startswith(b'\xef\xbb\xbf'),'Human framing');text=text[:-1];decoder=json.JSONDecoder();pos=0;result={}
    while pos<len(text):
        while pos<len(text) and text[pos]==' ':pos+=1
        if pos==len(text):break
        equal=text.index('=',pos);key=text[pos:equal];value,pos=decoder.raw_decode(text,equal+1);require(key not in result,'duplicate Human field');result[key]=value;require(pos==len(text) or text[pos]==' ','Human separator')
    return result

def record(value,index,pid,view=False):
    for key,wanted in {'component':'Failure','event_name':'failure.'+str(index),'message':'preserved','service':'Console.App','env':'test','version':'1.2.3','timestamp':'2026-10-13T10:11:12.0000000Z','severity' if view else 'status':'info'}.items():require(value[key]==wanted,'literal console record')
    require(value['attributes']==dict(producer_pid=pid) and 'truncated' not in value,'actual record producer')
    if not view:require(value['schema_version']==1 and value['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','protected facts')

def check(mode,a):
    render=mode.startswith('render');code='RenderTooLarge' if render else 'RetentionDeleteFailed';require(a['mode']==mode and a['composition']==('trusted-internal-small-segment' if render else 'configured-runtime'),'composition')
    require(a['error']==dict(code=code,message=f'Logging file output failed ({code}).',inner=False,dataCount=0),'safe fixed error')
    require(a['segmentMaxBytes']==(16 if render else 65536),'segment limit')
    if render:
        raw=base64.b64decode(a['attemptedFileBytes'],validate=True);require(16<len(raw)<=16384 and a['writeAttempts']==1,'single oversized Human attempt');record(human(raw),0,a['pid'],True)
    else:require(a['attemptedFileBytes'] is None and a['writeAttempts'] is None,'configured writer')
    require(a['leaseReleased'] is True and a['activeReleased'] is True,'in-process handle release')
    require(a['borrowedDisposes']==0 and a['launches']==2 and a['factories']==4 and a['stopped']=='Stopped','lifecycle/borrowed')
    require(a['before']==a['after'] and a['namesBefore']==a['namesAfter'],'no growth/preimage')
    wanted=[]
    for index in range(4):
        wanted.append(dict(outcome='PartiallyAccepted' if index==3 else 'Accepted',truncated=False,outputs=[dict(name='console',status='Accepted',sequence=index+1),dict(name='file',status='Faulted' if index==3 else 'Accepted',sequence=None if index==3 else index+1)]))
    require(a['results']==wanted,'admission tickets')
    for phase in ('fault','final'):
        d=a[phase];require(d['state']==('Stopped' if phase=='final' else 'Running') and d['invalid']==d['truncated']==0,'runtime/loss state');outputs={x['name']:x for x in d['outputs']};require(set(outputs)=={'console','file'},'output names');f=outputs['file'];c=outputs['console']
        require(f==dict(name='file',state='Stopped' if phase=='final' else 'Faulted',accepted=3,written=0,failed=1,abandoned=2,pending=0,unavailable=1 if phase=='final' else 0,faultCode=code,faultAt=f['faultAt']),'file terminal accounting')
        require(datetime.datetime.fromisoformat(f['faultAt']).utcoffset()==datetime.timedelta(0),'fault UTC')
        require(c['state']==('Stopped' if phase=='final' else 'Running') and c['faultCode'] is None and c['faultAt'] is None and c['failed']==c['abandoned']==c['unavailable']==0,'healthy console')
        if phase=='final':require(c['accepted']==c['written']==4 and c['pending']==0,'console progress')
        else:require(c['accepted']==3 and c['written']+c['pending']==3,'console prior boundary')
    require(a['fault']['outputs'][1]['faultAt']==a['final']['outputs'][1]['faultAt'],'retained fault')
    raw=base64.b64decode(a['consoleBytes'],validate=True);require(raw.endswith(b'\n') and not raw.startswith(b'\xef\xbb\xbf'),'console framing');lines=raw.splitlines(keepends=True);require(len(lines)==4,'console record count')
    for index,line in enumerate(lines):require(len(line)<=16384 and b'\r' not in line,'console cap/control');record(json.loads(line),index,a['pid'])
    directory='logs/';metadata=base64.b64decode(a['before'][directory+ACTIVE+'.cis.inventory.json'],validate=True);inventory=json.loads(metadata);group=inventory['groupId'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity')
    segments=[dict(id=1,fileName=ACTIVE,state='active')] if mode=='render-empty' else [dict(id=1,fileName='.cis-log-'+group+'-00000000000000000001.closed',state='closed'),dict(id=2,fileName=ACTIVE,state='active')]
    require(inventory==dict(schema=1,groupId=group,format='human' if render or mode.startswith('delete-human') else 'jsonl',encoding='utf-8',bom=False,newline='lf',segments=segments),'unchanged literal inventory')
    active=base64.b64decode(a['before'][directory+ACTIVE],validate=True)
    if mode=='render-empty':require(active==b'','empty active preserved')
    elif mode=='render-active':require(active==b'seed="prior"\n','populated active preserved')
    elif mode.startswith('delete-human'):require(active==b'seed="'+b'a'*65528+b'"\n','full Human seed')
    else:require(active==b'{"seed":"'+b'a'*65524+b'"}\n','full JSONL seed')

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];permissions=[];controls=[]
    sid=None
    if args.family=='windows':
        identity=subprocess.run(['whoami','/user','/fo','csv','/nh'],capture_output=True,check=True);sid=re.search(r'S-1-[0-9-]+',identity.stdout.decode('ascii',errors='ignore')).group()
    else:require(os.geteuid()!=0,'deletion permission controls need unprivileged user')
    with tempfile.TemporaryDirectory(prefix='cislib-file-failure-') as temp:
        root=Path(temp).resolve()
        for mode in MODES:
            work=root/mode;work.mkdir();command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-failure',mode,str(work)]
            env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);p=subprocess.Popen(command,cwd=work,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE);lines=queue.Queue();original=[];stderr=[];reports=[];restore=[];original_acls=[];denied=False;restored=False;original_mode=None;target=None
            def drain():
                for raw in iter(p.stdout.readline,b''):original.append(raw);lines.put(raw)
                lines.put(b'')
            def drain_error():stderr.append(p.stderr.read())
            reader=threading.Thread(target=drain,daemon=True);errors=threading.Thread(target=drain_error,daemon=True);reader.start();errors.start()
            def next_report():
                raw=lines.get(timeout=30);require(raw!=b'','unexpected child exit');value=json.loads(raw.decode('utf-8',errors='strict'));reports.append(value);return value
            try:
                if mode.endswith('permission'):
                    prepared=next_report();require(prepared==dict(stage='prepared',mode=mode,pid=p.pid,path=str(work/'logs'/ACTIVE),victim=prepared['victim']),'prepared owned process')
                    victim=Path(prepared['victim']);target=victim.parent;require(victim.resolve().is_relative_to(work) and target.resolve().is_relative_to(work) and not victim.is_symlink() and not target.is_symlink(),'permission target escaped owned work')
                    original_mode=stat.S_IMODE(target.stat().st_mode)
                    if sid:
                        for path,rights in ((victim,'DE'),(target,'DC')):
                            original_acls.append((path,subprocess.run(['icacls',str(path)],capture_output=True,check=True).stdout));subprocess.run(['icacls',str(path),'/deny','*'+sid+':('+rights+')'],capture_output=True,check=True);restore.append(['icacls',str(path),'/remove:d','*'+sid])
                    else:os.chmod(target,0o500)
                    try:victim.unlink()
                    except PermissionError:denied=True
                    require(denied,'actual OS delete was not denied');p.stdin.write(b'continue\n');p.stdin.flush()
                a=next_report();p.wait(timeout=30);reader.join(timeout=10);errors.join(timeout=10);require(not reader.is_alive() and not errors.is_alive() and p.returncode==0 and b''.join(stderr)==b'','child failure/drain')
                require((a['pid'],a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==(p.pid,'.NET 10.0.12',args.family,core,fixture),'actual process/runtime/assembly');check(mode,a)
                require(a['path']==str(work/'logs'/ACTIVE),'captured path')
                for name,value in a['after'].items():
                    path=work/name;require(path.resolve().is_relative_to(work) and path.read_bytes()==base64.b64decode(value,validate=True),'physical protected preimage')
                require(sorted(x.name for x in (work/'logs').iterdir())==a['namesAfter'],'physical exact membership')
                stdout=b''.join(original);errorbytes=b''.join(stderr);(out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(errorbytes);cases.append(dict(id=mode,pid=p.pid,command=command,exitCode=p.returncode,reports=reports,stdoutSha256=digest(stdout),stderrSha256=digest(errorbytes)))
                if mode=='delete-jsonl-directory':sample=copy.deepcopy(a)
            finally:
                cleanup_errors=[]
                # Terminate only this owned child before restore; attempt every restore even if one fails.
                try:
                    if p.poll() is None:p.kill();p.wait(timeout=15)
                except Exception as error:cleanup_errors.append(error)
                for change in reversed(restore):
                    try:subprocess.run(change,capture_output=True,check=True)
                    except Exception as error:cleanup_errors.append(error)
                try:
                    if target is not None and not sid and original_mode is not None:os.chmod(target,original_mode);require(stat.S_IMODE(target.stat().st_mode)==original_mode,'directory mode restoration')
                    if sid:
                        for path,original_acl in original_acls:require(subprocess.run(['icacls',str(path)],capture_output=True,check=True).stdout==original_acl,'owned ACL restoration')
                    restored=mode.endswith('permission') and not cleanup_errors
                except Exception as error:cleanup_errors.append(error)
                try:
                    reader.join(timeout=10);errors.join(timeout=10);require(not reader.is_alive() and not errors.is_alive(),'cleanup drain');(out/(mode+'.stdout.bin')).write_bytes(b''.join(original));(out/(mode+'.stderr.bin')).write_bytes(b''.join(stderr))
                finally:p.stdin.close();p.stdout.close();p.stderr.close()
                if cleanup_errors:raise cleanup_errors[0]
            if mode.endswith('permission'):permissions.append(dict(id=mode,actualDeleteDenied=denied,restored=restored,subject='current-windows-user' if sid else 'unprivileged-linux-user'))
        reasons={'fault-code':'file terminal accounting','growth':'no growth/preimage','console':'console progress','abandoned':'file terminal accounting','release':'in-process handle release','private':'safe fixed error'}
        for key,reason in reasons.items():
            bad=copy.deepcopy(sample)
            if key=='fault-code':bad['fault']['outputs'][1]['faultCode']='WriteFailed'
            elif key=='growth':bad['namesAfter'].append('new.closed')
            elif key=='console':bad['final']['outputs'][0]['written']=0
            elif key=='abandoned':bad['final']['outputs'][1]['abandoned']=0
            elif key=='release':bad['leaseReleased']=False
            else:bad['error']['message']='private filesystem content'
            try:check('delete-jsonl-directory',bad)
            except AssertionError as error:require(str(error)==reason,'control reached wrong assertion');controls.append(dict(id=key,rejected=True,reason=str(error)))
            else:raise AssertionError('corruption accepted: '+key)
    report=dict(schema='cis.logging.file-policy-failures-native/v1',task='WP05-S06-T03',family=args.family,runtime='.NET 10.0.12',children=6,coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,permissions=permissions,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=6,permissions=2,controls=6,passed=True)))
if __name__=='__main__':main()
