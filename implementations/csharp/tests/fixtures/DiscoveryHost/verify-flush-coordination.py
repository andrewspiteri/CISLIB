"""Pinned native Core caller-detachment/control bounds; public timeout results remain T03."""
import argparse,base64,copy,hashlib,json,os,re,subprocess,tempfile
from pathlib import Path
MODES=['write-cancel','write-deadline','flush-cancel','flush-deadline','cohort','dual','pre-cancel','fault','complete-race']
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def pairs(items):
    result={}
    for key,value in items:
        require(key not in result,'duplicate JSON key');result[key]=value
    return result
def load(raw):return json.loads(raw.decode('utf-8',errors='strict'),object_pairs_hook=pairs,parse_constant=lambda token:(_ for _ in ()).throw(AssertionError('nonfinite JSON')))
def decoded(value):return base64.b64decode(value,validate=True)
def output(boundary,filtered=0,full=0):return dict(boundary=str(boundary),filtered=str(filtered),queueFull=str(full),unavailable='0')
def records(raw,pid):
    require(not raw or raw.endswith(b'\n'),'complete record boundary')
    result=[]
    for line in raw.splitlines(keepends=True):
        require(line.endswith(b'\n') and b'\r' not in line and not line.startswith(b'\xef\xbb\xbf') and len(line)<=65536,'whole bounded record')
        d=load(line)
        for key,value in dict(schema_version=1,timestamp='2026-10-13T17:18:19.0000000Z',service='Console.App',env='test',version='1.2.3',component='FlushCoordination',message='\u03a9 \U0001f642').items():
            require(d[key]==value,'literal wire identity')
        require(d['attributes']==dict(producer_pid=pid),'actual wire producer')
        require(d['resource']['instance_id']=='11111111-1111-1111-1111-111111111111' and 'truncated' not in d,'protected complete record')
        result.append(d)
    return result
def check(mode,a,pid=None):
    require(a['mode']==mode,'mode')
    if pid is not None:require(a['pid']==pid,'actual child PID')
    repeated=mode in MODES[:4];both=mode in ('cohort','dual');accepted=4 if mode.startswith('write') else 3 if repeated or mode=='cohort' else 2 if mode in ('dual','fault') else 1
    require(a['capacity']==(2 if mode in ('cohort','dual','fault') else 3) and a['physicalFiles']==0,'owned memory streams')
    require(a['iterations']==(10000 if repeated else 0),'all repeated callers')
    require(a['retainedAfterDetach']==0 and a['finalControls']==0,'detached caller references')
    require(a['controlsPeak']==(4 if repeated else 3 if mode=='cohort' else 2 if mode in ('dual','fault') else 1),'bounded console controls')
    require(a['fileControlsPeak']==(3 if mode=='cohort' else 1 if mode=='dual' else 0),'bounded file controls')
    attached=1 if repeated or mode in ('dual','fault') else 64 if mode=='cohort' else 16 if mode=='complete-race' else 0
    require(a['attachedPeak']==attached,'actual attachment cohort')
    require(a['continuationSlotsPeak']==(4 if repeated else attached) and a['continuationSlotsAfterDetach']==(4 if repeated else 0),'bounded continuation storage')
    require(a['shared'] is True,'coalesced caller targets')
    require(a['deadlineFires']==(10000 if mode.endswith('deadline') else 16 if mode=='cohort' else 0),'actual deadline callbacks')
    if mode=='complete-race':
        require(a['cancelled']+a['succeeded']==16 and a['failed']==0 and len(a['race'])==16 and all(x in ('cancelled','succeeded') for x in a['race']),'settled allowed race')
        require(a['race'].count('cancelled')==a['cancelled'] and a['race'].count('succeeded')==a['succeeded'],'truthful race totals')
    else:
        require(a['cancelled']==(10000 if repeated else 48 if mode=='cohort' else 2 if mode=='pre-cancel' else 1 if mode=='dual' else 0),'caller cancellation count')
        require(a['succeeded']==(16 if mode=='cohort' else 0 if mode=='fault' else 1) and a['failed']==int(mode=='fault'),'actual caller writer result')
        require(a['race']==[],'no invented race')
    require(a['completedTokenCancelled'] is (mode=='pre-cancel'),'cancelled completed caller')
    expected_phases={
        'cohort':[dict(consoleControls=3,fileControls=3,consoleAttached=64,consoleSlots=64,fileAttached=0),dict(consoleControls=3,fileControls=3,consoleAttached=16,consoleSlots=64,fileAttached=0)],
        'dual':[dict(consoleControls=2,fileControls=1,consoleAttached=1,consoleSlots=1,fileAttached=0),dict(consoleControls=0,fileControls=1,consoleAttached=0,consoleSlots=0,fileAttached=1),dict(consoleControls=0,fileControls=1,consoleAttached=0,consoleSlots=0,fileAttached=0)],
        'pre-cancel':[dict(consoleControls=1,fileControls=0,consoleAttached=0,consoleSlots=0,fileAttached=0)]
    }.get(mode,[dict(consoleControls=4,fileControls=0,consoleAttached=0,consoleSlots=4,fileAttached=0)] if repeated else [])
    require(a['phases']==expected_phases,'live caller transition phases')
    snapshots=a['snapshots'];require(len(snapshots)==(6 if repeated else 3 if mode in ('cohort','dual') else 2 if mode=='fault' else 1),'owned snapshot count')
    for s in snapshots:
        require(re.fullmatch(r'[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}\+00:00',s['capturedAtUtc']),'UTC label')
        require(s['truncated']=='0','no invented truncation')
    if repeated:
        seed=0 if mode.startswith('flush') else 1
        for i,s in enumerate(snapshots[:4]):require(s['console']==output(seed+i) and s['file'] is None and s['invalid']=='0','distinct fixed target')
        for s,full in zip(snapshots[4:],(1,4)):require(s['console']==output(accepted,full=full) and s['file'] is None and s['invalid']=='1','original caller rejection facts')
    elif mode=='cohort':
        for i,s in enumerate(snapshots,1):require(s['console']==s['file']==output(i) and s['invalid']=='0','atomic shared targets')
    elif mode=='dual':
        require(snapshots[0]['console']==output(1) and snapshots[0]['file']==output(0,1) and snapshots[0]['invalid']=='0','seed independent targets')
        require(snapshots[1]['console']==output(2) and snapshots[1]['file']==output(1,1) and snapshots[1]['invalid']=='0','independent accepted targets')
        require(snapshots[2]['console']==output(2,1) and snapshots[2]['file']==output(1,2) and snapshots[2]['invalid']=='1','original global observations')
    else:
        for i,s in enumerate(snapshots,1):require(s['console']==output(i) and s['file'] is None and s['invalid']=='0','single fixed target')
    require((a['finalInvalid'],a['finalTruncated'])==('1' if repeated or mode=='dual' else '0','0'),'final global counters')
    names=['console','file'] if both else ['console'];require([x['name'] for x in a['outputs']]==names,'enabled destinations')
    for d in a['outputs']:
        name=d['name'];total=1 if mode=='dual' and name=='file' else accepted;fault=mode=='fault'
        expected=dict(name=name,state='Stopped',accepted=str(total),written='0' if fault else str(total),failed='1' if fault else '0',abandoned='1' if fault else '0',pending='0',filtered=str((1 if name=='console' else 2) if mode=='dual' else 0),queueFull='4' if repeated else '0',unavailable='0',fault='WriteFailed' if fault else None)
        require(d==expected,'literal final output accounting')
        writer=a['writers'][name];require(writer['maximum']==1,'serialized actual IO')
        order=[] if fault else ['write.1','flush.1','write.2','flush.2','flush.2'] if mode=='dual' and name=='console' else ['flush.0','write.1','flush.1'] if mode=='dual' else [x for i in range(1,total+1) for x in (f'write.{i}',f'flush.{i}')]
        if mode.startswith('flush'):order=['flush.0']+order
        require(writer['order']==order,'fixed target writer order')
        require(writer['attempts']==(1 if fault else total) and writer['flushes']==sum(x.startswith('flush.') for x in order),'actual writer call count')
        wire=records(decoded(writer['bytes']),a['pid']);expected_names=[] if fault else ['coord.1'] if mode=='dual' and name=='file' else [f'coord.{i}' for i in range(total)]
        require([x['event_name'] for x in wire]==expected_names,'original complete records')
        require(all(x['status']==('error' if mode=='dual' and x['event_name']=='coord.1' else 'info') for x in wire),'literal wire severity')
    require((a['writers']['file'] is not None)==both,'disabled writer')
def corruption_controls(cases):
    samples={c['id']:c['report'] for c in cases};controls=[]
    edits=[
        ('retained','write-cancel','detached caller references',lambda a:a.update(retainedAfterDetach=1)),
        ('controls','write-cancel','bounded console controls',lambda a:a.update(controlsPeak=10000)),
        ('storage','write-cancel','bounded continuation storage',lambda a:a.update(continuationSlotsAfterDetach=10000)),
        ('iterations','write-cancel','all repeated callers',lambda a:a.update(iterations=9999)),
        ('shared','write-cancel','coalesced caller targets',lambda a:a.update(shared=False)),
        ('deadline','write-deadline','actual deadline callbacks',lambda a:a.update(deadlineFires=0)),
        ('cancelled','write-cancel','caller cancellation count',lambda a:a.update(cancelled=9999)),
        ('initial','write-cancel','original caller rejection facts',lambda a:a['snapshots'][-2]['console'].update(queueFull='4')),
        ('order','write-cancel','fixed target writer order',lambda a:a['writers']['console']['order'].__setitem__(1,'flush.4')),
        ('file-controls','cohort','bounded file controls',lambda a:a.update(fileControlsPeak=64)),
        ('transition','dual','live caller transition phases',lambda a:a['phases'][1].update(fileAttached=0)),
        ('result','fault','actual caller writer result',lambda a:a.update(succeeded=1)),
        ('pre-cancel','pre-cancel','cancelled completed caller',lambda a:a.update(completedTokenCancelled=False)),
        ('accounting','fault','literal final output accounting',lambda a:a['outputs'][0].update(failed='0')),
        ('wire','pre-cancel','complete record boundary',lambda a:a['writers']['console'].update(bytes=base64.b64encode(decoded(a['writers']['console']['bytes'])[:-1]).decode('ascii'))),
        ('child','pre-cancel','actual child PID',lambda a:a.update(pid=a['pid']+1))
    ]
    for identity,mode,reason,edit in edits:
        bad=copy.deepcopy(samples[mode]);edit(bad)
        try:check(mode,bad,samples[mode]['pid'])
        except AssertionError as error:require(str(error)==reason,'unexpected corruption reason: '+identity);controls.append(dict(id=identity,rejected=True,reason=str(error)))
        else:raise AssertionError('corruption accepted: '+identity)
    return controls
def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'actual native verifier OS')
    host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-flush-coordination-') as temp:
        for mode in MODES:
            work=Path(temp).resolve()/mode;work.mkdir()
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'flush-coordination',mode,str(work)]
            env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout=stderr=b''
            try:
                try:stdout,stderr=process.communicate(timeout=60)
                except BaseException:process.kill();stdout,stderr=process.communicate(timeout=15);raise
            finally:
                (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
            require(process.returncode==0 and stderr==b'','actual child success')
            a=load(stdout);check(mode,a,process.pid)
            require((a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==('.NET 10.0.12',args.family,core,fixture),'exact runtime and assembly bytes')
            cases.append(dict(id=mode,pid=process.pid,command=command,exitCode=process.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
    controls=corruption_controls(cases)
    report=dict(schema='cis.logging.flush-coordination-native/v1',task='WP05-S08-T02',family=args.family,runtime='.NET 10.0.12',children=9,coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(family=args.family,children=9,controls=len(controls),repeatedDetachedCallers=40000,passed=True)))
if __name__=='__main__':main()
