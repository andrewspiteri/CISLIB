"""Independent actual-Core owned/borrowed-host scope reader."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('scope_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('owned','external','hybrid','extensions','async-owned','async-external','bounds','invalid','host-incompatible','depth','filtered','correlation','replacement')


def same(actual,expected):
    if type(actual) is not type(expected):return False
    if isinstance(expected,dict):return actual.keys()==expected.keys() and all(same(actual[k],v) for k,v in expected.items())
    if isinstance(expected,list):return len(actual)==len(expected) and all(same(a,b) for a,b in zip(actual,expected))
    return actual==expected


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime')
    require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    require(a['physical'] is True and a['leaseReleased'] is True,'physical lifetime')
    count=4 if mode in ('invalid','host-incompatible') else 3 if mode in ('hybrid','async-owned','async-external','replacement') else 2 if mode=='bounds' else 0 if mode=='filtered' else 1
    calls=3 if mode in ('async-owned','async-external','depth') else 2 if mode in ('hybrid','bounds','replacement') else 4 if mode in ('invalid','host-incompatible') else 0 if mode=='filtered' else 1
    require(type(a['formatterCalls']) is int and a['formatterCalls']==calls,'formatter calls')
    require(type(a['ownedErrors']) is int and a['ownedErrors']==(1 if mode in ('invalid','depth') else 0),'owned scope errors')
    require(type(a['fieldVisits']) is int and a['fieldVisits']==(1025 if mode=='bounds' else 0),'bounded scope visits')
    require(type(a['filteredHostCalls']) is int and a['filteredHostCalls']==0,'filtered host capture')
    require(a['invalid']==('2' if mode=='depth' else '0'),'invalid accounting');require(a['truncated']==('2' if mode=='bounds' else '3' if mode in ('invalid','host-incompatible') else '0'),'truncation accounting')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape')
    require(result['completion']=='Completed' and result['succeeded'] is True and result['loss'] is (mode in ('bounds','invalid','host-incompatible','depth')),'stop summary')
    require(len(result['outputs'])==1 and result['outputs'][0]['name']=='file','file output');out=result['outputs'][0]
    require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==(count,count,0,0,0) and out['flush']=='Succeeded','core delivery')
    data=decoded(a['bytes']);require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf') if count else data==b'','actual framing');lines=data.splitlines();require(len(lines)==count and all(len(line)+1<=16384 for line in lines),'bounded records');rows=[load(line) for line in lines]
    names=['host','event','direct'] if mode=='hybrid' else ['fields','depth'] if mode=='bounds' else ['first','second','direct'] if mode=='replacement' else ['invalid']*3+['after'] if mode=='invalid' else ['string-host','string-map-host','microsoft-value-host','after'] if mode=='host-incompatible' else ['after'] if mode=='depth' else ['entry'] if count==1 else []
    if mode in ('async-owned','async-external'):require(sorted(row['event_name'] for row in rows)==['after','operation.1','operation.2'] and rows[2]['event_name']=='after','async completion order')
    else:require([row['event_name'] for row in rows]==names,'event names')
    for row in rows:
        require(type(row['schema_version']) is int and row['schema_version']==1 and row['status']=='info' and row['component']==('Direct' if row['event_name']=='direct' else 'Provider.Scope') and row['message']=='rendered','core wire mapping')
        require(row['service']=='CIS.Core.DiscoveryFixture' and row['resource']['process_id']==a['pid'] and row['resource']['runtime_description']=='.NET 10.0.12' and row['resource']['os_family']==a['os'],'automatic core facts')
        require(row.get('truncated') is True if mode=='bounds' or (mode in ('invalid','host-incompatible') and row['event_name']!='after') else 'truncated' not in row,'truncation marker')
    if mode=='owned':require(same(rows[0]['attributes'],{'owner':'original','items':[1,True],'event_id':17}),'owned snapshot')
    if mode=='external':require(same(rows[0]['attributes'],{'owner':'at-capture','event_id':17}) and rows[0]['operation_id']=='host.operation','host capture snapshot')
    if mode=='hybrid':
        require(same(rows[0]['attributes'],{'common':'host','core':True,'host':True,'event_id':17}) and rows[0]['operation_id']=='host.operation','host overlay')
        require(same(rows[1]['attributes'],{'common':'event','core':True,'host':True,'event_id':17}) and rows[1]['operation_id']=='host.operation','event overlay')
        require(same(rows[2]['attributes'],{'common':'core','core':True}) and rows[2]['operation_id']=='core.operation','direct core scope')
    if mode=='extensions':require(same(rows[0]['attributes'],{'Owner':'\u03a9','event_id':17}) and 'message_template' not in rows[0],'Microsoft scope fields')
    if mode in ('async-owned','async-external'):
        for row in rows[:2]:
            owner=row['attributes']['owner'];require(type(owner) is int and owner in (1,2) and same(row['attributes'],{'owner':owner,'event_id':17}) and row['operation_id']==row['event_name']=='operation.'+str(owner),'isolated async scopes')
        require(same(rows[2]['attributes'],{'event_id':17}) and 'operation_id' not in rows[2],'popped async scopes')
    if mode=='bounds':
        require(same(rows[0]['attributes'],{'k'+str(i).zfill(4):i for i in range(1023)}),'bounded owned prefix')
        deep=[]
        for _ in range(6):deep=[deep]
        require(same(rows[1]['attributes'],{'deep':deep,'event_id':17}),'bounded host depth')
    if mode=='depth':require(same(rows[0]['attributes'],{'event_id':17}),'no partial invalid scope')
    if mode in ('invalid','host-incompatible'):require(all(same(row['attributes'],{'event_id':17}) for row in rows),'incompatible frame omitted')
    if mode=='correlation':require(rows[0]['trace_id']==a['trace']=='11111111111111111111111111111111' and rows[0]['span_id']==a['span'] and len(a['span'])==16 and a['span']!='0'*16 and same(rows[0]['attributes'],{'trace_id':'fake','event_id':17}),'frozen Activity')
    if mode=='replacement':require(same(rows[0]['attributes'],{'owner':'first','event_id':17}) and same(rows[1]['attributes'],{'owner':'second','event_id':17}) and same(rows[2]['attributes'],{}),'replacement and ownership')


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def wire(a,edit,index=0):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows[index]);a['bytes']=base64.b64encode(b''.join(json.dumps(row,separators=(',',':')).encode()+b'\n' for row in rows)).decode()
    edits=[('pid','owned','actual child PID',lambda a:a.update(pid=a['pid']+1)),('lease','external','physical lifetime',lambda a:a.update(leaseReleased=False)),('formatter','filtered','formatter calls',lambda a:a.update(formatterCalls=1)),('owner-error','invalid','owned scope errors',lambda a:a.update(ownedErrors=0)),('copy-work','bounds','bounded scope visits',lambda a:a.update(fieldVisits=2000)),('host-work','filtered','filtered host capture',lambda a:a.update(filteredHostCalls=1)),('invalid','depth','invalid accounting',lambda a:a.update(invalid='1')),('loss','bounds','truncation accounting',lambda a:a.update(truncated='0')),('stop','owned','core delivery',lambda a:a['result']['outputs'][0].update(boundary='0')),('facts','owned','automatic core facts',lambda a:wire(a,lambda row:row.update(service='fake'))),('schema-type','owned','core wire mapping',lambda a:wire(a,lambda row:row.update(schema_version=True))),('owned','owned','owned snapshot',lambda a:wire(a,lambda row:row['attributes'].update(owner='changed'))),('host-copy','external','host capture snapshot',lambda a:wire(a,lambda row:row['attributes'].update(owner='after'))),('host-precedence','hybrid','host overlay',lambda a:wire(a,lambda row:row['attributes'].update(common='core'))),('event-precedence','hybrid','event overlay',lambda a:wire(a,lambda row:row['attributes'].update(common='host'),1)),('direct-context','hybrid','direct core scope',lambda a:wire(a,lambda row:row['attributes'].update(host=True),2)),('extension','extensions','Microsoft scope fields',lambda a:wire(a,lambda row:row['attributes'].update(Owner='wrong'))),('async','async-external','isolated async scopes',lambda a:wire(a,lambda row:row.update(operation_id='other'))),('popped','async-owned','popped async scopes',lambda a:wire(a,lambda row:row.update(operation_id='leak'),2)),('prefix-type','bounds','bounded owned prefix',lambda a:wire(a,lambda row:row['attributes'].update(k0000=False))),('nested-depth','bounds','bounded host depth',lambda a:wire(a,lambda row:row['attributes'].update(deep=[]),1)),('trace','correlation','frozen Activity',lambda a:wire(a,lambda row:row.update(trace_id='33333333333333333333333333333333'))),('incompatible-fields','host-incompatible','incompatible frame omitted',lambda a:wire(a,lambda row:row['attributes'].update(HttpMethod='GET'))),('incompatible-marker','host-incompatible','truncation marker',lambda a:wire(a,lambda row:row.pop('truncated'))),('replacement','replacement','replacement and ownership',lambda a:wire(a,lambda row:row['attributes'].update(owner='first'),1))]
    controls=[]
    for identity,mode,reason,edit in edits:
        bad=copy.deepcopy(good[mode]);edit(bad)
        try:check(mode,bad,good[mode]['pid'])
        except AssertionError as error:require(str(error)==reason,'unexpected corruption '+identity+': '+str(error));controls.append(dict(id=identity,rejected=True,reason=reason))
        else:raise AssertionError('corruption accepted '+identity)
    return controls

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require(args.family in ('windows','linux') and (os.name=='nt')==(args.family=='windows'),'native OS')
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-scope-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-scope-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-scope',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes();require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-scope-native/v1',task='WP06-S02-T01',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()