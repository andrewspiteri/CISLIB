"""Independent T01 native interval reader; actual Core child processes, not a logging simulator."""
import argparse,base64,copy,hashlib,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location("flush_results_base",Path(__file__).with_name("verify-flush-results.py"))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
load=base.load;decoded=base.decoded
MODES=("idle-min","idle-default","idle-max","configured","console-only","file-none","create-failure","arm-failure","below","above","env")
digest=lambda b:hashlib.sha256(b).hexdigest()
sha=lambda p:digest(Path(p).read_bytes())
def require(value,reason):
 if not value:raise AssertionError(reason)
def check(mode,a,pid=None):
 require(a['mode']==mode and a['runtime']=='.NET 10.0.12','actual runtime/mode')
 if pid is not None:require(a['pid']==pid and pid>0,'actual child PID')
 t=a['timers']
 require(t['active']==0 and t['created']==t['disposed'],'timer cleanup')
 if t['created']>0:require(t['flowSuppressed'] is True,'timer context flow')
 invalid=mode in ('below','above');failure=mode.endswith('-failure');disabled=mode in ('console-only','file-none')
 if invalid:
  require((a['errorType'],a['safeCode'],a['workers'],a['selections'],t['attempts'],t['created'],t['fires'])==('ArgumentOutOfRangeException',None,0,0,0,0,0),'before acquisition')
  require(a['noOutputBeforeFailure'] is True and decoded(a['fileBytes'])==b'','no startup output')
  return
 if failure:
  require(a['errorType']=='CoreLogInitializationException' and a['safeCode']=='FileFlushTimerInitializationFailed','safe initialization category')
  require(a['workers']==2 and a['selections']==1 and a['joined'] is True,'startup workers joined')
  require(a['borrowedOpen'] is True,'borrowed stream ownership')
  require(a['ownershipReleased'] is True,'file ownership release')
  require(a['noOutputBeforeFailure'] is True and decoded(a['fileBytes'])==b'','no startup output')
  require((t['attempts'],t['created'],t['disposed'],t['fires'],t['period'])==(1,0 if mode=='create-failure' else 1,0 if mode=='create-failure' else 1,0,0),'timer acquisition rollback')
  return
 interval={'idle-min':10,'idle-default':1000,'idle-max':60000,'configured':500,'env':250}.get(mode)
 require((t['attempts'],t['created'],t['disposed'],t['fires'])==((0,0,0,0) if disabled else (1,1,1,3 if mode.startswith('idle-') else 2)),'periodic timer ownership')
 require(a['interval']==interval and t['period']==(interval or 0),'frozen configured interval')
 require(a['targets']==([] if disabled else ['1','1']),'idle fixed boundary')
 require(a['beforeDue'] is True and a['idleNoNewEvents'] is True,'idle scheduling facts')
 d=a['diagnostics']
 require(len(d)==1 and d[0]['name']==('console' if disabled else 'file'),'diagnostic destination')
 require(tuple(d[0][k] for k in ('accepted','written','failed','abandoned','pending','full','unavailable','filtered'))==('1','1','0','0','0','0','0','0') and d[0]['fault'] is None,'actual event counts')
 if mode.startswith('idle-'):
  w=a['writer'];raw=decoded(w['bytes'])
  require((w['writes'],w['flushes'],w['maximumIo'])==(1,2,1),'actual writer calls')
  require(w['order']==['write.1','flush.1','flush.1'],'serialized periodic order')
 else:
  require(a['ownershipReleased'] is True,'file ownership release')
  require(a['borrowedOpen'] is True,'borrowed stream ownership')
  require(a['optionMutationIgnored'] is True,'frozen option mutation')
  raw=decoded(a['bytes'])
 rows=base.records(raw,a['pid'])
 require([row['event_name'] for row in rows]==['one'],'actual record order')
def corruption_controls(cases):
 samples={c['id']:c['report'] for c in cases};controls=[]
 edits=[
 ('timer','idle-default','periodic timer ownership',lambda a:a['timers'].update(created=0,disposed=0)),
 ('closed','idle-default','periodic timer ownership',lambda a:a['timers'].update(fires=2)),
 ('extra','idle-default','actual writer calls',lambda a:a['writer'].update(flushes=3)),
 ('launch','below','before acquisition',lambda a:a.update(workers=1)),
 ('active','idle-default','timer cleanup',lambda a:a['timers'].update(active=1)),
 ('flow','idle-default','timer context flow',lambda a:a['timers'].update(flowSuppressed=False)),
 ('interval','configured','frozen configured interval',lambda a:a.update(interval=60000)),
 ('environment','env','frozen configured interval',lambda a:a['timers'].update(period=1000)),
 ('boundary','idle-default','idle fixed boundary',lambda a:a['targets'].__setitem__(1,'2')),
 ('early','idle-default','idle scheduling facts',lambda a:a.update(beforeDue=False)),
 ('idle','configured','idle scheduling facts',lambda a:a.update(idleNoNewEvents=False)),
 ('counts','configured','actual event counts',lambda a:a['diagnostics'][0].update(written='0')),
 ('order','idle-default','serialized periodic order',lambda a:a['writer']['order'].__setitem__(2,'flush.2')),
 ('records','configured','actual record order',lambda a:a.update(bytes='')),
 ('ownership','arm-failure','file ownership release',lambda a:a.update(ownershipReleased=False)),
 ('borrowed','create-failure','borrowed stream ownership',lambda a:a.update(borrowedOpen=False)),
 ('joined','arm-failure','startup workers joined',lambda a:a.update(joined=False)),
 ('category','create-failure','safe initialization category',lambda a:a.update(safeCode='Unsafe')),
 ('range','below','before acquisition',lambda a:a.update(selections=1)),
 ('pid','idle-default','actual child PID',lambda a:a.update(pid=a['pid']+1))]
 for identity,mode,reason,edit in edits:
  bad=copy.deepcopy(samples[mode]);edit(bad)
  try:check(mode,bad,samples[mode]['pid'])
  except AssertionError as error:
   require(str(error)==reason,'unexpected corruption '+identity+': '+str(error));controls.append(dict(id=identity,rejected=True,reason=reason))
  else:raise AssertionError('corruption accepted '+identity)
 return controls
def main():
 p=argparse.ArgumentParser()
 for key in ('dotnet','host','output','family'):p.add_argument('--'+key,required=True)
 a=p.parse_args();require((os.name=='nt')==(a.family=='windows'),'native OS')
 repo=Path(__file__).resolve().parents[5];mapping=load((repo/'conformance/logging/file-interval-cases.json').read_bytes())
 require([c['id'] for c in mapping['cases']]==list(MODES),'shared case membership')
 host=Path(a.host).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
 with tempfile.TemporaryDirectory(prefix='cislib-file-interval-') as temp:
  for mode in MODES:
   work=Path(temp)/mode;work.mkdir()
   env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
   if mode=='env':env.update(CORE_LOG_CONSOLE_LEVEL='NONE',CORE_LOG_FILE_LEVEL='INFO',CORE_LOG_FILE_PATH=str(work/'owned/events.jsonl'),CORE_LOG_FLUSH_INTERVAL_MS='250')
   command=[a.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-interval',mode,str(work)]
   process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout=stderr=b''
   try:
    try:stdout,stderr=process.communicate(timeout=60)
    except BaseException:process.kill();stdout,stderr=process.communicate(timeout=15);raise
   finally:
    (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
   require(process.returncode==0 and stderr==b'','child failure '+mode)
   report=load(stdout);check(mode,report,process.pid)
   require((report['os'],report['coreSha256'],report['fixtureSha256'])==(a.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
   cases.append(dict(id=mode,pid=process.pid,command=command,exitCode=0,report=report,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
 controls=corruption_controls(cases)
 report=dict(schema='cis.logging.file-interval-native/v1',task='WP05-S09-T01',family=a.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(repo/'conformance/logging/file-interval-cases.json'),cases=cases,controls=controls)
 (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
 print(json.dumps(dict(family=a.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
