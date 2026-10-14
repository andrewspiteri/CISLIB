"""Independent actual-Core Microsoft structured state reader."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('provider_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('extensions','scalars','collections','precedence','unsupported','bounds','filtered','correlation','template-size')


def same(actual,expected):
    if type(actual) is not type(expected):return False
    if isinstance(expected,dict):return actual.keys()==expected.keys() and all(same(actual[k],v) for k,v in expected.items())
    if isinstance(expected,list):return len(actual)==len(expected) and all(same(a,b) for a,b in zip(actual,expected))
    return actual==expected


def shape(value):
    children=list(value.values()) if isinstance(value,dict) else value if isinstance(value,list) else []
    parts=[shape(v) for v in children];return 1+sum(p[0] for p in parts),1+max((p[1] for p in parts),default=0)


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime')
    require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    require(a['physical'] is True and a['leaseReleased'] is True and a['borrowedConsoleOpen'] is None,'physical lifetime')
    count=0 if mode in ('unsupported','filtered') else 3 if mode=='bounds' else 1
    calls=0 if mode in ('extensions','filtered') else 12 if mode=='unsupported' else 3 if mode=='bounds' else 1
    require(type(a['formatterCalls']) is int and a['formatterCalls']==calls,'formatter calls');require(type(a['stateIdentityChecks']) is int and a['stateIdentityChecks']==calls,'original state and null exception')
    require(type(a['arbitraryToStringCalls']) is int and a['arbitraryToStringCalls']==0,'no arbitrary ToString')
    require(type(a['stateEnumerationCalls']) is int and a['stateEnumerationCalls']==(1 if mode=='correlation' else 0),'state enumeration')
    require(type(a['arrayEnumerated']) is int and a['arrayEnumerated']==(1022 if mode=='bounds' else 0),'bounded traversal')
    require(a['invalid']==('12' if mode=='unsupported' else '0'),'invalid accounting');require(a['truncated']==('3' if mode=='bounds' else '1' if mode=='template-size' else '0'),'truncation accounting')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape');require((result['completion'],result['succeeded'],result['loss'])==('Completed',True,mode in ('unsupported','bounds','template-size')),'stop summary')
    require(len(result['outputs'])==1 and result['outputs'][0]['name']=='file','file output');out=result['outputs'][0]
    require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==(count,count,0,0,0) and out['flush']=='Succeeded','core delivery')
    data=decoded(a['bytes']);require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf') if count else data==b'','actual framing');lines=data.splitlines();require(len(lines)==count and all(len(line)+1<=16384 for line in lines),'actual bounded records')
    rows=[load(line) for line in lines]
    names=['array-bound','depth-bound','field-bound'] if mode=='bounds' else ['orders' if mode=='extensions' else mode] if count else []
    require([row['event_name'] for row in rows]==names,'event names')
    for row in rows:
        require(row['schema_version']==1 and row['status']=='info' and row['component']=='Provider.State' and row['service']=='CIS.Core.DiscoveryFixture' and row['resource']['process_id']==a['pid'] and row['resource']['runtime_description']=='.NET 10.0.12' and row['resource']['os_family']==a['os'],'automatic core facts')
        require(type(row['attributes']['event_id']) is int and row['attributes']['event_id']==17,'provider numeric ID')
        nodes,depth=shape(row['attributes']);require(nodes<=1024 and depth<=8,'attribute shape')
        require(row.get('truncated') is True if mode in ('bounds','template-size') else 'truncated' not in row,'truncation marker')
    if not count:return
    first=rows[0];attrs=first['attributes']
    if mode=='extensions':
        require(first['message']=='Orders 3 \u03a9' and first['message_template']=='Orders {Count} {User}','framework template/message')
        require(same(attrs,{'event_id':17,'Count':3,'User':'\u03a9'}),'framework fields')
    else:require(all(row['message']=='supplied rendered' for row in rows),'supplied rendered message')
    if mode=='scalars':
        expected={'event_id':17,'nil':None,'bool':True,'text':'\u03a9 \U0001f642','char':'x','sbyte':-8,'byte':8,'short':-16,'ushort':16,'int':-32,'uint':4294967295,'long':9007199254740991,'ulong':9007199254740991,'float':1.5,'double':2.5,'decimal':'1.20','wholeDecimal':'2.0','uuid':'11111111-2222-3333-4444-555555555555','offset':'2026-10-14T10:00:00.0000000Z','utc':'2026-10-14T10:00:00.0000000Z','explicit':'9223372036854775807'}
        require(same(attrs,expected),'scalar normalization');require(first['message_template']=='raw {template}','original format')
    if mode=='collections':require(same(attrs,{'event_id':17,'nested':{'array':[1,None,True],'typed':{'i':7}},'owned':['original']}) and 'message_template' not in first,'copied collections')
    if mode=='precedence':require(same(attrs,{'event_id':17,'common':'event','choice':2,'service':'fake'}) and first['operation_id']=='operation.original' and first['message_template']=='scope {choice}','event/scope precedence')
    if mode=='bounds':
        require(same(attrs,{'event_id':17,'array':list(range(1021))}) and shape(attrs)[0]==1024 and first['message_template']=='large {array}','array bound')
        deep=[]
        for _ in range(6):deep=[deep]
        require(same(rows[1]['attributes'],{'event_id':17,'deep':deep}) and shape(rows[1]['attributes'])==(9,8),'depth bound')
        require(same(rows[2]['attributes'],dict(event_id=17,**{'k'+str(i).zfill(4):i for i in range(1022)})),'field bound')
    if mode=='correlation':require(first['trace_id']==a['expectedTrace']=='11111111111111111111111111111111' and first['span_id']==a['expectedSpan'] and len(a['expectedSpan'])==16 and a['expectedSpan']!='0'*16 and same(attrs,{'event_id':17,'trace_id':'fake'}),'frozen Activity')
    if mode=='template-size':require(first['message_template']=='x'*8192 and same(attrs,{'event_id':17}),'template bound')


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def record_change(a,edit,index=0):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows[index]);a['bytes']=base64.b64encode(b''.join(json.dumps(r,ensure_ascii=False,separators=(',',':')).encode()+b'\n' for r in rows)).decode()
    edits=[
      ('pid','extensions','actual child PID',lambda a:a.update(pid=a['pid']+1)),
      ('runtime','extensions','actual runtime',lambda a:a.update(runtime='.NET 10.0.13')),
      ('lease','extensions','physical lifetime',lambda a:a.update(leaseReleased=False)),
      ('formatter','scalars','formatter calls',lambda a:a.update(formatterCalls=0)),
      ('identity','scalars','original state and null exception',lambda a:a.update(stateIdentityChecks=0)),
      ('ToString','unsupported','no arbitrary ToString',lambda a:a.update(arbitraryToStringCalls=1)),
      ('filtered-traversal','filtered','state enumeration',lambda a:a.update(stateEnumerationCalls=1)),
      ('bounded-traversal','bounds','bounded traversal',lambda a:a.update(arrayEnumerated=2000)),
      ('invalid','unsupported','invalid accounting',lambda a:a.update(invalid='0')),
      ('truncated','bounds','truncation accounting',lambda a:a.update(truncated='0')),
      ('shape','extensions','result shape',lambda a:a['result'].update(extra=True)),
      ('summary','unsupported','stop summary',lambda a:a['result'].update(loss=False)),
      ('ticket','extensions','core delivery',lambda a:a['result']['outputs'][0].update(boundary='0')),
      ('framing','extensions','actual framing',lambda a:a.update(bytes=base64.b64encode(decoded(a['bytes'])[:-1]).decode())),
      ('event-name','extensions','event names',lambda a:record_change(a,lambda row:row.update(event_name='wrong'))),
      ('identity-facts','extensions','automatic core facts',lambda a:record_change(a,lambda row:row.update(service='fake'))),
      ('provider-id','precedence','provider numeric ID',lambda a:record_change(a,lambda row:row['attributes'].update(event_id=999))),
      ('marker','bounds','truncation marker',lambda a:record_change(a,lambda row:row.pop('truncated'))),
      ('framework-template','extensions','framework template/message',lambda a:record_change(a,lambda row:row.update(message_template='wrong'))),
      ('framework-fields','extensions','framework fields',lambda a:record_change(a,lambda row:row['attributes'].update(Count='3'))),
      ('rendered','scalars','supplied rendered message',lambda a:record_change(a,lambda row:row.update(message='wrong'))),
      ('scalar-type','scalars','scalar normalization',lambda a:record_change(a,lambda row:row['attributes'].update(bool=1))),
      ('decimal','scalars','scalar normalization',lambda a:record_change(a,lambda row:row['attributes'].update(decimal=1.2))),
      ('format','scalars','original format',lambda a:record_change(a,lambda row:row.update(message_template='wrong'))),
      ('copied-array','collections','copied collections',lambda a:record_change(a,lambda row:row['attributes']['nested']['array'].__setitem__(0,999))),
      ('nested-bool-type','collections','copied collections',lambda a:record_change(a,lambda row:row['attributes']['nested']['array'].__setitem__(2,1))),
      ('precedence','precedence','event/scope precedence',lambda a:record_change(a,lambda row:row['attributes'].update(common='outer'))),
      ('duplicates','precedence','event/scope precedence',lambda a:record_change(a,lambda row:row['attributes'].update(choice=1))),
      ('array-count','bounds','array bound',lambda a:record_change(a,lambda row:row['attributes']['array'].pop())),
      ('depth-value','bounds','depth bound',lambda a:record_change(a,lambda row:row['attributes'].update(deep=[]),1)),
      ('field-prefix','bounds','field bound',lambda a:record_change(a,lambda row:row['attributes'].pop('k1021'),2)),
      ('trace','correlation','frozen Activity',lambda a:record_change(a,lambda row:row.update(trace_id='33333333333333333333333333333333'))),
      ('span','correlation','frozen Activity',lambda a:record_change(a,lambda row:row.update(span_id='0'*16))),
      ('template-size','template-size','template bound',lambda a:record_change(a,lambda row:row.update(message_template='x'*8191)))
    ]
    controls=[]
    for identity,mode,reason,edit in edits:
        bad=copy.deepcopy(good[mode]);edit(bad)
        try:check(mode,bad,good[mode]['pid'])
        except AssertionError as error:
            require(str(error)==reason,'unexpected corruption '+identity+': '+str(error));controls.append(dict(id=identity,rejected=True,reason=reason))
        else:raise AssertionError('corruption accepted '+identity)
    return controls


def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();require(args.family in ('windows','linux') and (os.name=='nt')==(args.family=='windows'),'native OS')
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-state-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-state-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-state',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes();require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-state-native/v1',task='WP06-S01-T03',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()