"""Independent actual-Core Microsoft provider foundation reader."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('provider_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('forward','extensions','categories','filtered','nonowning','stopped','typed-scope','formatter-error')


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime')
    require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    require(a['physical'] is True and a['leaseReleased'] is True and a['borrowedConsoleOpen'] is True,'physical lifetime')
    require(a['invalid']=='0','core invalid count')
    require((a['sharedRuntimeLiveAfterProviderDispose'],a['formatterErrorPreserved'],a['scopeCopied'])==(mode=='nonowning',mode=='formatter-error',mode=='typed-scope'),'provider ownership/error/scope facts')
    require(a['formatterExceptionReceived'] is (mode=='forward'),'formatter exception identity')
    count=0 if mode in ('filtered','stopped','formatter-error') else 2 if mode in ('categories','nonowning') else 1
    calls=1 if mode in ('forward','nonowning','typed-scope','formatter-error') else 2 if mode=='categories' else 0
    require(type(a['formatterCalls']) is int and a['formatterCalls']==calls,'deferred formatter calls')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape')
    require((result['completion'],result['succeeded'],result['loss'])==('Completed',True,False),'stop summary')
    require(len(result['outputs'])==1 and result['outputs'][0]['name']=='file','file output');out=result['outputs'][0]
    require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==(count,count,0,0,0) and out['flush']=='Succeeded','core delivery')
    data=decoded(a['bytes']);require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf') if count else data==b'','actual framing')
    lines=data.splitlines();require(len(lines)==count and all(len(line)+1<=16384 for line in lines),'actual bounded records')
    rows=[load(line) for line in lines]
    names=['first','second'] if mode=='categories' else ['direct','other'] if mode=='nonowning' else ['log.message'] if mode=='extensions' else ['scoped'] if mode=='typed-scope' else ['forward'] if count else []
    components=['Orders.First','Orders.Second'] if mode=='categories' else ['Direct','Other.Provider'] if mode=='nonowning' else ['Provider.Foundation'] if count else []
    require([x['event_name'] for x in rows]==names,'event names');require([x['component'] for x in rows]==components,'category components')
    require(all(x['schema_version']==1 and x['status']==('error' if mode=='forward' else 'info') and x['service']=='CIS.Core.DiscoveryFixture' and x['resource']['process_id']==a['pid'] and x['resource']['runtime_description']=='.NET 10.0.12' and x['resource']['os_family']==a['os'] for x in rows),'automatic core facts')
    if mode=='forward':
        require(rows[0]['message']=='\u03a9 \U0001f642','rendered message')
        error=rows[0]['exception'];require(error['type']=='System.InvalidOperationException','exception type');require(error['message']=='controlled provider failure','exception message')
        require(set(error)<= {'type','message','stack_trace','inner_exceptions','truncated'} and 'private_data' not in json.dumps(rows[0]) and 'must not be projected' not in json.dumps(rows[0]),'exception excludes Data')
    if mode=='extensions':require(rows[0]['message']=='Orders 3','framework formatter')
    if mode=='typed-scope':require(rows[0]['operation_id']=='operation.original' and rows[0]['attributes']['scope_attribute']=='original','copied typed scope')


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def record_change(a,edit):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows[0]);a['bytes']=base64.b64encode(b''.join(json.dumps(r,ensure_ascii=False).encode()+b'\n' for r in rows)).decode()
    edits=[
      ('pid','forward','actual child PID',lambda a:a.update(pid=a['pid']+1)),
      ('exception-identity','forward','formatter exception identity',lambda a:a.update(formatterExceptionReceived=False)),
      ('exception-type','forward','exception type',lambda a:record_change(a,lambda r:r['exception'].update(type='wrong'))),
      ('exception-message','forward','exception message',lambda a:record_change(a,lambda r:r['exception'].update(message='wrong'))),
      ('exception-data','forward','exception excludes Data',lambda a:record_change(a,lambda r:r['exception'].update(Data={'private_data':'must not be projected'}))),
      ('runtime','forward','actual runtime',lambda a:a.update(runtime='.NET 10.0.13')),
      ('lease','forward','physical lifetime',lambda a:a.update(leaseReleased=False)),
      ('invalid','forward','core invalid count',lambda a:a.update(invalid='1')),
      ('owner','nonowning','provider ownership/error/scope facts',lambda a:a.update(sharedRuntimeLiveAfterProviderDispose=False)),
      ('formatter-error','formatter-error','provider ownership/error/scope facts',lambda a:a.update(formatterErrorPreserved=False)),
      ('scope-copy','typed-scope','provider ownership/error/scope facts',lambda a:a.update(scopeCopied=False)),
      ('filtered','filtered','deferred formatter calls',lambda a:a.update(formatterCalls=1)),
      ('stopped','stopped','deferred formatter calls',lambda a:a.update(formatterCalls=1)),
      ('shape','forward','result shape',lambda a:a['result'].update(extra=True)),
      ('summary','forward','stop summary',lambda a:a['result'].update(succeeded=False)),
      ('ticket','forward','core delivery',lambda a:a['result']['outputs'][0].update(boundary='0')),
      ('framing','forward','actual framing',lambda a:a.update(bytes=base64.b64encode(decoded(a['bytes'])[:-1]).decode())),
      ('names','forward','event names',lambda a:record_change(a,lambda r:r.update(event_name='other'))),
      ('category','categories','category components',lambda a:record_change(a,lambda r:r.update(component='wrong'))),
      ('facts','forward','automatic core facts',lambda a:record_change(a,lambda r:r['resource'].update(process_id=0))),
      ('message','forward','rendered message',lambda a:record_change(a,lambda r:r.update(message='wrong'))),
      ('extension','extensions','framework formatter',lambda a:record_change(a,lambda r:r.update(message='Orders {Count}'))),
      ('scope-value','typed-scope','copied typed scope',lambda a:record_change(a,lambda r:r['attributes'].update(scope_attribute='changed'))),
      ('operation-id','typed-scope','copied typed scope',lambda a:record_change(a,lambda r:r.update(operation_id='changed')))
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
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-foundation-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-foundation-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-foundation',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes();require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-foundation-native/v1',task='WP06-S01-T01',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()