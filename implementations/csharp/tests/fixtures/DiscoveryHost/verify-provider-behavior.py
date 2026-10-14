"""Independent actual-Core interest/rejection reader; controlled workers are explicit."""
import argparse,base64,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('behavior_base',Path(__file__).with_name('verify-flush-results.py'));base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('filtered','no-outputs','stopped','disposed','all-faulted','partial-fault','full','template-tail','float','duplicates','root-shape','rejection','capture-failure','formatter-failure')
CONTROLLED=('no-outputs','all-faulted','partial-fault','full')


def same(actual,expected):
    if type(actual) is not type(expected):return False
    if isinstance(expected,dict):return actual.keys()==expected.keys() and all(same(actual[k],v) for k,v in expected.items())
    if isinstance(expected,list):return len(actual)==len(expected) and all(same(a,b) for a,b in zip(actual,expected))
    return actual==expected


def check(mode,a,pid=None):
    require(a['mode']==mode and mode in MODES,'mode');require(a['runtime']=='.NET 10.0.12','actual runtime')
    require(type(a['pid']) is int and a['pid']>0 and (pid is None or a['pid']==pid),'actual child PID')
    physical=mode not in CONTROLLED;require(a['physical'] is physical and a['leaseReleased'] is physical,'physical lifetime')
    calls=0 if mode in ('filtered','no-outputs','stopped','disposed') else 2 if mode in ('partial-fault','capture-failure','formatter-failure') else 3 if mode=='full' else 19 if mode=='rejection' else 1
    states=1 if mode in ('all-faulted','capture-failure','formatter-failure') else 2 if mode=='partial-fault' else 3 if mode in ('full','rejection') else 0
    require(type(a['formatterCalls']) is int and a['formatterCalls']==calls,'formatter calls');require(type(a['stateCalls']) is int and a['stateCalls']==states,'state traversal')
    require(a['enabledBefore'] is (mode not in ('filtered','no-outputs')) and a['enabledAtProbe'] is (mode not in ('filtered','no-outputs','stopped','disposed','all-faulted')),'interest gates')
    invalid=19 if mode=='rejection' else 1 if mode=='capture-failure' else 0
    require(a['invalid']==str(invalid) and type(a['rejected']) is int and a['rejected']==invalid,'invalid accounting')
    require(a['truncated']==('2' if mode=='template-tail' else '0'),'truncation accounting');require(a['exceptionIdentity'] is (mode=='formatter-failure'),'formatter exception identity')
    names=[] if mode=='no-outputs' else ['console','file'] if mode=='partial-fault' else ['file' if physical else 'console']
    require([x['name'] for x in a['outputs']]==names,'output selection')
    for out in a['outputs']:
        fault=mode in ('all-faulted','partial-fault') and out['name']=='console'
        accepted=1 if fault else 0 if mode in ('filtered','stopped','disposed') else 2 if mode in ('partial-fault','full','template-tail') else 1
        expected=(accepted,0 if fault else accepted,1 if fault else 0,0,0,1 if mode=='full' else 0,1 if fault else 0,2 if mode=='filtered' else 0)
        require(tuple(number(out[k]) for k in ('accepted','written','failed','abandoned','pending','full','unavailable','filtered'))==expected,'output accounting')
        require(out['fault']==('WriteFailed' if fault else None),'safe fault code')
    result=a['result'];require(set(result)=={'completion','succeeded','loss','captured','observed','outputs'},'result shape')
    require(result['completion']=='Completed' and result['succeeded'] is (mode not in ('all-faulted','partial-fault')) and result['loss'] is (mode in ('all-faulted','partial-fault','full','template-tail','rejection','capture-failure')),'stop summary')
    require([x['name'] for x in result['outputs']]==names,'stop outputs')
    for live,out in zip(a['outputs'],result['outputs']):
        require(tuple(number(out[k]) for k in ('boundary','written','failed','abandoned','pending'))==tuple(number(live[k]) for k in ('accepted','written','failed','abandoned','pending')),'stop boundary')
        require(out['flush']==('Failed' if live['fault'] else 'Succeeded'),'stop flush')
    def rows(data,count):
        require(data[-1:]==b'\n' and not data.startswith(b'\xef\xbb\xbf') if count else data==b'','actual framing')
        lines=data.splitlines();require(len(lines)==count and all(len(line)+1<=16384 for line in lines),'bounded record count');return [load(line) for line in lines]
    count=0 if mode in ('filtered','no-outputs','stopped','disposed','all-faulted','partial-fault') else 2 if mode in ('full','template-tail') else 1
    written=rows(decoded(a['bytes']),count);file=rows(decoded(a['fileBytes']),2 if mode=='partial-fault' else 0)
    for row in written+file:
        require(row['schema_version']==1 and row['status']=='info' and row['component']=='Provider.Behavior' and type(row['attributes']['event_id']) is int and row['attributes']['event_id']==17,'core wire mapping')
        require(row['service']==('CIS.Core.DiscoveryFixture' if physical else 'Console.App'),'core identity')
        if physical:require(row['resource']['process_id']==a['pid'] and row['resource']['runtime_description']=='.NET 10.0.12' and row['resource']['os_family']==a['os'],'automatic process facts')
        require(row.get('truncated') is True if mode=='template-tail' else 'truncated' not in row,'truncation marker')
    if mode in ('full','partial-fault'):require([row['event_name'] for row in (written if mode=='full' else file)]==['first','second'] and all(row['attributes']=={'event_id':17,'value':7} for row in (written if mode=='full' else file)),'accepted prefix')
    if mode=='template-tail':
        require([row['event_name'] for row in written]==['framework','skipped-tail'] and [row.get('message_template') for row in written]==['Items {Items} Count {Count}','bounded {Items}'],'template after budget')
        require(written[0]['message']=='Items '+', '.join(['0']*2000)+' Count 7' and written[1]['message']=='rendered','supplied messages')
        require(all(same(row['attributes'],{'event_id':17,'Items':[0]*1021}) for row in written),'bounded retained prefix')
    if mode=='float':require(type(written[0]['attributes']['number']) is float and written[0]['attributes']=={'event_id':17,'number':0.10000000149011612},'exact Single widening')
    if mode=='duplicates':require(written[0]['message_template']=='last' and written[0]['attributes']=={'event_id':17,'nested':{'same':2}},'last visited duplicates')
    if mode=='root-shape':require(written[0]['attributes']=={'event_id':17},'root shape policy')
    if mode in ('rejection','capture-failure','formatter-failure'):require(written[0]['event_name']=='after-'+('rejections' if mode=='rejection' else 'capture' if mode=='capture-failure' else 'formatter') and written[0]['attributes']=={'event_id':17,'value':7},'no partial admission')


def corruption_controls(cases):
    good={x['id']:x['report'] for x in cases}
    def wire(a,edit):
        rows=[load(line) for line in decoded(a['bytes']).splitlines()];edit(rows);a['bytes']=base64.b64encode(b''.join(json.dumps(row,separators=(',',':')).encode()+b'\n' for row in rows)).decode()
    edits=[('pid','float','actual child PID',lambda a:a.update(pid=a['pid']+1)),('lifetime','float','physical lifetime',lambda a:a.update(leaseReleased=False)),('formatter','filtered','formatter calls',lambda a:a.update(formatterCalls=1)),('copy','stopped','state traversal',lambda a:a.update(stateCalls=1)),('full-interest','full','interest gates',lambda a:a.update(enabledAtProbe=False)),('all-faulted','all-faulted','formatter calls',lambda a:a.update(formatterCalls=2)),('invalid','rejection','invalid accounting',lambda a:a.update(invalid='18')),('truncation','template-tail','truncation accounting',lambda a:a.update(truncated='0')),('exception','formatter-failure','formatter exception identity',lambda a:a.update(exceptionIdentity=False)),('gap','full','output accounting',lambda a:a['outputs'][0].update(accepted='3')),('fault','partial-fault','safe fault code',lambda a:a['outputs'][0].update(fault='private exception')),('stop','full','stop boundary',lambda a:a['result']['outputs'][0].update(boundary='3')),('template','template-tail','template after budget',lambda a:wire(a,lambda rows:rows[0].pop('message_template'))),('prefix','template-tail','bounded retained prefix',lambda a:wire(a,lambda rows:rows[0]['attributes'].update(Count=7))),('prefix-type','template-tail','bounded retained prefix',lambda a:wire(a,lambda rows:rows[0]['attributes']['Items'].__setitem__(0,False))),('float','float','exact Single widening',lambda a:wire(a,lambda rows:rows[0]['attributes'].update(number=0.1))),('duplicates','duplicates','last visited duplicates',lambda a:wire(a,lambda rows:rows[0].update(message_template='first'))),('root','root-shape','root shape policy',lambda a:wire(a,lambda rows:rows[0]['attributes'].update(ignored=7))),('partial','rejection','no partial admission',lambda a:wire(a,lambda rows:rows[0].update(event_name='invalid')))]
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
    repo=Path(__file__).resolve().parents[5];mapping=repo/'conformance/logging/provider-behavior-cases.json';require([x['id'] for x in load(mapping.read_bytes())['cases']]==list(MODES),'shared case membership')
    host,out=Path(args.host).resolve(),Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
    with tempfile.TemporaryDirectory(prefix='cislib-provider-behavior-') as temp:
        for mode in MODES:
            work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
            command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'provider-behavior',mode,str(work)]
            process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:stdout,stderr=process.communicate(timeout=60)
            except BaseException:process.kill();process.communicate(timeout=15);raise
            (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr);require(process.returncode==0 and stderr==b'','child failure '+mode)
            a=load(stdout);check(mode,a,process.pid);require((a['os'],a['coreSha256'],a['fixtureSha256'])==(args.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
            data=(work/'logs \u03a9'/'active.log').read_bytes() if a['physical'] else decoded(a['bytes']);require(data==decoded(a['bytes']),'actual file bytes');(out/(mode+'.file.bin')).write_bytes(data)
            cases.append(dict(id=mode,pid=process.pid,exitCode=0,command=command,report=a,stdoutSha256=digest(stdout),stderrSha256=digest(stderr),fileSha256=digest(data)))
    report=dict(schema='cis.logging.provider-behavior-native/v1',task='WP06-S01-T04',family=args.family,children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(mapping),cases=cases,controls=corruption_controls(cases))
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(report['controls']),passed=True)))


if __name__=='__main__':main()