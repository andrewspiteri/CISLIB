"""Separate held producers, forced owned exits, restarted runtimes and a real offline collector selector."""
import argparse,base64,copy,hashlib,importlib.util,json,os,queue,re,subprocess,sys,tempfile,threading
from pathlib import Path
MODES=['intact','partial-json','partial-utf8']+[kind+'-'+stage for kind in ('rotation','recovery') for stage in ('intent','renamed','published','created')]+['collision-file','collision-directory','unproven-gap','ambiguous-both']
ACTIVE='logs/\u03a9-\U0001f642.jsonl'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
decoded=lambda b:base64.b64decode(b,validate=True)
def require(value,reason):
    if not value:raise AssertionError(reason)
def record(raw,name,pid):
    require(raw.endswith(b'\n') and raw.count(b'\n')==1 and b'\r' not in raw and len(raw)<=16384 and not raw.startswith(b'\xef\xbb\xbf'),'record framing')
    a=json.loads(raw)
    for key,value in dict(schema_version=1,status='info',component='Restart',event_name=name,message='\u03a9 \U0001f642',service='Console.App',env='test',version='1.2.3',timestamp='2026-10-13T10:11:12.0000000Z',attributes=dict(producer_pid=pid)).items():require(a[key]==value,'actual record producer')
    require(a['resource']['instance_id']=='11111111-1111-1111-1111-111111111111' and 'truncated' not in a,'protected record')
def check(mode,seed,resume,probe=None):
    require(seed['mode']==resume['mode']==mode and seed['phase']=='hold' and resume['phase']=='resume' and seed['composition']==resume['composition']=='actual-core-seed-with-staged-physical-restart','composition')
    require(seed['pid']!=resume['pid'] and seed['seedWritten']==2 and seed['leaseHeld'] and resume['leaseReleased'],'owned process/lease')
    group=seed['group'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'group identity')
    records=[decoded(x) for x in seed['seedRecords']];require(len(records)==2,'seed count')
    for index,raw in enumerate(records):record(raw,'restart.seed.'+str(index),seed['pid'])
    rotation=mode.startswith('rotation') or mode in ('unproven-gap','ambiguous-both');recovery=mode.startswith('recovery');staged=rotation or recovery;partial=mode.startswith('partial')
    failure=mode.startswith('collision') or mode in ('unproven-gap','ambiguous-both')
    name=lambda identity,state:'.cis-log-'+group+'-'+str(identity).zfill(20)+'.'+state
    meta=ACTIVE+'.cis.inventory.json';pending=ACTIVE+'.cis.transition.json'
    head=dict(schema=1,groupId=group,format='jsonl',encoding='utf-8',bom=False,newline='lf')
    old=[dict(id=1,fileName=name(1,'damaged'),state='damaged'),dict(id=2,fileName=name(2,'closed'),state='closed'),dict(id=3,fileName=ACTIVE[5:],state='active')] if staged else [dict(id=1,fileName=ACTIVE[5:],state='active')]
    new=([old[1],dict(id=3,fileName=name(3,'closed'),state='closed'),dict(id=4,fileName=ACTIVE[5:],state='active')] if rotation else [*old[:-1],dict(id=3,fileName=name(3,'damaged'),state='damaged'),dict(id=4,fileName=ACTIVE[5:],state='active')]) if staged else ([dict(id=1,fileName=name(1,'damaged'),state='damaged'),dict(id=2,fileName=ACTIVE[5:],state='active')] if partial else old)
    suffix=b'{"partial":"' + b'\xf0\x9f' if mode=='partial-utf8' or recovery else b'{"partial":'
    original=(records[1]+suffix if recovery else records[1]) if staged else records[0]+records[1]+(suffix if partial or mode.startswith('collision') else b'')
    pre={'neighbor.bin':b'\xff\x00\xfe','logs/'+name(50,'closed'):records[0],'logs/.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed':records[1],meta+'.unlisted.tmp':b'unlisted candidate\n'}
    stage=mode.split('-',1)[1] if '-' in mode else ''
    archived='logs/'+name(3 if staged else 1,'damaged' if recovery or partial else 'closed')
    if staged:
        pre['logs/'+name(2,'closed')]=records[0]
        if recovery:pre['logs/'+name(1,'damaged')]=b'\xff\n'
    if stage in ('renamed','published','created'):pre[archived]=original
    else:pre[ACTIVE]=original
    if stage=='created':pre[ACTIVE]=b''
    if mode=='ambiguous-both':pre[archived]=original
    if mode.startswith('collision'):pre['logs/'+name(1,'damaged')+('/keep.txt' if mode=='collision-directory' else '')]=b'reserved collision\n'
    files=seed['files'];before=resume['before'];after=resume['after']
    require(json.loads(decoded(files[meta]))==dict(head,segments=new if stage in ('published','created') else old),'initial inventory');pre[meta]=decoded(files[meta])
    if staged and mode!='unproven-gap':
        raw=decoded(files[pending]);require(raw.endswith(b'\n') and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf') and json.loads(raw)==dict(schema=1,recovery=recovery,retainedLimit=3 if recovery else 2,originalBytes=len(original),originalSha256=digest(original),previous=dict(head,segments=old),next=dict(head,segments=new)),'pending identity');pre[pending]=raw
    require({key:decoded(value) for key,value in files.items()}==pre,'literal stage preimage')
    def names(values):
        result={'logs',ACTIVE+'.cis.lock'}
        for key in values:
            result.add(key)
            if '/keep.txt' in key:result.add(key.rsplit('/',1)[0])
        return sorted(result)
    require(seed['names']==names(files),'stage membership')
    require(before==files and resume['namesBefore']==seed['names'],'cross-process preimage')
    if probe is not None:
        code='FileOwnershipUnavailable';require(probe['error']==dict(code=code,output='file',message="Logging output 'file' could not initialize ("+code+").",inner=False,dataCount=0) and probe['final'] is None and not probe['leaseReleased'],'held competing owner')
        require(probe['before']==probe['after']==files and probe['namesBefore']==probe['namesAfter']==seed['names'],'held probe preservation')
    if failure:
        code='FileRecoveryCollision' if mode.startswith('collision') else 'FileInventoryIncompatible' if mode=='unproven-gap' else 'FileTransitionIncompatible'
        require(resume['error']==dict(code=code,output='file',message="Logging output 'file' could not initialize ("+code+").",inner=False,dataCount=0),'safe failure')
        require(before==after and resume['namesBefore']==resume['namesAfter'],'unchanged restart failure');require(resume['final'] is None and resume['outcome'] is None,'failed publication');return
    require(resume['error'] is None and resume['outcome']=='Accepted','admission')
    require(resume['final']==dict(state='Stopped',accepted=1,written=1,failed=0,abandoned=0,pending=0,recovered=partial or recovery,fault=None),'current accounting')
    active=decoded(after[ACTIVE]);parts=active.splitlines(keepends=True)
    require(len(parts)==(3 if mode=='intact' else 1),'fresh record count')
    if mode=='intact':require(parts[:2]==records,'intact append preimage')
    record(parts[-1],'restart.resume',resume['pid'])
    metadata=decoded(after[meta]);require(metadata.endswith(b'\n') and b'\r' not in metadata and not metadata.startswith(b'\xef\xbb\xbf') and json.loads(metadata)==dict(head,segments=new),'next inventory')
    expected=dict(pre);expected.pop(pending,None);expected[meta]=metadata;expected[ACTIVE]=active
    if mode!='intact':expected[archived]=original
    require({key:decoded(value) for key,value in after.items()}==expected and resume['namesAfter']==names(after),'exact restart transition')
def qualify_selector(selector,seed):
    """Execute the actual helper against independent stopped-group mutations."""
    spec=importlib.util.spec_from_file_location('collector_example',selector);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    controls=[];positive=[];group='12345678123456781234567812345678';name=lambda identity,state:'.cis-log-'+group+'-'+str(identity).zfill(20)+'.'+state
    ids=['pending','human','partial-active','partial-closed','invalid-utf8','bom','crlf','array-record','duplicate-record-key','nonfinite-record','invalid-metadata','duplicate-metadata-key','traversal','foreign-name','duplicate-id','missing-member','directory-member','inventory-bound','entry-bound','inventory-bom','schema','encoding','bom-flag','newline','group-zero','active-first','symlink-member']
    with tempfile.TemporaryDirectory(prefix='cislib-selector-controls-') as temp:
        root=Path(temp).resolve()
        for key in ids:
            work=root/key;work.mkdir();active=work/'active.jsonl';meta=Path(str(active)+'.cis.inventory.json')
            damaged=work/name(1,'damaged');closed=work/name(2,'closed')
            active.write_bytes(decoded(seed['seedRecords'][1]));closed.write_bytes(decoded(seed['seedRecords'][0]));damaged.write_bytes(b'\xff\n')
            document=dict(schema=1,groupId=group,format='jsonl',encoding='utf-8',bom=False,newline='lf',segments=[dict(id=1,fileName=damaged.name,state='damaged'),dict(id=2,fileName=closed.name,state='closed'),dict(id=3,fileName=active.name,state='active')])
            meta.write_text(json.dumps(document),encoding='utf-8')
            (work/'unlisted.closed').write_bytes(b'{"not_selected":true}\n')
            (work/(meta.name+'.tmp')).write_bytes(b'private')
            require(module.select_inputs(active)==[str(closed),str(active)],'actual selector baseline excludes damage and neighbors')
            # Exact bound positives execute the actual parser with valid framing.
            if key=='entry-bound':
                exact=b'{"a":"'+b'x'*(module.ENTRY_BYTES-9)+b'"}\n';require(len(exact)==module.ENTRY_BYTES,'literal exact entry length');active.write_bytes(exact)
                require(module.select_inputs(active)==[str(closed),str(active)],'actual exact entry bound');positive.append(dict(id='entry-exact',bytes=len(exact),passed=True))
            if key=='inventory-bound':
                exact=json.dumps(document).encode();exact+=b' '*(module.INVENTORY_BYTES-len(exact));meta.write_bytes(exact)
                require(module.select_inputs(active)==[str(closed),str(active)],'actual exact inventory bound');positive.append(dict(id='inventory-exact',bytes=len(exact),passed=True))
            if key=='pending':Path(str(active)+'.cis.transition.json').write_bytes(b'{}')
            elif key=='human':document['format']='human'
            elif key=='partial-active':active.write_bytes(b'{"partial":')
            elif key=='partial-closed':closed.write_bytes(b'{"partial":')
            elif key=='invalid-utf8':active.write_bytes(b'{"value":"\xff"}\n')
            elif key=='bom':active.write_bytes(b'\xef\xbb\xbf{}\n')
            elif key=='crlf':active.write_bytes(b'{}\r\n')
            elif key=='array-record':active.write_bytes(b'[]\n')
            elif key=='duplicate-record-key':active.write_bytes(b'{"a":1,"a":2}\n')
            elif key=='nonfinite-record':active.write_bytes(b'{"a":NaN}\n')
            elif key=='invalid-metadata':meta.write_bytes(b'{')
            elif key=='duplicate-metadata-key':meta.write_text('{"schema":1,"schema":1}',encoding='utf-8')
            elif key=='traversal':document['segments'][0]['fileName']='../outside'
            elif key=='foreign-name':document['segments'][0]['fileName']=damaged.name.replace(group,'f'*32)
            elif key=='duplicate-id':document['segments'][1]['id']=1
            elif key=='missing-member':damaged.unlink()
            elif key=='directory-member':damaged.unlink();damaged.mkdir()
            elif key=='inventory-bound':meta.write_bytes(meta.read_bytes()+b' ')
            elif key=='entry-bound':active.write_bytes(b'{"a":"'+b'x'*module.ENTRY_BYTES+b'"}\n')
            elif key=='inventory-bom':meta.write_bytes(b'\xef\xbb\xbf'+meta.read_bytes())
            elif key=='schema':document['schema']=2
            elif key=='encoding':document['encoding']='utf-16'
            elif key=='bom-flag':document['bom']=True
            elif key=='newline':document['newline']='crlf'
            elif key=='group-zero':
                document['groupId']='0'*32
                for member in document['segments'][:-1]:
                    old=work/member['fileName'];member['fileName']=member['fileName'].replace(group,'0'*32);old.rename(work/member['fileName'])
            elif key=='active-first':document['segments'][0]['state']='active'
            elif key=='symlink-member':
                damaged.unlink()
                try:damaged.symlink_to(work/'unlisted.closed')
                except OSError as error:
                    require(os.name=='nt' and getattr(error,'winerror',None)==1314,'only unavailable Windows symlink privilege may skip')
                    controls.append(dict(id=key,rejected=None,skipped='Windows symlink privilege unavailable'));continue
            if key in ('human','traversal','foreign-name','duplicate-id','schema','encoding','bom-flag','newline','group-zero','active-first'):meta.write_text(json.dumps(document),encoding='utf-8')
            try:module.select_inputs(active)
            except (OSError,ValueError,TypeError,RecursionError):controls.append(dict(id=key,rejected=True))
            else:raise AssertionError('actual selector accepted '+key)
    require(len(controls)==27 and len(positive)==2,'selector controls count');return controls,positive

def main():
    p=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):p.add_argument('--'+key,required=True)
    args=p.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS')
    host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);rootrepo=Path(__file__).resolve().parents[5];selector=rootrepo/'docs/modules/logging/examples/select-jsonl-inputs.py'
    core=sha(host.parent/'CIS.Core.dll');fixture=sha(host);cases=[];controls=[];children=[]
    def command(mode,phase,work):return [args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-restart',mode,phase,str(work)]
    def save(identity,pid,cmd,code,stdout,stderr,killed=False):
        (out/(identity+'.stdout.bin')).write_bytes(stdout);(out/(identity+'.stderr.bin')).write_bytes(stderr)
        child=dict(id=identity,pid=pid,command=cmd,exitCode=code,killed=killed,stdoutSha256=digest(stdout),stderrSha256=digest(stderr));children.append(child);return child
    def run(mode,phase,work):
        cmd=command(mode,phase,work);env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);child=subprocess.Popen(cmd,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        stdout=b'';stderr=b''
        try:stdout,stderr=child.communicate(timeout=30);require(child.returncode==0 and stderr==b'','child completion');value=json.loads(stdout)
        finally:
            if child.poll() is None:child.kill();stdout,stderr=child.communicate(timeout=15)
            saved=save(mode+'-'+phase,child.pid,cmd,child.returncode,stdout,stderr);child.stdout.close();child.stderr.close()
        require((value['pid'],value['runtime'],value['os'],value['coreSha256'],value['fixtureSha256'])==(saved['pid'],'.NET 10.0.12',args.family,core,fixture),'actual child binding');return value
    samples={}
    with tempfile.TemporaryDirectory(prefix='cislib-restart-') as temp:
        root=Path(temp).resolve()
        for mode in MODES:
            work=root/mode;work.mkdir();cmd=command(mode,'hold',work);env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
            producer=subprocess.Popen(cmd,cwd=work,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE);lines=queue.Queue();raw=[];errors=[]
            def drain():
                for line in iter(producer.stdout.readline,b''):raw.append(line);lines.put(line)
                lines.put(b'')
            def error_drain():errors.append(producer.stderr.read())
            reader=threading.Thread(target=drain,daemon=True);error_reader=threading.Thread(target=error_drain,daemon=True);reader.start();error_reader.start();probe=None
            try:
                first=lines.get(timeout=30);require(first!=b'' and producer.poll() is None,'held producer');seed=json.loads(first)
                require((seed['pid'],seed['runtime'],seed['os'],seed['coreSha256'],seed['fixtureSha256'])==(producer.pid,'.NET 10.0.12',args.family,core,fixture),'actual producer binding')
                if mode in ('rotation-published','recovery-intent'):probe=run(mode,'probe',work)
                producer.kill();producer.wait(timeout=15);require(producer.poll() is not None,'owned producer exit')
            finally:
                if producer.poll() is None:producer.kill();producer.wait(timeout=15)
                reader.join(timeout=10);error_reader.join(timeout=10);require(not reader.is_alive() and not error_reader.is_alive(),'producer drain')
                save(mode+'-hold',producer.pid,cmd,producer.returncode,b''.join(raw),b''.join(errors),True);producer.stdin.close();producer.stdout.close();producer.stderr.close()
            require(b''.join(errors)==b'' and len(raw)==1,'original producer bytes')
            resume=run(mode,'resume',work);check(mode,seed,resume,probe)
            require(seed['path']==resume['path']==str(work/'logs'/ACTIVE[5:]),'captured path')
            physical={f.relative_to(work).as_posix():f.read_bytes() for f in work.rglob('*') if f.is_file() and not f.name.endswith('.cis.lock')}
            require(physical=={key:decoded(value) for key,value in resume['after'].items()},'physical post-restart bytes')
            cmd=[sys.executable,'-X','utf8',str(selector),resume['path']];collector=subprocess.Popen(cmd,cwd=work,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            stdout=b'';stderr=b''
            try:stdout,stderr=collector.communicate(timeout=30)
            finally:
                if collector.poll() is None:collector.kill();stdout,stderr=collector.communicate(timeout=15)
                collector_child=save(mode+'-collector',collector.pid,cmd,collector.returncode,stdout,stderr);collector.stdout.close();collector.stderr.close()
            failure=mode.startswith('collision') or mode in ('unproven-gap','ambiguous-both');selected=None
            if failure:require(collector.returncode==2 and stdout==b'' and stderr==b'Cannot select a consistent stopped JSONL group.'+(b'\r\n' if os.name=='nt' else b'\n'),'collector rejects unsafe group')
            else:
                require(collector.returncode==0 and stderr==b'','collector completion');selected=json.loads(stdout)
                inventory=json.loads(decoded(resume['after'][ACTIVE+'.cis.inventory.json']));expected=[str(work/'logs'/m['fileName']) for m in inventory['segments'] if m['state'] in ('active','closed')]
                require(selected==expected and len(selected)==(3 if mode.startswith('rotation') else 2 if mode.startswith('recovery') else 1),'collector exact inputs')
                for chosen in selected:
                    rawbytes=Path(chosen).read_bytes();require(rawbytes.endswith(b'\n'),'collector complete inputs')
                    for line in rawbytes.splitlines(keepends=True):
                        entry=json.loads(line);record(line,entry['event_name'],entry['attributes']['producer_pid']);require(entry['event_name']=='restart.resume' and entry['attributes']['producer_pid']==resume['pid'] or entry['event_name'] in ('restart.seed.0','restart.seed.1') and entry['attributes']['producer_pid']==seed['pid'],'collector producer selection')
            case=dict(id=mode,producer=seed,resume=resume,probe=probe,selected=selected,collector=collector_child);cases.append(case);samples[mode]=case
        reasons={'original':'actual record producer','stage':'literal stage preimage','pending':'pending identity','failure-code':'safe failure','failure-growth':'unchanged restart failure','lease':'owned process/lease','accounting':'current accounting','archive':'exact restart transition','neighbor':'exact restart transition','record':'actual record producer','inventory':'next inventory','probe':'held probe preservation'}
        for key,reason in reasons.items():
            mode='collision-file' if key.startswith('failure') else 'rotation-published' if key=='probe' else 'recovery-intent';bad=copy.deepcopy(samples[mode]);seed=bad['producer'];resume=bad['resume']
            if key=='original':
                value=json.loads(decoded(seed['seedRecords'][0]));value['event_name']='other';seed['seedRecords'][0]=base64.b64encode((json.dumps(value)+'\n').encode()).decode()
            elif key=='stage':seed['files']['neighbor.bin']=base64.b64encode(b'changed').decode()
            elif key=='pending':
                pending=ACTIVE+'.cis.transition.json';value=json.loads(decoded(seed['files'][pending]));value['originalSha256']='0'*64;seed['files'][pending]=base64.b64encode((json.dumps(value)+'\n').encode()).decode()
            elif key=='failure-code':resume['error']['code']='Other'
            elif key=='failure-growth':resume['after'][ACTIVE]=base64.b64encode(b'changed').decode()
            elif key=='lease':resume['leaseReleased']=False
            elif key=='accounting':resume['final']['written']=0
            elif key=='archive':resume['after'][next(n for n in resume['after'] if n.endswith('00000000000000000003.damaged'))]=base64.b64encode(b'changed').decode()
            elif key=='neighbor':resume['after']['neighbor.bin']=base64.b64encode(b'changed').decode()
            elif key=='record':
                value=json.loads(decoded(resume['after'][ACTIVE]));value['attributes']['producer_pid']=0;resume['after'][ACTIVE]=base64.b64encode((json.dumps(value,ensure_ascii=False)+'\n').encode()).decode()
            elif key=='inventory':
                meta=ACTIVE+'.cis.inventory.json';value=json.loads(decoded(resume['after'][meta]));value['segments'][-1]['id']=99;resume['after'][meta]=base64.b64encode((json.dumps(value)+'\n').encode()).decode()
            else:bad['probe']['after']['neighbor.bin']=base64.b64encode(b'changed').decode()
            try:check(mode,seed,resume,bad['probe'])
            except AssertionError as error:require(str(error)==reason,'specific corruption assertion');controls.append(dict(id=key,rejected=True,reason=reason))
            else:raise AssertionError('accepted corruption '+key)
    selector_controls,selector_positive=qualify_selector(selector,samples['intact']['producer'])
    require(len(children)==47 and len(cases)==15,'child/scenario count')
    report=dict(schema='cis.logging.file-restart-native/v1',task='WP05-S07-T04',family=args.family,runtime='.NET 10.0.12',children=47,producerProcesses=32,selectorProcesses=15,scenarios=15,coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),selectorSha256=sha(selector),pythonRuntime=sys.version,selectorControls=selector_controls,selectorPositiveBounds=selector_positive,cases=cases,childExecutions=children,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=47,scenarios=15,forcedOwnedExits=15,selectorProcesses=15,controls=12,selectorControls=len(selector_controls),passed=True)))
if __name__=='__main__':main()
