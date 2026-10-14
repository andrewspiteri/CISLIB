"""Independent actual-Core Microsoft provider foundation reader."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('provider_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('levels','names','ids','payload')


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime')
    require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    require(a['physical'] is True and a['leaseReleased'] is True and a['borrowedConsoleOpen'] is True,'physical lifetime');require(a['invalid']=='0','core invalid count')
    pairs={'levels':6,'names':3,'ids':5,'payload':1}[mode]
    require(type(a['formatterCalls']) is int and a['formatterCalls']==pairs,'supplied formatter calls')
    require(type(a['noneFormatterCalls']) is int and a['noneFormatterCalls']==0,'None formatter not called')
    require(a['formatterExceptionReceived'] is (mode=='payload'),'formatter exception identity')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape');require((result['completion'],result['succeeded'],result['loss'])==('Completed',True,False),'stop summary')
    require(len(result['outputs'])==1 and result['outputs'][0]['name']=='file','file output');out=result['outputs'][0]
    require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==(pairs*2,pairs*2,0,0,0) and out['flush']=='Succeeded','core delivery')
    data=decoded(a['bytes']);require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf'),'actual framing');lines=data.splitlines();require(len(lines)==pairs*2 and all(len(line)+1<=16384 for line in lines),'actual bounded records')
    rows=[load(line) for line in lines]
    statuses=['trace','debug','info','warn','error','critical'] if mode=='levels' else ['error' if mode=='payload' else 'info']*pairs
    ids={'levels':[17]*6,'names':[9,-4,0],'ids':[0,1,-1,-2147483648,2147483647],'payload':[42]}[mode]
    categories=['Map.First','Map.Second','Map.Empty'] if mode=='names' else ['Map.'+{'levels':'Levels','ids':'Ids','payload':'Payload'}[mode]]*pairs
    names={'levels':['level']*6,'names':['named.event','log.message','log.message'],'ids':['id']*5,'payload':['payload']}[mode]
    messages={'levels':['level '+str(i) for i in range(6)],'names':['named','absent','empty'],'ids':['0','1','-1','-2147483648','2147483647'],'payload':['rendered {x} \u03a9 \U0001f642']}[mode]
    for i in range(pairs):
        for row in rows[i*2:i*2+2]:
            require(row['status']==statuses[i],'mapped severity');require(row['component']==categories[i],'category component');require(row['event_name']==names[i],'mapped event name');require(row['message']==messages[i],'supplied rendered message')
            require(set(row['attributes'])=={'event_id'} and type(row['attributes']['event_id']) is int and row['attributes']['event_id']==ids[i],'numeric event ID')
            require(row['schema_version']==1 and row['service']=='CIS.Core.DiscoveryFixture' and row['resource']['process_id']==a['pid'] and row['resource']['runtime_description']=='.NET 10.0.12' and row['resource']['os_family']==a['os'],'automatic core facts')
            if mode=='payload':
                error=row['exception'];require(error['type']=='System.InvalidOperationException','exception type');require(error['message']=='controlled mapping failure','exception message');require(set(error)=={'type','message'},'exception excludes Data')
            else:require('exception' not in row,'no fabricated exception')
        require({k:v for k,v in rows[i*2].items() if k!='timestamp'}=={k:v for k,v in rows[i*2+1].items() if k!='timestamp'},'equivalent core record')


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def record_change(a,edit,index=0):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows[index]);a['bytes']=base64.b64encode(b''.join(json.dumps(r,ensure_ascii=False).encode()+b'\n' for r in rows)).decode()
    edits=[
      ('pid','levels','actual child PID',lambda a:a.update(pid=a['pid']+1)),
      ('runtime','levels','actual runtime',lambda a:a.update(runtime='.NET 10.0.13')),
      ('lease','levels','physical lifetime',lambda a:a.update(leaseReleased=False)),
      ('invalid','levels','core invalid count',lambda a:a.update(invalid='1')),
      ('formatter','levels','supplied formatter calls',lambda a:a.update(formatterCalls=0)),
      ('none','levels','None formatter not called',lambda a:a.update(noneFormatterCalls=1)),
      ('exception-identity','payload','formatter exception identity',lambda a:a.update(formatterExceptionReceived=False)),
      ('shape','levels','result shape',lambda a:a['result'].update(extra=True)),
      ('summary','levels','stop summary',lambda a:a['result'].update(succeeded=False)),
      ('ticket','levels','core delivery',lambda a:a['result']['outputs'][0].update(boundary='0')),
      ('framing','levels','actual framing',lambda a:a.update(bytes=base64.b64encode(decoded(a['bytes'])[:-1]).decode())),
      ('severity','levels','mapped severity',lambda a:record_change(a,lambda row:row.update(status='info'))),
      ('category','names','category component',lambda a:record_change(a,lambda row:row.update(component='wrong'))),
      ('name','names','mapped event name',lambda a:record_change(a,lambda row:row.update(event_name='wrong'))),
      ('message','payload','supplied rendered message',lambda a:record_change(a,lambda row:row.update(message='unrendered input'))),
      ('event-id-value','ids','numeric event ID',lambda a:record_change(a,lambda row:row['attributes'].update(event_id=99))),
      ('event-id-type','ids','numeric event ID',lambda a:record_change(a,lambda row:row['attributes'].update(event_id='0'))),
      ('facts','levels','automatic core facts',lambda a:record_change(a,lambda row:row['resource'].update(process_id=0))),
      ('exception-type','payload','exception type',lambda a:record_change(a,lambda row:row['exception'].update(type='wrong'))),
      ('exception-message','payload','exception message',lambda a:record_change(a,lambda row:row['exception'].update(message='wrong'))),
      ('exception-data','payload','exception excludes Data',lambda a:record_change(a,lambda row:row['exception'].update(Data={'private_data':'must not be projected'}))),
      ('direct-equivalence','payload','equivalent core record',lambda a:record_change(a,lambda row:row.update(env='changed'),1))
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
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-mapping-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-mapping-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-mapping',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes();require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-mapping-native/v1',task='WP06-S01-T02',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()