"""Real runtime ownership/path/format/permission consumers. ACL changes stay inside fresh owned test paths."""
import argparse, base64, copy, hashlib, json, os, queue, re, stat, subprocess, tempfile, threading
from pathlib import Path

LIMIT=20
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,message):
    if not value:raise AssertionError(message)

def records(raw,human,name,expected):
    text=raw.decode('utf-8',errors='strict');require(raw.endswith(b'\n') and not raw.startswith(b'\xef\xbb\xbf') and text.count('\n')==len(expected),'group framing')
    decoder=json.JSONDecoder()
    for line,(event,pid) in zip(text.splitlines(),expected):
        require(len((line+'\n').encode('utf-8'))<=16384 and '\r' not in line and '\x1b' not in line,'group cap/control')
        if human:
            value={};pos=0
            while pos<len(line):
                while pos<len(line) and line[pos]==' ':pos+=1
                if pos==len(line):break
                equal=line.index('=',pos);key=line[pos:equal];pos=equal+1
                while line[pos]==' ':pos+=1
                field,end=decoder.raw_decode(line,pos);require(key not in value,'duplicate field');value[key]=field;pos=end;require(pos==len(line) or line[pos]==' ','field separator')
        else:value=json.loads(line)
        for key,wanted in {'event_name':event,'message':name,'component':'Group','severity' if human else 'status':'info','service':'Console.App','env':'test','version':'1.2.3','timestamp':'2026-10-13T10:11:12.0000000Z'}.items():require(value[key]==wanted,'record identity')
        require(value['attributes']['process_id']==pid and type(value['attributes']['process_id']) is int and 'truncated' not in value,'process route/loss')
        if not human:require(value['schema_version']==1 and value['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','protected facts')

def check(a,status,path,count,code=None):
    require(a['status']==status and a['capturedPath']==str(path),'status/path')
    require(a['workers']==(0 if status=='prepared' else 2) and a['borrowedDisposes']==0 and a['factories']==count,'workers/borrowed/factory')
    joined=status in ('prepared','failed','released','completed');require(a['joined']==joined,'worker join')
    wanted=None if code is None else dict(output='file',code=code,message=f"Logging output 'file' could not initialize ({code}).",inner=False,dataCount=0)
    require(a['error']==wanted,'safe error')
    if code in ('FileInventoryMissing','FileInventoryIncompatible','FileInventoryInitializationFailed'):require(a['leaseReleased'] is True and a['activeReleased'] is True,'in-process owned handle release')
    if status in ('prepared','failed'):
        require(a['outputs'] is None and a['state'] is None and a['consoleBytes']==a['fileBytes']=='','unpublished output');return
    state='Stopped' if joined else 'Running'
    require(a['state']==state and a['outputs']==[dict(name=name,state=state,accepted=count,written=count,failed=0,abandoned=0,pending=0) for name in ('console','file')],'dispositions')
    records(base64.b64decode(a['consoleBytes'],validate=True),False,path.name,[('group.initial',a['pid'])]+[('group.after.contender',a['pid'])]*(count-1))
    require(digest(Path(str(path)+'.cis.inventory.json').read_bytes())==a['inventorySha256'],'inventory bytes')
    require(path.read_bytes()==base64.b64decode(a['fileBytes'],validate=True),'actual file bytes')

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);verifier=sha(__file__);host=Path(args.host).resolve();host_hash=sha(host);core_hash=sha(host.parent/'CIS.Core.dll')
    env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);base=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host)]
    cases=[];permission=[]
    def observations(a,pid):require((a['pid'],a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==(pid,'.NET 10.0.12',args.family,core_hash,host_hash),'actual process/runtime/assembly')
    class Child:
        def __init__(self,id,mode,path,cwd):
            self.id=id;self.command=base+['file-runtime-group',mode,str(path)];self.process=subprocess.Popen(self.command,cwd=cwd,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE);self.stdout=b'';self.reports=[];self.ended=False;self.lines=queue.Queue();self.reader=threading.Thread(target=self.drain,daemon=True);self.reader.start();self.stderr=b'';self.error_reader=threading.Thread(target=self.drain_error,daemon=True);self.error_reader.start()
        def drain(self):
            for raw in iter(self.process.stdout.readline,b''):self.lines.put(raw)
            self.lines.put(b'')
        def drain_error(self):
            self.stderr=self.process.stderr.read()
        def join_readers(self):
            self.reader.join(timeout=LIMIT);self.error_reader.join(timeout=LIMIT);require(not self.reader.is_alive() and not self.error_reader.is_alive(),'process readers did not join')
        def read(self):
            raw=self.lines.get(timeout=LIMIT);require(raw!=b'','missing child report');self.stdout+=raw;a=json.loads(raw.decode('utf-8',errors='strict'));observations(a,self.process.pid);self.reports.append(a);return a
        def command_line(self,text):self.process.stdin.write((text+'\n').encode('utf-8'));self.process.stdin.flush();return self.read()
        def finish(self,killed=False):
            if killed:self.process.kill()
            self.process.stdin.close();self.process.wait(timeout=LIMIT);self.join_readers();stderr=self.stderr;require(self.lines.get(timeout=LIMIT)==b'' and stderr==b'','unexpected process bytes');require(killed or self.process.returncode==0,'child failure')
            (out/(self.id+'.stdout.bin')).write_bytes(self.stdout);(out/(self.id+'.stderr.bin')).write_bytes(stderr);cases.append(dict(id=self.id,pid=self.process.pid,command=self.command,exitCode=self.process.returncode,killed=killed,reports=self.reports,stdoutSha256=digest(self.stdout),stderrSha256=digest(stderr)));self.ended=True
        def close(self):
            if not self.ended:
                if self.process.poll() is None:self.process.kill()
                self.process.wait(timeout=LIMIT);self.join_readers()
    def probe(id,mode,path,cwd,status='completed',code=None):
        child=Child(id,mode,path,cwd)
        try:a=child.read();check(a,status,Path(path).resolve(),0 if code else 1,code);child.finish();return a
        finally:child.close()
    def inventory(path,format):
        raw=Path(str(path)+'.cis.inventory.json').read_bytes();d=json.loads(raw.decode('utf-8',errors='strict'));require(not raw.startswith(b'\xef\xbb\xbf') and d['schema']==1 and re.fullmatch('[0-9a-f]{32}',d['groupId']) and d['groupId']!='0'*32,'inventory identity')
        require(d['format']==format and (d['encoding'],d['bom'],d['newline'])==('utf-8',False,'lf'),'inventory format/encoding')
        require(d['segments']==[dict(id=1,fileName=path.name,state='active')],'active segment');return raw
    with tempfile.TemporaryDirectory(prefix='cislib-runtime-groups-') as temp:
        root=Path(temp).resolve()
        for format in ('jsonl','human'):
            parent=root/format;parent.mkdir();a=parent/'A.jsonl';b=parent/'B.jsonl';first=Child(format+'-holder-a','hold-'+format,a,parent);second=None
            try:
                check(first.read(),'ready',a,1);before=a.read_bytes();meta=inventory(a,format)
                probe(format+'-blocked','probe-'+format,a,parent,'failed','FileOwnershipUnavailable');require(a.read_bytes()==before and inventory(a,format)==meta,'contender preservation')
                alias=str(a.parent)+os.sep+'.'+os.sep+a.name;probe(format+'-alias-blocked','probe-'+format,alias,parent,'failed','FileOwnershipUnavailable')
                second=Child(format+'-holder-b','hold-'+format,b,parent);check(second.read(),'ready',b,1);require(json.loads(inventory(b,format))['groupId']!=json.loads(meta)['groupId'],'distinct identity')
                check(first.command_line('append'),'appended',a,2);records(b.read_bytes(),format=='human','B.jsonl',[('group.initial',second.process.pid)])
                check(second.command_line('append'),'appended',b,2);records(a.read_bytes(),format=='human','A.jsonl',[('group.initial',first.process.pid),('group.after.contender',first.process.pid)]);records(b.read_bytes(),format=='human','B.jsonl',[('group.initial',second.process.pid),('group.after.contender',second.process.pid)])
                check(first.command_line('release'),'released',a,2);first.finish();check(second.command_line('release'),'released',b,2);second.finish()
                restart=probe(format+'-restart','probe-'+format,a,parent);records(a.read_bytes(),format=='human','A.jsonl',[('group.initial',first.process.pid),('group.after.contender',first.process.pid),('group.initial',restart['pid'])]);require(inventory(a,format)==meta,'restart identity')
            finally:
                first.close()
                if second:second.close()
        path=root/'killed.jsonl';child=Child('killed-owner','hold-jsonl',path,root)
        try:check(child.read(),'ready',path,1);meta=inventory(path,'jsonl');child.finish(killed=True)
        finally:child.close()
        successor=probe('killed-restart','probe-jsonl',path,root);records(path.read_bytes(),False,path.name,[('group.initial',child.process.pid),('group.initial',successor['pid'])]);require(inventory(path,'jsonl')==meta,'killed restart identity')
        origin=root/'origin';later=root/'later';origin.mkdir();later.mkdir();relative=Path('missing')/'\u03a9-\U0001f642-e\u0301.jsonl';path=origin/relative;child=Child('relative-unicode-capture','capture-jsonl',relative,origin)
        try:
            check(child.read(),'prepared',path,0);require(not path.parent.exists() and not list(later.iterdir()),'prepare performed I/O');a=child.command_line('start:'+str(later));check(a,'completed',path,1);require(a['cwd']==str(later) and not list(later.iterdir()),'captured cwd/fallback');child.finish();records(path.read_bytes(),False,path.name,[('group.initial',child.process.pid)]);inventory(path,'jsonl')
        finally:child.close()
        for format in ('jsonl','human'):
            path=root/('switch-'+format+'.log');probe(format+'-format-seed','probe-'+format,path,root);before=path.read_bytes();meta=inventory(path,format)
            probe(format+'-format-rejected','probe-'+('human' if format=='jsonl' else 'jsonl'),path,root,'failed','FileInventoryIncompatible');require(path.read_bytes()==before and inventory(path,format)==meta,'format preservation')
            path=root/('missing-'+format+'.log');before=b'{"prior":true}\n';path.write_bytes(before)
            for attempt in range(2):probe(format+'-missing-'+str(attempt),'probe-'+format,path,root,'failed','FileInventoryMissing');require(path.read_bytes()==before and not Path(str(path)+'.cis.inventory.json').exists(),'missing inventory preservation')
        for format in ('jsonl','human'):
            path=root/('empty-'+format+'.log');path.write_bytes(b'');doc=dict(schema=1,groupId='2'*32,format=format,encoding='utf-8',bom=False,newline='lf',segments=[dict(id=1,fileName=path.name,state='active')]);metadata=Path(str(path)+'.cis.inventory.json');raw=json.dumps(doc,separators=(',',':')).encode('utf-8');metadata.write_bytes(raw)
            probe(format+'-empty-switch-rejected','probe-'+('human' if format=='jsonl' else 'jsonl'),path,root,'failed','FileInventoryIncompatible');require(path.read_bytes()==b'' and metadata.read_bytes()==raw,'empty group format changed')
            a=probe(format+'-empty-matching','probe-'+format,path,root);records(path.read_bytes(),format=='human',path.name,[('group.initial',a['pid'])]);require(metadata.read_bytes()==raw,'compatible empty inventory rewritten')
        # Actual denied OS operations are a prerequisite for permission qualification.
        sid=None
        if args.family=='windows':
            identity=subprocess.run(['whoami','/user','/fo','csv','/nh'],capture_output=True,check=True);sid=re.search(r'S-1-[0-9-]+',identity.stdout.decode('ascii',errors='ignore')).group()
        else:require(os.geteuid()!=0,'permission lane must run as an unprivileged Unix user')
        for kind in ('directory','active','inventory','lease'):
            parent=root/('denied-'+kind);parent.mkdir();path=(parent/'missing'/'active.jsonl') if kind=='directory' else parent/'active.jsonl';keep=parent/'keep.txt';keep.write_bytes(b'keep')
            if kind!='directory':probe('permission-'+kind+'-seed','probe-jsonl',path,root)
            target=parent if kind=='directory' else Path(str(path)+'.cis.inventory.json') if kind=='inventory' else Path(str(path)+'.cis.lock') if kind=='lease' else path
            require(target.resolve().is_relative_to(root) and not target.is_symlink(),'permission target escaped owned root')
            before={p:p.read_bytes() for p in parent.iterdir() if p.is_file()};original_mode=stat.S_IMODE(target.stat().st_mode);rights='RD' if kind=='inventory' else 'W';denied=False;restore=None
            try:
                if args.family=='windows':
                    mutation=subprocess.run(['icacls',str(target),'/deny','*'+sid+':('+rights+')'],capture_output=True,check=True);restore=['icacls',str(target),'/remove:d','*'+sid]
                else:os.chmod(target,0 if kind=='inventory' else 0o500 if kind=='directory' else 0o400)
                try:
                    if kind=='directory':
                        os.mkdir(target/'missing')
                    else:
                        with open(target,'rb' if kind=='inventory' else 'r+b'):pass
                except PermissionError:denied=True
                require(denied,'permission control did not deny the actual OS operation')
                code='FileInventoryInitializationFailed' if kind=='inventory' else 'FileInitializationFailed'
                a=probe('permission-'+kind+'-denied','probe-jsonl',path,root,'failed',code)
                if kind=='active':require(a['leaseReleased'] is True,'active denial retained its lease before exit')
            finally:
                if args.family=='windows' and restore:subprocess.run(restore,capture_output=True,check=True)
                elif args.family!='windows':os.chmod(target,original_mode)
            require(all(p.read_bytes()==raw for p,raw in before.items()),'permission failure changed existing files')
            permission.append(dict(id=kind,actualOperationDenied=denied,subject='current-windows-user' if sid else 'unprivileged-linux-user',preimages={p.name:digest(raw) for p,raw in before.items()},restored=True))
            probe('permission-'+kind+'-restored','probe-jsonl',path,root);require(keep.read_bytes()==b'keep','permission collision modified unrelated file')
        sample=next(x for x in cases if x['id']=='jsonl-blocked')['reports'][0];controls=[]
        for key in ('joined','borrowedDisposes','error','capturedPath','factories'):
            bad=copy.deepcopy(sample)
            if key=='error':bad['error']['message']='private-path'
            elif key=='capturedPath':bad[key]='wrong-path'
            elif key=='joined':bad[key]=False
            else:bad[key]=1
            try:check(bad,'failed',Path(sample['capturedPath']),0,'FileOwnershipUnavailable')
            except AssertionError as e:controls.append(dict(id=key,rejected=True,reason=str(e)))
            else:raise AssertionError('corruption accepted: '+key)
        released=copy.deepcopy(next(x for x in cases if x['id']=='jsonl-format-rejected')['reports'][0]);released['leaseReleased']=False
        try:check(released,'failed',Path(released['capturedPath']),0,'FileInventoryIncompatible')
        except AssertionError as e:require(str(e)=='in-process owned handle release','release corruption failed elsewhere');controls.append(dict(id='leaseReleased',rejected=True,reason=str(e)))
        else:raise AssertionError('release corruption accepted')
    report=dict(schema='cis.logging.file-group-consumers-native/v1',task='WP05-S05-T04',family=args.family,runtime='.NET 10.0.12',fixtureSha256=host_hash,coreSha256=core_hash,verifierSha256=verifier,children=len(cases),cases=cases,permissions=permission,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=len(cases),permissionCases=len(permission),controls=len(controls),passed=True)))

if __name__=='__main__':main()
