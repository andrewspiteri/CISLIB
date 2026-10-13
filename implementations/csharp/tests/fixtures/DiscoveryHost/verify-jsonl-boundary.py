"""Owned JSONL tail inspection with literal seeds and physical original-byte checks."""
import argparse, base64, copy, hashlib, json, os, re, subprocess, tempfile
from pathlib import Path
MODES=['empty','object','unicode','two-records','chunk-boundary','missing-lf','partial-json','partial-utf8','invalid-json','invalid-utf8','array','blank','crlf','bom','missing-inventory','wrong-format','wrong-encoding']
COMPLETE={'empty','object','unicode','two-records','chunk-boundary'}
IDENTITY={'missing-inventory','wrong-format','wrong-encoding'}
ACTIVE='\u03a9-\U0001f642.jsonl'
FOREIGN='.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest=lambda b:hashlib.sha256(b).hexdigest()
def require(value,reason):
    if not value:raise AssertionError(reason)
def decoded(a,key):return base64.b64decode(a[key],validate=True)
def seed(mode):
    return {'empty':b'','unicode':'{"message":"\u03a9 \U0001f642 e\u0301"}\n'.encode('utf-8'),'two-records':b'{"first":true}\n{"next":true}\n','chunk-boundary':b'{"prior":true}\n{"message":"'+b'x'*4081+b'"}\n','missing-lf':b'{"a":1}','partial-json':b'{"prior":true}\n{"message":','partial-utf8':b'{"prior":true}\n{"message":"'+bytes([240,159]),'invalid-json':b'{"a":\n','invalid-utf8':b'{"a":"'+bytes([255])+b'"}\n','array':b'[]\n','blank':b'\n','crlf':b'{}\r\n','bom':bytes([239,187,191])+b'{}\n','wrong-format':bytes([255,10]),'wrong-encoding':bytes([255,10])}.get(mode,b'{"a":1}\n')
def check(mode,a):
    require(a['mode']==mode,'mode')
    require(a['inspectionReleased'] is True and a['finalReleased'] is True,'in-process release')
    identity=mode in IDENTITY
    require(a['inspected']==(0 if identity else 1),'identity before inspection')
    error='FileInventoryMissing' if mode=='missing-inventory' else 'FileInventoryIncompatible' if identity else None if mode in COMPLETE else 'FileRecordBoundaryInvalid'
    require(a['inspectionError']==(error if identity else None) and a['startupError']==error,'safe startup error')
    before,after=decoded(a,'before'),decoded(a,'after');require(before==seed(mode),'literal seed')
    if identity:require(all(a[k] is None for k in ('status','length','originalPosition','restoredPosition')),'uninspected identity')
    else:
        status='Empty' if mode=='empty' else 'Complete' if mode in COMPLETE else 'Partial' if mode in {'missing-lf','partial-json','partial-utf8'} else 'Invalid'
        require(a['status']==status and a['length']==len(before),'boundary classification')
        require(a['originalPosition']==(0 if mode=='empty' else 3) and a['restoredPosition']==a['originalPosition'],'stream position')
    require(a['inventoryBefore']==a['inventoryAfter'],'inventory unchanged')
    if mode=='missing-inventory':require(a['inventoryBefore'] is None,'missing inventory')
    else:
        inventory=json.loads(decoded(a,'inventoryBefore').decode('utf-8',errors='strict'));group=inventory['groupId'];require(re.fullmatch('[0-9a-f]{32}',group) and group!='0'*32,'persistent group')
        require(inventory==dict(schema=1,groupId=group,format='human' if mode=='wrong-format' else 'jsonl',encoding='utf-16' if mode=='wrong-encoding' else 'utf-8',bom=False,newline='lf',segments=[dict(id=1,fileName=ACTIVE,state='active')]),'literal inventory identity')
    require(decoded(a,'neighbor')==b'untouched neighbor\n','neighbor preservation')
    require(a['names']==sorted([FOREIGN,ACTIVE,ACTIVE+'.cis.lock']+([] if mode=='missing-inventory' else [ACTIVE+'.cis.inventory.json'])),'exact membership')
    if mode not in COMPLETE:require(a['outcome'] is None and a['written'] is None and after==before,'failed startup preserves bytes')
    else:
        require(a['outcome']=='Accepted' and a['written']==1 and after.startswith(before),'actual append');line=after[len(before):]
        require(line.endswith(b'\n') and line.count(b'\n')==1 and b'\r' not in line and not line.startswith(bytes([239,187,191])) and 0<len(line)<=16384,'complete appended record')
        record=json.loads(line.decode('utf-8',errors='strict'));expected=dict(schema_version=1,event_name='boundary.append',component='Boundary',message='\u03a9 \U0001f642',status='info',service='Console.App',env='test',version='1.2.3',timestamp='2026-10-13T10:11:12.0000000Z')
        require(all(record.get(k)==v for k,v in expected.items()) and record['attributes']==dict(producer_pid=a['pid']) and record['resource']['instance_id']=='11111111-1111-1111-1111-111111111111' and 'truncated' not in record,'literal append')
def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require((os.name=='nt')==(args.family=='windows'),'native OS');host=Path(args.host).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);core,fixture=sha(host.parent/'CIS.Core.dll'),sha(host);cases=[];controls=[]
    with tempfile.TemporaryDirectory(prefix='cislib-jsonl-boundary-') as temp:
        for mode in MODES:
            work=Path(temp).resolve()/mode;work.mkdir();command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'jsonl-boundary',mode,str(work)];env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None);p=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout,stderr=b'',b''
            try:
                try:stdout,stderr=p.communicate(timeout=30)
                except BaseException:p.kill();stdout,stderr=p.communicate(timeout=15);raise
            finally:
                (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
            require(p.returncode==0 and stderr==b'','child failure');a=json.loads(stdout.decode('utf-8',errors='strict'))
            require((a['pid'],a['runtime'],a['os'],a['coreSha256'],a['fixtureSha256'])==(p.pid,'.NET 10.0.12',args.family,core,fixture),'actual process/runtime/assembly');check(mode,a)
            path=work/'logs'/ACTIVE;require(a['path']==str(path) and path.read_bytes()==decoded(a,'after'),'physical active path/bytes');metadata=Path(str(path)+'.cis.inventory.json')
            require((metadata.read_bytes() if metadata.exists() else None)==(decoded(a,'inventoryAfter') if a['inventoryAfter'] is not None else None),'physical inventory bytes')
            require((path.parent/FOREIGN).read_bytes()==b'untouched neighbor\n' and sorted(x.name for x in path.parent.iterdir())==a['names'],'physical neighbor/membership')
            cases.append(dict(id=mode,pid=p.pid,command=command,exitCode=p.returncode,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
        bymode={c['id']:c['report'] for c in cases}
        mutations=[('status','object','boundary classification',lambda a:a.update(status='Partial')),('position','object','stream position',lambda a:a.update(restoredPosition=4)),('identity-order','wrong-format','identity before inspection',lambda a:a.update(inspected=1)),('append','object','literal append',lambda a:a.update(after=base64.b64encode(decoded(a,'after').replace(b'boundary.append',b'boundary.other')).decode('ascii'))),('failed-growth','partial-utf8','failed startup preserves bytes',lambda a:a.update(after=base64.b64encode(decoded(a,'after')+b'{}\n').decode('ascii'))),('inventory','object','inventory unchanged',lambda a:a.update(inventoryAfter=base64.b64encode(b'{}').decode('ascii'))),('release','object','in-process release',lambda a:a.update(finalReleased=False))]
        for identity,mode,reason,mutate in mutations:
            bad=copy.deepcopy(bymode[mode]);mutate(bad)
            try:check(mode,bad)
            except AssertionError as error:
                require(str(error)==reason,'control reached wrong assertion: '+identity);controls.append(dict(id=identity,rejected=True,reason=reason))
            else:raise AssertionError('corruption accepted: '+identity)
    report=dict(schema='cis.logging.jsonl-boundary-native/v1',task='WP05-S07-T01',family=args.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core,fixtureSha256=fixture,verifierSha256=sha(__file__),cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
