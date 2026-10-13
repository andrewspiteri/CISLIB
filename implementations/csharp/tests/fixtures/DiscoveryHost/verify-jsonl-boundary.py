"""Owned JSONL inspection/recovery with literal seeds and exact physical transitions."""
import argparse,base64,copy,hashlib,json,os,re,subprocess,tempfile
from pathlib import Path
MODES=['empty','object','unicode','two-records','chunk-boundary','missing-lf','partial-json','partial-utf8','invalid-json','invalid-utf8','array','blank','crlf','bom','missing-inventory','wrong-format','wrong-encoding','repeat','with-retained','collision-file','collision-directory','retention-full','identity-exhausted']
RECOVERY={'missing-lf','partial-json','partial-utf8','repeat','with-retained'}
COMPLETE={'empty','object','unicode','two-records','chunk-boundary'}
IDENTITY={'missing-inventory','wrong-format','wrong-encoding'}
PARTIAL=RECOVERY|{'collision-file','collision-directory','retention-full','identity-exhausted'}
ACTIVE='\u03a9-\U0001f642.jsonl'
FOREIGN='.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
encoded=lambda b:base64.b64encode(b).decode('ascii')
def require(value,reason):
    if not value:raise AssertionError(reason)
def decoded(a,key):return base64.b64decode(a[key],validate=True)
def seed(mode):
    if mode in {'repeat','with-retained','collision-file','collision-directory','retention-full','identity-exhausted'}:return b'{"stage":'
    return {'empty':b'','unicode':'{"message":"\u03a9 \U0001f642 e\u0301"}\n'.encode('utf-8'),'two-records':b'{"first":true}\n{"next":true}\n','chunk-boundary':b'{"prior":true}\n{"message":"'+b'x'*4081+b'"}\n','missing-lf':b'{"a":1}','partial-json':b'{"prior":true}\n{"message":','partial-utf8':b'{"prior":true}\n{"message":"'+bytes([240,159]),'invalid-json':b'{"a":\n','invalid-utf8':b'{"a":"'+bytes([255])+b'"}\n','array':b'[]\n','blank':b'\n','crlf':b'{}\r\n','bom':bytes([239,187,191])+b'{}\n','wrong-format':bytes([255,10]),'wrong-encoding':bytes([255,10])}.get(mode,b'{"a":1}\n')
def name(group,id,state):return '.cis-log-'+group+'-'+str(id).zfill(20)+'.'+state
def record(line,pid):
    require(line.endswith(b'\n') and line.count(b'\n')==1 and b'\r' not in line and not line.startswith(bytes([239,187,191])) and 0<len(line)<=16384,'complete appended record')
    a=json.loads(line.decode('utf-8',errors='strict'));expected=dict(schema_version=1,event_name='boundary.append',component='Boundary',message='\u03a9 \U0001f642',status='info',service='Console.App',env='test',version='1.2.3',timestamp='2026-10-13T10:11:12.0000000Z')
    require(all(a.get(k)==v for k,v in expected.items()) and a['attributes']==dict(producer_pid=pid) and a['resource']['instance_id']=='11111111-1111-1111-1111-111111111111' and 'truncated' not in a,'literal append')
def check(mode,a):
    require(a['mode']==mode,'mode');require(a['inspectionReleased'] is True and a['finalReleased'] is True,'in-process release')
    identity=mode in IDENTITY;success=mode in RECOVERY|COMPLETE;recovery=mode in RECOVERY
    require(a['inspected']==(0 if identity else 1),'identity before inspection')
    error={'missing-inventory':'FileInventoryMissing','wrong-format':'FileInventoryIncompatible','wrong-encoding':'FileInventoryIncompatible','collision-file':'FileRecoveryCollision','collision-directory':'FileRecoveryCollision','retention-full':'FileRetentionLimitExceeded','identity-exhausted':'FileRecoveryIdentityExhausted'}.get(mode,None if success else 'FileRecordBoundaryInvalid')
    require(a['inspectionError']==(error if identity else None) and a['startupError']==error,'safe startup error')
    before,after=decoded(a,'before'),decoded(a,'after');require(before==seed(mode),'literal seed')
    if identity:require(all(a[k] is None for k in ('status','length','originalPosition','restoredPosition')),'uninspected identity')
    else:
        status='Empty' if mode=='empty' else 'Partial' if mode in PARTIAL else 'Complete' if mode in COMPLETE else 'Invalid'
        require(a['status']==status and a['length']==len(before),'boundary classification')
        require(a['originalPosition']==(0 if mode=='empty' else 3) and a['restoredPosition']==a['originalPosition'],'stream position')
    pre={ACTIVE:encoded(seed(mode)),ACTIVE+'.cis.lock':'',FOREIGN:encoded(b'untouched neighbor\n')}
    old=None;group=None;id=1
    if mode=='missing-inventory':require(a['inventoryBefore'] is None,'missing inventory')
    else:
        old=json.loads(decoded(a,'inventoryBefore').decode('utf-8',errors='strict'));group=old['groupId'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'persistent group')
        id=7 if mode=='with-retained' else 2 if mode=='retention-full' else 9223372036854775807 if mode=='identity-exhausted' else 1
        segments=[]
        if mode in {'with-retained','retention-full'}:
            closed=name(group,1,'closed');segments.append(dict(id=1,fileName=closed,state='closed'));pre[closed]=encoded(b'prior closed evidence\n')
        if mode=='with-retained':
            damaged=name(group,3,'damaged');segments.append(dict(id=3,fileName=damaged,state='damaged'));pre[damaged]=encoded(bytes([255,10]))
        segments.append(dict(id=id,fileName=ACTIVE,state='active'))
        require(old==dict(schema=1,groupId=group,format='human' if mode=='wrong-format' else 'jsonl',encoding='utf-16' if mode=='wrong-encoding' else 'utf-8',bom=False,newline='lf',segments=segments),'literal inventory identity')
        pre[ACTIVE+'.cis.inventory.json']=a['inventoryBefore']
        if mode in {'collision-file','collision-directory'}:pre[name(group,1,'damaged')+('/keep.txt' if mode=='collision-directory' else '')]=encoded(b'reserved name\n')
    require(a['preimage']==pre,'literal physical preimage');require(decoded(a,'neighbor')==b'untouched neighbor\n','neighbor preservation')
    expected=copy.deepcopy(pre)
    if not success:
        require(a['outcome'] is None and a['written'] is None and all(a[k] is None for k in ('recovered','heldRecovered','restartRecovered')) and a['phases']==[] and after==before,'failed startup preserves bytes')
        require(a['inventoryBefore']==a['inventoryAfter'],'inventory unchanged')
    else:
        require(a['outcome']=='Accepted' and a['written']==1,'actual append')
        require(a['recovered'] is recovery and a['heldRecovered'] is recovery and a['restartRecovered'] is False,'recovery diagnostics')
        if recovery:record(after,a['pid'])
        else:require(after.startswith(before),'intact append');record(after[len(before):],a['pid'])
        phase=dict(recovered=recovery,heldRecovered=recovery,heldAccepted=0,heldFailed=0,heldFault=None,state='Stopped',accepted=1,written=1,failed=0,abandoned=0,pending=0,fault=None,faultAt=None)
        require(a['phases']==[phase]*(2 if mode=='repeat' else 1),'current delivery accounting')
        if recovery:
            next=copy.deepcopy(old);damaged=name(group,id,'damaged');expected[damaged]=a['before'];next['segments'][-1]=dict(id=id,fileName=damaged,state='damaged');next['segments'].append(dict(id=id+1,fileName=ACTIVE,state='active'))
            if mode=='repeat':
                first=decoded(a,'firstActive');record(first,a['pid']);interrupted=first+b'{"again":"'+bytes([240,159]);require(decoded(a,'repeatBefore')==interrupted,'second interrupted preimage')
                second=name(group,id+1,'damaged');expected[second]=a['repeatBefore'];next['segments'][-1]=dict(id=id+1,fileName=second,state='damaged');next['segments'].append(dict(id=id+2,fileName=ACTIVE,state='active'))
            raw=decoded(a,'inventoryAfter');require(raw.endswith(b'\n') and b'\r' not in raw and not raw.startswith(bytes([239,187,191])),'published inventory framing')
            require(json.loads(raw.decode('utf-8',errors='strict'))==next,'persistent damaged inventory');expected[ACTIVE+'.cis.inventory.json']=a['inventoryAfter']
        else:require(a['inventoryBefore']==a['inventoryAfter'],'inventory unchanged')
        expected[ACTIVE]=a['after']
    require(a['filesystem']==expected,'exact physical transition')
    require(a['names']==sorted({key.split('/')[0] for key in expected}),'exact membership')
def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core,fixture=sha(host.parent/'CIS.Core.dll'),sha(host);cases=[];controls=[]
    with tempfile.TemporaryDirectory(prefix='cislib-jsonl-recovery-') as temp:
        for mode in MODES:
            work=Path(temp).resolve()/mode;work.mkdir();command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'jsonl-boundary',mode,str(work)];env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);p=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout,stderr=b'',b''
            try:
                try:stdout,stderr=p.communicate(timeout=30)
                except BaseException:p.kill();stdout,stderr=p.communicate(timeout=15);raise
            finally:(out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
            require(p.returncode==0 and stderr==b'','child failure');a=json.loads(stdout.decode('utf-8',errors='strict'));require((a['pid'],a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==(p.pid,'.NET 10.0.12',args.family,core,fixture),'actual process/runtime/assembly');check(mode,a)
            path=work/'logs'/ACTIVE;require(a['path']==str(path),'physical captured path');physical={str(x.relative_to(path.parent)).replace(os.sep,'/'):encoded(x.read_bytes()) for x in path.parent.rglob('*') if x.is_file()}
            require(physical==a['filesystem'] and sorted(x.name for x in path.parent.iterdir())==a['names'],'physical filesystem/membership')
            cases.append(dict(id=mode,pid=p.pid,command=command,exitCode=p.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
        bymode={c['id']:c['report'] for c in cases}
        def damage_bytes(a):
            key=next(k for k in a['filesystem'] if k.endswith('.damaged'));a['filesystem'][key]=encoded(b'changed')
        def damage_status(a):
            doc=json.loads(decoded(a,'inventoryAfter'));doc['segments'][-2]['state']='closed';a['inventoryAfter']=encoded(json.dumps(doc).encode()+b'\n')
        mutations=[('status','object','boundary classification',lambda a:a.update(status='Partial')),('position','object','stream position',lambda a:a.update(restoredPosition=4)),('identity-order','wrong-format','identity before inspection',lambda a:a.update(inspected=1)),('append','object','literal append',lambda a:a.update(after=encoded(decoded(a,'after').replace(b'boundary.append',b'boundary.other')))),('failed-growth','invalid-json','failed startup preserves bytes',lambda a:a.update(after=encoded(decoded(a,'after')+b'{}\n'))),('inventory','object','inventory unchanged',lambda a:a.update(inventoryAfter=encoded(b'{}'))),('release','object','in-process release',lambda a:a.update(finalReleased=False)),('recovery-flag','partial-json','recovery diagnostics',lambda a:a.update(recovered=False)),('damaged-preimage','partial-json','exact physical transition',damage_bytes),('damage-status','partial-json','persistent damaged inventory',damage_status),('restart-flag','repeat','recovery diagnostics',lambda a:a.update(restartRecovered=True)),('accounting','partial-utf8','current delivery accounting',lambda a:a['phases'][0].update(failed=1))]
        for identity,mode,reason,mutate in mutations:
            bad=copy.deepcopy(bymode[mode]);mutate(bad)
            try:check(mode,bad)
            except AssertionError as error:require(str(error)==reason,'control reached wrong assertion: '+identity);controls.append(dict(id=identity,rejected=True,reason=reason))
            else:raise AssertionError('corruption accepted: '+identity)
    report=dict(schema='cis.logging.jsonl-recovery-native/v1',task='WP05-S07-T02',family=args.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
