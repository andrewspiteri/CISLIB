"""Independent native observations of registered Core flush targets; no public deadline API."""
import argparse,base64,copy,hashlib,json,os,re,subprocess,tempfile
from pathlib import Path

MODES=['empty','both','saturated','faulted','closed','concurrent','physical']
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
def count(value):
    require(type(value)==str and re.fullmatch(r'0|[1-9][0-9]*',value) and int(value)<=9223372036854775807,'counter encoding')
    return int(value)
def output(target,filtered=0,full=0,unavailable=0):
    return dict(boundary=str(target),filtered=str(filtered),queueFull=str(full),unavailable=str(unavailable))
def records(raw,pid):
    require(not raw or raw.endswith(b'\n'),'complete record boundary')
    result=[]
    for line in raw.splitlines(keepends=True):
        require(line.endswith(b'\n') and b'\r' not in line and not line.startswith(b'\xef\xbb\xbf') and len(line)<=16384,'complete bounded record')
        d=load(line)
        for key,value in dict(schema_version=1,timestamp='2026-10-13T15:16:17.0000000Z',service='Console.App',env='test',version='1.2.3',component='FlushBoundary').items():
            require(d[key]==value,'wire identity')
        require(d['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','wire resource')
        require(d['attributes']==dict(producer_pid=pid),'wire producer')
        if d.get('truncated'):
            require(d['truncated'] is True and d['event_name'] in ('flush.2','flush.4') and d['message']=='x'*8192,'actual truncation')
        else:require(d['message']=='\u03a9 \U0001f642' and 'truncated' not in d,'wire message')
        result.append(d)
    return result
def check(mode,a,pid=None):
    require(a['mode']==mode,'mode')
    if pid is not None:require(a['pid']==pid,'actual child PID')
    require(a['captures']==a['after'],'snapshot serialization')
    captures=a['captures'];require(len(captures)==(34 if mode=='concurrent' else 2 if mode in ('saturated','faulted') else 1),'capture count')
    require(len(a['flushResults'])==len(captures),'result count')
    enabled=['file'] if mode=='physical' else ['console','file'] if mode in ('both','saturated','concurrent') else ['console']
    for index,c in enumerate(captures):
        require(set(c)=={'capturedAtUtc','invalid','truncated','console','file'},'capture shape')
        require(re.fullmatch(r'[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}\+00:00',c['capturedAtUtc']),'UTC capture')
        count(c['invalid']);count(c['truncated'])
        for name in ('console','file'):
            if name not in enabled:require(c[name] is None and a['flushResults'][index][name] is None,'disabled output')
            else:
                require(set(c[name])=={'boundary','filtered','queueFull','unavailable'},'output shape')
                for value in c[name].values():count(value)
                require(a['flushResults'][index][name] is (mode not in ('faulted','closed')),'writer flush result')
    first=captures[0]
    if mode=='concurrent':
        targets={count(c['console']['boundary']) for c in captures}
        for c in captures:
            target=count(c['console']['boundary'])
            require(1<=target<=65 and c['console']==c['file']==output(target),'atomic output targets')
            require(0<=count(c['invalid'])<=16 and c['truncated']=='0','initial global facts')
        require(first['console']==output(1) and first['invalid']=='0','seed target')
        require(captures[-1]['console']==output(65) and captures[-1]['invalid']=='16','complete cohort target')
        require(a['heldControls']==dict(console=len(targets),file=len(targets)),'bounded controls')
    else:
        targets=None
        expected={
            'empty':(0,0,output(0),None),'both':(1,1,output(3,1),output(1,3)),
            'saturated':(1,1,output(2,1,2),output(2,1,2)),
            'faulted':(0,0,output(2),None),'closed':(0,0,output(1),None),
            'physical':(0,0,None,output(2))
        }[mode]
        require((first['invalid'],first['truncated'])==tuple(str(x) for x in expected[:2]),'initial global facts')
        require((first['console'],first['file'])==expected[2:],'initial output facts')
        if mode=='saturated':
            require(captures[1]['console']==captures[1]['file']==output(2,1,2) and captures[1]['invalid']=='2' and captures[1]['truncated']=='1','second saturated facts')
            require(a['heldControls']==dict(console=1,file=1),'bounded controls')
        if mode=='faulted':require(captures[1]['console']==output(2,unavailable=1) and captures[1]['invalid']==captures[1]['truncated']=='0','fault initial facts')
    require(a['finalControls']==dict(console=0,file=0),'released controls')
    if mode in ('saturated','concurrent'):require(a['coalesced'] is True,'same target coalescing')
    final=a['final'];require(final['state']=='Stopped','terminal runtime')
    global_expected={'empty':(0,0),'both':(2,2),'saturated':(3,1),'faulted':(0,0),'closed':(0,0),'concurrent':(16,0),'physical':(0,0)}[mode]
    require((final['invalid'],final['truncated'])==tuple(str(x) for x in global_expected),'final global facts')
    require([d['name'] for d in final['outputs']]==enabled,'enabled outputs')
    for d in final['outputs']:
        name=d['name'];accepted={'empty':0,'both':5 if name=='console' else 3,'saturated':3,'faulted':2,'closed':1,'concurrent':65,'physical':2}[mode]
        failed=1 if mode=='faulted' else 0;abandoned=failed;written=accepted-failed-abandoned
        expected=dict(name=name,state='Stopped',accepted=str(accepted),written=str(written),failed=str(failed),abandoned=str(abandoned),pending='0',unavailable='1' if failed else '0',queueFull='2' if mode=='saturated' else '0',filtered=str((2 if name=='console' else 4) if mode=='both' else 1 if mode=='saturated' else 0),fault='WriteFailed' if failed else None)
        require(d==expected,'terminal output facts')
        if mode=='physical':
            wire=records(decoded(a['bytes']),a['pid']);require(a['leaseReleased'] is True,'released physical lease')
        else:
            require(a['physicalFiles']==0,'controlled streams only')
            writer=a['writers'][name];require(writer['maximum']==(0 if mode=='empty' and not writer['order'] else 1),'serialized IO')
            order=writer['order']
            if mode=='concurrent':
                written_so_far=0;flushed=set()
                for operation in order:
                    require(re.fullmatch(r'(write|flush)\.[1-9][0-9]*',operation),'operation shape')
                    action,sequence=operation.split('.');sequence=int(sequence)
                    if action=='write':
                        written_so_far+=1;require(sequence==written_so_far,'contiguous writes')
                    else:
                        require(sequence==written_so_far and sequence not in flushed,'fixed target order');flushed.add(sequence)
                require(written_so_far==65 and flushed==targets,'covered targets')
            else:
                expected_order={
                    'empty':['flush.0'],'closed':['write.1'],'faulted':[],
                    'saturated':['write.1','write.2','flush.2','write.3'],
                    'both':['write.1','write.2','write.3','flush.3','write.4','write.5'] if name=='console' else ['write.1','flush.1','write.2','write.3']
                }[mode]
                require(order==expected_order,'fixed target order')
            require(writer['attempts']==(1 if mode=='faulted' else written),'actual write attempts')
            wire=records(decoded(writer['bytes']),a['pid'])
        if mode=='concurrent':
            names={d['event_name'] for d in wire};require(len(wire)==65 and names=={'flush.0'}|{f'flush.producer.{p}.{i}' for p in range(16) for i in range(4)},'actual producer cohort')
            require(all(d['status']=='warn' for d in wire),'wire severity')
        else:
            names={'empty':[],'both':['flush.0','flush.1','flush.2','flush.3','flush.4'] if name=='console' else ['flush.1','flush.3','flush.4'],'saturated':['flush.0','flush.1','flush.3'],'faulted':[],'closed':['flush.0'],'physical':['flush.0','flush.1']}[mode]
            require([d['event_name'] for d in wire]==names,'actual record selection')
            for d in wire:
                require(d['status']==('error' if mode=='both' and d['event_name'] in ('flush.1','flush.3','flush.4') else 'warn' if mode=='saturated' else 'info'),'wire severity')
        require(len(wire)==written,'whole write bytes')
    if mode!='physical':
        require((a['writers']['file'] is not None)==('file' in enabled),'disabled writer')
        expected_results={
            'empty':[],'closed':['Accepted'],'faulted':['Accepted','Accepted','Rejected'],
            'both':['Accepted','Accepted','Filtered','InvalidEvent','Accepted','Accepted','InvalidEvent','Filtered','Accepted'],
            'saturated':['Accepted','Accepted','Rejected','InvalidEvent','Rejected','Filtered','InvalidEvent','Accepted','InvalidEvent'],
            'concurrent':['Accepted']
        }[mode]
        require(a['writeResults']==expected_results,'admission outcomes')
def corruption_controls(cases):
    samples={c['id']:c['report'] for c in cases};controls=[]
    edits=[
        ('serialization','both','snapshot serialization',lambda a:a['after'][0].update(invalid='99')),
        ('global','both','initial global facts',lambda a:[a[k][0].update(invalid='0') for k in ('captures','after')]),
        ('boundary','both','initial output facts',lambda a:[a[k][0]['console'].update(boundary='4') for k in ('captures','after')]),
        ('rejection','both','initial output facts',lambda a:[a[k][0]['file'].update(filtered='2') for k in ('captures','after')]),
        ('order','both','fixed target order',lambda a:a['writers']['console']['order'].__setitem__(3,'flush.4')),
        ('controls','saturated','bounded controls',lambda a:a['heldControls'].update(console=2)),
        ('coalescing','saturated','same target coalescing',lambda a:a.update(coalesced=False)),
        ('terminal','both','terminal output facts',lambda a:a['final']['outputs'][0].update(pending='1')),
        ('flush-result','both','writer flush result',lambda a:a['flushResults'][0].update(console=False)),
        ('wire','closed','complete record boundary',lambda a:a['writers']['console'].update(bytes=base64.b64encode(decoded(a['writers']['console']['bytes'])[:-1]).decode('ascii'))),
        ('child-pid','empty','actual child PID',lambda a:a.update(pid=a['pid']+1)),
        ('cleanup','closed','released controls',lambda a:a['finalControls'].update(console=1))
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
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native verifier OS')
    host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-flush-boundary-') as temp:
        for mode in MODES:
            work=Path(temp).resolve()/mode;work.mkdir()
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'flush-boundary',mode,str(work)]
            env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            stdout=stderr=b''
            try:
                try:stdout,stderr=process.communicate(timeout=30)
                except BaseException:
                    process.kill();stdout,stderr=process.communicate(timeout=15);raise
            finally:
                (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
            require(process.returncode==0 and stderr==b'','child failure')
            a=load(stdout);check(mode,a,process.pid)
            require((a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==('.NET 10.0.12',args.family,core,fixture),'actual runtime/assembly')
            case=dict(id=mode,pid=process.pid,command=command,exitCode=process.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr))
            if mode=='physical':
                path=work/'logs'/'\u03a9-\U0001f642.jsonl';require(a['path']==str(path) and path.read_bytes()==decoded(a['bytes']),'physical bytes')
                inventory=load(Path(str(path)+'.cis.inventory.json').read_bytes())
                require(re.fullmatch('[0-9a-f]{32}',inventory['groupId']) and inventory['groupId']!='0'*32,'group identity')
                require(inventory==dict(schema=1,groupId=inventory['groupId'],format='jsonl',encoding='utf-8',bom=False,newline='lf',segments=[dict(id=1,fileName=path.name,state='active')]),'physical inventory')
                require(set(p.name for p in path.parent.iterdir())=={path.name,path.name+'.cis.lock',path.name+'.cis.inventory.json'},'physical membership')
                (out/'physical.active.bin').write_bytes(path.read_bytes());case['inventory']=inventory;case['activeSha256']=sha(path)
            cases.append(case)
    controls=corruption_controls(cases)
    report=dict(schema='cis.logging.flush-boundary-native/v1',task='WP05-S08-T01',family=args.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(family=args.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
