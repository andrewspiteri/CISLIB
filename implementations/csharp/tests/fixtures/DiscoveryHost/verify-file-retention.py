"""Real process restart, bounded exact-group inventory and independently parsed physical records."""
import argparse,copy,hashlib,json,os,re,subprocess,tempfile
from pathlib import Path

MODES=['jsonl-1','jsonl-3','human-1','human-3'];ACTIVE='\u03a9-\U0001f642.log'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def fields(raw,human):
    text=raw.decode('utf-8',errors='strict');require(raw.endswith(b'\n') and text.count('\n')==1 and '\r' not in text and not raw.startswith(b'\xef\xbb\xbf') and 13107<len(raw)<=16384,'complete LF-inclusive record')
    if not human:return json.loads(text)
    result={};decoder=json.JSONDecoder();pos=0;text=text[:-1]
    while pos<len(text):
        while pos<len(text) and text[pos]==' ':pos+=1
        if pos==len(text):break
        end=text.index('=',pos);key=text[pos:end];value,pos=decoder.raw_decode(text,end+1);require(key not in result,'duplicate field');result[key]=value;require(pos==len(text) or text[pos]==' ','human separator')
    return result

def records(raw,human,id,origins):
    require(len(raw)<=65536 and raw.endswith(b'\n'),'segment cap/boundary');lines=raw.splitlines(keepends=True);require(len(lines)==4,'four complete records')
    for offset,line in enumerate(lines):
        value=fields(line,human);entry=(id-1)*4+offset;origin=origins[0 if entry<20 else 1]
        for key,wanted in {'component':'Retention','event_name':'retention.event','message':'x'*7000+'\u03a9\U0001f642','service':'Retention.App','env':'test','version':'1.2.3','timestamp':'2026-10-13T10:11:12.0000000Z','severity' if human else 'status':'info'}.items():require(value[key]==wanted,'literal record fields')
        require(value['attributes']=={'entry_index':entry,'payload':'y'*7000} and 'truncated' not in value,'literal record order/loss')
        resource=value if human else value['resource'];prefix='resource.' if human else ''
        require(resource[prefix+'instance_id']==origin['instanceId'] and resource[prefix+'process_id']==origin['pid'],'actual producer identity')
        if not human:require(value['schema_version']==1,'record schema')

def check(mode,phase,a,previous):
    retained=int(mode[-1]);count=20 if phase=='start' else 12;last=5 if phase=='start' else 8
    require(a['mode']==mode and a['phase']==phase and a['state']=='Stopped','runtime state')
    require(a['output']==dict(name='file',state='Stopped',accepted=count,written=count,failed=0,pending=0,abandoned=0),'file accounting')
    require(a['results']==[dict(outcome='Accepted',truncated=False,sequence=x+1) for x in range(count)],'accepted tickets')
    group=a['inventory']['groupId'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity')
    if previous is None:require(a['before'] is None,'initial inventory')
    else:require(a['before']==previous['inventory'] and group==previous['inventory']['groupId'] and a['instanceId']!=previous['instanceId'],'restart identity')
    segments=[dict(id=id,fileName=ACTIVE if id==last else '.cis-log-'+group+f'-{id:020d}.closed',state='active' if id==last else 'closed') for id in range(last-retained,last+1)]
    wanted=dict(schema=1,groupId=group,format='human' if mode.startswith('human') else 'jsonl',encoding='utf-8',bom=False,newline='lf',segments=segments)
    require(a['inventory']==wanted,'newest bounded segment order');require([x['name'] for x in a['files']]==[x['fileName'] for x in segments],'exact listed physical files')


def neighbors_unchanged(neighbors):
    require(all(path.read_bytes()==raw for path,raw in neighbors.items()),'neighbor preservation')

def membership(directory,inventory,neighbors):
    expected={x['fileName'] for x in inventory['segments']}|{ACTIVE+'.cis.inventory.json',ACTIVE+'.cis.lock'}|{x.name for x in neighbors if x.parent==directory}
    require({x.name for x in directory.iterdir()}==expected,'exact physical retained membership')

def preimages(a,previous):
    if previous is None:return
    old={segment['id']:file['sha256'] for segment,file in zip(previous['inventory']['segments'],previous['files'])}
    for segment,file in zip(a['inventory']['segments'],a['files']):
        if segment['id'] in old:require(file['sha256']==old[segment['id']],'retained physical preimage')

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];controls=[]
    with tempfile.TemporaryDirectory(prefix='cislib-retention-') as temp:
        root=Path(temp).resolve()
        for mode in MODES:
            work=root/mode;directory=work/'logs';directory.mkdir(parents=True);previous=None;origins=[]
            neighbors={directory/'.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed':b'foreign group\n',directory/'other.log':b'another active path\n',work/'archives'/'keep.jsonl':b'archived outside group\n'}
            for path,raw in neighbors.items():path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(raw)
            for phase in ('start','resume'):
                id=mode+'-'+phase;command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-retention',mode,phase,str(work)]
                env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
                try:stdout,stderr=process.communicate(timeout=30)
                except BaseException:process.kill();process.communicate(timeout=15);raise
                require(process.returncode==0 and stderr==b'','child failure');a=json.loads(stdout.decode('utf-8',errors='strict'));check(mode,phase,a,previous)
                require((a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'],a['pid'])==('.NET 10.0.12',args.family,core,fixture,process.pid),'actual process/runtime/assembly');origins.append(a)
                path=directory/ACTIVE;require(a['path']==str(path) and json.loads(Path(str(path)+'.cis.inventory.json').read_bytes())==a['inventory'],'physical captured path/inventory')
                evidence=[]
                for segment,file in zip(a['inventory']['segments'],a['files']):
                    member=directory/segment['fileName'];raw=member.read_bytes();require(file['bytes']==len(raw) and file['sha256']==digest(raw),'physical bytes/hash');records(raw,mode.startswith('human'),segment['id'],origins)
                    name=id+'-segment-'+str(segment['id'])+'.bin';(out/name).write_bytes(raw);evidence.append(dict(path=name,sha256=digest(raw),id=segment['id']))
                neighbors_unchanged(neighbors);membership(directory,a['inventory'],neighbors);preimages(a,previous)
                (out/(id+'.stdout.bin')).write_bytes(stdout);(out/(id+'.stderr.bin')).write_bytes(stderr)
                cases.append(dict(id=id,pid=process.pid,command=command,exitCode=process.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),segmentsEvidence=evidence))
                if phase=='start':
                    unlisted=directory/('.cis-log-'+a['inventory']['groupId']+'-00000000000000000050.closed');unlisted.write_bytes(b'unlisted same-group archive\n');neighbors[unlisted]=unlisted.read_bytes()
                    # Make the oldest identity newest by mtime; identity order must still win.
                    for segment in a['inventory']['segments'][:-1]:os.utime(directory/segment['fileName'],(1900000000-segment['id'],1900000000-segment['id']))
                previous=a
            if mode=='jsonl-3':sample=copy.deepcopy(a);prior=copy.deepcopy(origins[0]);sample_origins=copy.deepcopy(origins);sample_raw=(directory/sample['inventory']['segments'][0]['fileName']).read_bytes();sample_directory=directory;sample_neighbors=dict(neighbors)
        for key in ('bound','id-reset','written','group','record-order','record-pid','record-instance','extra-member','neighbor','retained-preimage'):
            bad=copy.deepcopy(sample);restore=None;extra=None
            try:
                if key=='bound':bad['inventory']['segments'].insert(0,dict(id=1,fileName='extra.closed',state='closed'));check('jsonl-3','resume',bad,prior)
                elif key=='id-reset':bad['inventory']['segments'][-1]['id']=1;check('jsonl-3','resume',bad,prior)
                elif key=='written':bad['output']['written']=0;check('jsonl-3','resume',bad,prior)
                elif key=='group':bad['inventory']['groupId']='eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee';check('jsonl-3','resume',bad,prior)
                elif key=='extra-member':extra=sample_directory/'unexpected.closed';extra.write_bytes(b'extra');membership(sample_directory,sample['inventory'],sample_neighbors)
                elif key=='neighbor':
                    path=next(iter(sample_neighbors));restore=(path,sample_neighbors[path]);path.write_bytes(b'changed neighbor');neighbors_unchanged(sample_neighbors)
                elif key=='retained-preimage':
                    changed=copy.deepcopy(prior);changed['files'][-1]['sha256']='0'*64;preimages(sample,changed)
                else:
                    lines=sample_raw.splitlines(keepends=True);value=json.loads(lines[0])
                    if key=='record-order':value['attributes']['entry_index']=99
                    elif key=='record-pid':value['resource']['process_id']=-1
                    else:value['resource']['instance_id']='eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
                    lines[0]=(json.dumps(value,separators=(',',':'),ensure_ascii=False)+'\n').encode('utf-8');records(b''.join(lines),False,5,sample_origins)
            except AssertionError as error:controls.append(dict(id=key,rejected=True,reason=str(error)))
            else:raise AssertionError('corruption accepted: '+key)
            finally:
                if extra is not None:extra.unlink()
                if restore is not None:restore[0].write_bytes(restore[1])
    report=dict(schema='cis.logging.file-retention-native/v1',task='WP05-S06-T02',family=args.family,runtime='.NET 10.0.12',children=8,coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=8,controls=10,passed=True)))
if __name__=='__main__':main()
