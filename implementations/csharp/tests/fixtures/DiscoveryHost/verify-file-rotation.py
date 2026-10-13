"""Exact-runtime physical rotation; independent byte arithmetic and original process bytes."""
import argparse,base64,copy,hashlib,json,os,re,subprocess,tempfile
from pathlib import Path

MODES=['jsonl-exact','jsonl-over','human-exact','human-over']
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def bytesof(a,key):return base64.b64decode(a[key],validate=True)
def record(raw,human):
    text=raw.decode('utf-8',errors='strict');require(raw.endswith(b'\n') and text.count('\n')==1 and not raw.startswith(b'\xef\xbb\xbf') and '\r' not in text and len(raw)<=16384,'complete bounded record')
    if human:
        d={};decoder=json.JSONDecoder();pos=0;text=text[:-1]
        while pos<len(text):
            while pos<len(text) and text[pos]==' ':pos+=1
            if pos==len(text):break
            end=text.index('=',pos);key=text[pos:end];pos=end+1
            while text[pos]==' ':pos+=1
            value,pos=decoder.raw_decode(text,pos);require(key not in d,'duplicate human field');d[key]=value;require(pos==len(text) or text[pos]==' ','human separator')
    else:d=json.loads(text)
    for key,wanted in {'event_name':'rotation.event','message':'\u03a9 \U0001f642 e\u0301','component':'Rotation','severity' if human else 'status':'info','service':'Console.App','env':'test','version':'1.2.3','timestamp':'2026-10-13T10:11:12.0000000Z'}.items():require(d[key]==wanted,'literal record identity')
    require('truncated' not in d,'unexpected loss')
    if not human:require(d['schema_version']==1 and d['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','protected record facts')
def check(mode,a):
    human=mode.startswith('human');over=mode.endswith('over');require(a['mode']==mode and a['result']=='Accepted' and a['truncated'] is False and a['state']=='Stopped' and a['borrowedDisposes']==0,'runtime facts')
    require(a['tickets']==[dict(name=n,status='Accepted',sequence=1) for n in ('console','file')],'admission tickets')
    require(a['outputs']==[dict(name=n,state='Stopped',accepted=1,written=1,failed=0,pending=0,abandoned=0) for n in ('console','file')],'write dispositions')
    line=bytesof(a,'recordBytes');seed=bytesof(a,'seedBytes');active=bytesof(a,'activeBytes');record(line,human);record(bytesof(a,'consoleBytes'),False)
    require(len(seed)+len(line)==65536+int(over),'literal LF-inclusive boundary')
    prefix=b'seed="' if human else b'{"seed":"';suffix=b'"\n' if human else b'"}\n';require(seed==prefix+b'a'*(len(seed)-len(prefix)-len(suffix))+suffix,'literal existing record')
    old=a['oldInventory'];new=a['inventory'];group=old['groupId'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity')
    name='\u03a9-\U0001f642.log';head=dict(schema=1,groupId=group,format='human' if human else 'jsonl',encoding='utf-8',bom=False,newline='lf')
    require(old==dict(head,segments=[dict(id=1,fileName=name,state='active')]),'old inventory')
    closed='.cis-log-'+group+'-00000000000000000001.closed'
    segments=[dict(id=1,fileName=closed,state='closed'),dict(id=2,fileName=name,state='active')] if over else old['segments']
    require(new==dict(head,segments=segments),'rotation identity/order')
    require(active==(line if over else seed+line) and len(active)<=65536,'whole active bytes')
    require(a['closed']==([dict(name=closed,bytes=a['seedBytes'])] if over else []),'whole closed bytes')
def main():
    parser=argparse.ArgumentParser()
    for k in ('dotnet','host','output','family'):parser.add_argument('--'+k,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native verifier OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];controls=[]
    with tempfile.TemporaryDirectory(prefix='cislib-rotation-') as temp:
        root=Path(temp).resolve()
        for mode in MODES:
            work=root/mode;work.mkdir();command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-rotation',mode,str(work)]
            env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=30)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            require(process.returncode==0 and stderr==b'','child failure');a=json.loads(stdout.decode('utf-8',errors='strict'));check(mode,a)
            require((a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==('.NET 10.0.12',args.family,core,fixture),'actual runtime/assembly')
            path=work/'logs'/'\u03a9-\U0001f642.log';require(a['path']==str(path) and path.read_bytes()==bytesof(a,'activeBytes'),'physical captured active path/bytes');require((work/'reference.log').read_bytes()==bytesof(a,'recordBytes'),'physical prepared record')
            require(json.loads(Path(str(path)+'.cis.inventory.json').read_bytes())==a['inventory'],'physical inventory');closed=list(path.parent.glob('*.closed'));require(len(closed)==len(a['closed']),'physical closed count')
            for item in a['closed']:require((path.parent/item['name']).read_bytes()==base64.b64decode(item['bytes'],validate=True),'physical closed bytes')
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);cases.append(dict(id=mode,pid=process.pid,command=command,exitCode=process.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
        sample=copy.deepcopy(cases[1]['report'])
        for key in ('activeBytes','inventory','outputs','borrowedDisposes','recordBytes'):
            bad=copy.deepcopy(sample)
            if key=='activeBytes':bad[key]=base64.b64encode(bytesof(bad,'seedBytes')+bytesof(bad,'recordBytes')).decode('ascii')
            elif key=='inventory':bad[key]['segments'][-1]['id']=1
            elif key=='outputs':bad[key][1]['written']=0
            elif key=='borrowedDisposes':bad[key]=1
            else:bad[key]=base64.b64encode(bytesof(bad,key)[:-1]).decode('ascii')
            try:check('jsonl-over',bad)
            except AssertionError as error:controls.append(dict(id=key,rejected=True,reason=str(error)))
            else:raise AssertionError('corruption accepted: '+key)
    report=dict(schema='cis.logging.file-rotation-native/v1',task='WP05-S06-T01',family=args.family,runtime='.NET 10.0.12',children=4,coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=4,controls=5,passed=True)))
if __name__=='__main__':main()
