"""Independent actual-Core scope operation identity and precedence reader."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('operation_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('owned','host','nested-owned','nested-host','duplicates','nonstring','event','protected','bounds','invalid','host-omitted','callback-errors')


def same(actual,expected):
    if type(actual) is not type(expected):return False
    if isinstance(expected,dict):return actual.keys()==expected.keys() and all(same(actual[k],v) for k,v in expected.items())
    if isinstance(expected,list):return len(actual)==len(expected) and all(same(a,b) for a,b in zip(actual,expected))
    return actual==expected


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime');require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    require(a['physical'] is True and a['leaseReleased'] is True,'physical lifetime')
    count={'nested-owned':4,'nested-host':3,'duplicates':5,'nonstring':2,'event':3,'bounds':2}.get(mode,1);calls=1 if mode=='event' else 3 if mode=='callback-errors' else count
    require(type(a['formatterCalls']) is int and a['formatterCalls']==calls,'formatter calls');require(type(a['ownedErrors']) is int and a['ownedErrors']==(3 if mode=='invalid' else 0),'owned ID errors')
    require(a['invalid']==('2' if mode=='callback-errors' else '0'),'invalid accounting');require(a['truncated']==('2' if mode=='bounds' else '1' if mode=='host-omitted' else '0'),'truncation accounting')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape');require(result['completion']=='Completed' and result['succeeded'] is True and result['loss'] is (mode in ('bounds','host-omitted','callback-errors')),'stop summary')
    require(len(result['outputs'])==1 and result['outputs'][0]['name']=='file','file output');out=result['outputs'][0];require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==(count,count,0,0,0) and out['flush']=='Succeeded','core delivery')
    data=decoded(a['bytes']);require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf'),'actual framing');lines=data.splitlines();require(len(lines)==count and all(len(line)+1<=16384 for line in lines),'bounded records');rows=[load(line) for line in lines]
    names={'nested-owned':['outer','inner','blank','after'],'nested-host':['outer','inner','blank'],'duplicates':['duplicate.'+str(i) for i in range(5)],'nonstring':['integer','explicit-value'],'event':['provider','explicit','blank'],'bounds':['retained','late'],'callback-errors':['after']}.get(mode,['entry']);require([row['event_name'] for row in rows]==names,'event names')
    for row in rows:
        require(type(row['schema_version']) is int and row['schema_version']==1 and row['status']=='info' and row['message']=='rendered' and row['component']==('Direct' if mode=='event' and row['event_name']!='provider' else 'Provider.Operation'),'core wire mapping')
        require(row['service']=='CIS.Core.DiscoveryFixture' and row['resource']['process_id']==a['pid'] and row['resource']['runtime_description']=='.NET 10.0.12' and row['resource']['os_family']==a['os'],'automatic core facts')
        require(row.get('truncated') is True if mode in ('bounds','host-omitted') else 'truncated' not in row,'truncation marker')
    def row(i,operation,attrs):require(rows[i].get('operation_id')==operation and same(rows[i]['attributes'],attrs),'operation and attributes')
    if mode=='owned':row(0,'owned.operation',{'common':'original','event_id':17})
    if mode=='host':row(0,'host.operation',{'common':'at-capture','event_id':17})
    if mode=='nested-owned':
        row(0,'outer',{'common':'outer','event_id':17});row(1,'inner',{'common':'inner','event_id':17});row(2,'inner',{'common':'event','event_id':17,'operation_id':'attribute-only'});row(3,None,{'event_id':17})
    if mode=='nested-host':
        row(0,'host.outer',{'core':True,'common':'outer','event_id':17});row(1,'host.inner',{'core':True,'common':'inner','event_id':17});row(2,'host.inner',{'core':True,'common':'event','event_id':17})
    if mode=='duplicates':
        row(0,'parent',{'event_id':17,'operation_id':7});row(1,'two',{'event_id':17});row(2,'parent',{'event_id':17});row(3,'parent',{'event_id':17,'Operation_Id':'ordinary'});row(4,'parent',{'event_id':17,'operation_id':None})
    if mode=='nonstring':row(0,None,{'event_id':17,'operation_id':7});row(1,None,{'event_id':17,'operation_id':'explicit'})
    if mode=='event':row(0,'scope',{'common':'event','event_id':17,'operation_id':'attribute-only'});row(1,'event.id',{'common':'direct'});row(2,'scope',{'common':'scope'})
    if mode=='protected':
        row(0,'protected.operation',{'event_id':17,'service':'event.fake','env':'fake','version':'fake','component':'fake','resource':'fake','trace_id':'event.fake','span_id':'fake'})
        require(rows[0]['env']!='fake' and rows[0]['version']!='fake' and rows[0]['trace_id']==a['trace']=='11111111111111111111111111111111' and rows[0]['span_id']==a['span'] and len(a['span'])==16 and a['span']!='0'*16,'protected identity and Activity')
    if mode=='bounds':row(0,'bounded',dict(common='event',**{'k'+str(i).zfill(4):i for i in range(1022)}));row(1,None,{'k'+str(i).zfill(4):i for i in range(1023)})
    if mode=='invalid':row(0,'x'*255,{'common':'valid','event_id':17})
    if mode=='host-omitted':row(0,'parent',{'common':'parent','event_id':17})
    if mode=='callback-errors':row(0,None,{'event_id':17})


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def wire(a,edit,index=0):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows[index]);a['bytes']=base64.b64encode(b''.join(json.dumps(row,separators=(',',':')).encode()+b'\n' for row in rows)).decode()
    edits=[('pid','owned','actual child PID',lambda a:a.update(pid=a['pid']+1)),('lease','host','physical lifetime',lambda a:a.update(leaseReleased=False)),('formatter','owned','formatter calls',lambda a:a.update(formatterCalls=0)),('owned-errors','invalid','owned ID errors',lambda a:a.update(ownedErrors=0)),('callback-loss','callback-errors','invalid accounting',lambda a:a.update(invalid='0')),('omitted-loss','host-omitted','truncation accounting',lambda a:a.update(truncated='0')),('ticket','event','core delivery',lambda a:a['result']['outputs'][0].update(boundary='1')),('names','nested-owned','event names',lambda a:wire(a,lambda row:row.update(event_name='wrong'))),('schema','owned','core wire mapping',lambda a:wire(a,lambda row:row.update(schema_version=True))),('service','protected','automatic core facts',lambda a:wire(a,lambda row:row.update(service='event.fake'))),('marker','host-omitted','truncation marker',lambda a:wire(a,lambda row:row.pop('truncated'))),('owned-copy','owned','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='changed'))),('host-copy','host','operation and attributes',lambda a:wire(a,lambda row:row['attributes'].update(common='before'))),('nested','nested-owned','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='outer'),1)),('blank','nested-host','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id=''),2)),('duplicate','duplicates','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='one'))),('case','duplicates','operation and attributes',lambda a:wire(a,lambda row:row['attributes'].update(operation_id='ordinary'),3)),('nonstring','nonstring','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='7'))),('explicit-value','nonstring','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='explicit'),1)),('event-override','event','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='scope'),1)),('trace','protected','protected identity and Activity',lambda a:wire(a,lambda row:row.update(trace_id='event.fake'))),('bound-id','bounds','operation and attributes',lambda a:wire(a,lambda row:row.pop('operation_id'))),('late-id','bounds','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='not-visited'),1)),('max-id','invalid','operation and attributes',lambda a:wire(a,lambda row:row.update(operation_id='x'*254))),('omitted-parent','host-omitted','operation and attributes',lambda a:wire(a,lambda row:row.pop('operation_id')))]
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
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-operation-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-operation-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-operation',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes();require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-operation-native/v1',task='WP06-S02-T02',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()