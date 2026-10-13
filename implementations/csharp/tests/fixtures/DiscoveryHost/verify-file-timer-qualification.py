import argparse,copy,importlib.util,json,os,subprocess,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('file_timer_qualification_records',Path(__file__).with_name('verify-flush-results.py'))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('stopped-rearm','faulted-rearm')

def check(mode,a,pid=None):
 require(a['mode']==mode and a['runtime']=='.NET 10.0.12','runtime mode')
 if pid is not None:require(a['pid']==pid and pid>0,'actual child PID')
 fault=mode=='faulted-rearm'
 require(a['sameWorker'] is True and a['sameSharedCompletion'] is True and a['sameDisposal'] is True,'original task identities')
 require(a['workerFaulted'] is fault and a['sharedFaulted'] is fault,'actual terminal worker')
 require(a['finalControls']==0,'no terminal controls')
 before,after=a['before'],a['after'];require(before==after,'stable terminal diagnostic')
 require(before['state']==('Faulted' if fault else 'Stopped'),'terminal state')
 require(before['fault']==('FlushFailed' if fault else None),'safe terminal fault')
 require(tuple(number(before[k]) for k in ('accepted','written','failed','abandoned','pending','unavailable'))==(1,1,0,0,0,0),'terminal event accounting')
 probes=a['probes'];require(len(probes)==64,'restart probe count')
 for index,p in enumerate(probes):
  require((p['created'],p['disposed'],p['active'],p['fires'],p['controls'])==(index+2,index+2,0,1,0),'inactive restart probes')
 t=a['timers'];bt=a['beforeTimers']
 require((bt['created'],bt['disposed'],bt['active'],bt['periodicFires'])==(1,1,0,1),'original timer disposal')
 require((t['created'],t['disposed'],t['active'],t['activePeriodic'],t['periodicFires'],t['deadlineFires'],t['maximumActive'])==(65,65,0,0,1,0,1),'final timer work')
 require(t['periodicSuppressed'] is True,'suppressed periodic creation')
 w=a['writer'];require((w['writes'],w['flushes'],w['maximumIo'])==(1,1,1),'original sequential I/O')
 require(w['order']==(['write.1'] if fault else ['write.1','flush.1']),'actual I/O order')
 rows=base.records(decoded(w['bytes']),a['pid']);require([r['event_name'] for r in rows]==['one'],'actual original record')
 require((a['lateOutcome'],a['lateOutput'])==(('Rejected','Faulted') if fault else ('Stopped','Stopped')),'late admission outcome')
 late=a['afterLate'];expected=dict(before);expected['unavailable']='1' if fault else '0';require(late==expected,'late admission accounting')

def corruption_controls(cases):
 samples={c['id']:c['report'] for c in cases}
 def counters(a):
  a['before']['written']=a['after']['written']='0'
 edits=[
 ('pid','stopped-rearm','actual child PID',lambda a:a.update(pid=a['pid']+1)),
 ('task','stopped-rearm','original task identities',lambda a:a.update(sameDisposal=False)),
 ('fault-task','faulted-rearm','actual terminal worker',lambda a:a.update(workerFaulted=False)),
 ('control','stopped-rearm','no terminal controls',lambda a:a.update(finalControls=1)),
 ('snapshot','faulted-rearm','stable terminal diagnostic',lambda a:a['after'].update(fault=None)),
 ('state','faulted-rearm','terminal state',lambda a:(a['before'].update(state='Stopped'),a['after'].update(state='Stopped'))),
 ('fault','faulted-rearm','safe terminal fault',lambda a:(a['before'].update(fault='private error'),a['after'].update(fault='private error'))),
 ('counters','stopped-rearm','terminal event accounting',counters),
 ('probes','stopped-rearm','restart probe count',lambda a:a['probes'].pop()),
 ('restart','faulted-rearm','inactive restart probes',lambda a:a['probes'][0].update(active=1)),
 ('timer','stopped-rearm','original timer disposal',lambda a:a['beforeTimers'].update(disposed=0)),
 ('tick','faulted-rearm','final timer work',lambda a:a['timers'].update(periodicFires=2)),
 ('flow','stopped-rearm','suppressed periodic creation',lambda a:a['timers'].update(periodicSuppressed=False)),
 ('io','faulted-rearm','original sequential I/O',lambda a:a['writer'].update(flushes=2)),
 ('order','stopped-rearm','actual I/O order',lambda a:a['writer']['order'].reverse()),
 ('records','faulted-rearm','actual original record',lambda a:a['writer'].update(bytes='')),
 ('late','faulted-rearm','late admission outcome',lambda a:a.update(lateOutcome='Accepted')),
 ('late-count','stopped-rearm','late admission accounting',lambda a:a['afterLate'].update(accepted='2'))]
 result=[]
 for identity,mode,reason,edit in edits:
  bad=copy.deepcopy(samples[mode]);edit(bad)
  try:check(mode,bad,samples[mode]['pid'])
  except AssertionError as error:
   require(str(error)==reason,'unexpected corruption '+identity+': '+str(error));result.append(dict(id=identity,rejected=True,reason=reason))
  else:raise AssertionError('corruption accepted '+identity)
 return result

def main():
 p=argparse.ArgumentParser()
 for key in ('dotnet','host','output','family'):p.add_argument('--'+key,required=True)
 a=p.parse_args();require((os.name=='nt')==(a.family=='windows'),'native OS')
 repo=Path(__file__).resolve().parents[5];mapping=load((repo/'conformance/logging/file-timer-qualification-cases.json').read_bytes())
 require([c['id'] for c in mapping['cases']]==list(MODES),'shared case membership')
 host=Path(a.host).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
 with tempfile.TemporaryDirectory(prefix='cislib-file-timer-qualification-') as temp:
  for mode in MODES:
   work=Path(temp)/mode;work.mkdir();env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
   command=[a.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-timer-qualification',mode,str(work)]
   process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout=stderr=b''
   try:
    try:stdout,stderr=process.communicate(timeout=60)
    except BaseException:process.kill();stdout,stderr=process.communicate(timeout=15);raise
   finally:
    (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
   require(process.returncode==0 and stderr==b'','child failure '+mode);report=load(stdout);check(mode,report,process.pid)
   require((report['os'],report['coreSha256'],report['fixtureSha256'])==(a.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
   cases.append(dict(id=mode,pid=process.pid,command=command,exitCode=0,report=report,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
 report=dict(schema='cis.logging.file-timer-qualification-native/v1',task='WP05-S09-T04',family=a.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(repo/'conformance/logging/file-timer-qualification-cases.json'),cases=cases,controls=corruption_controls(cases))
 (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
 print(json.dumps(dict(family=a.family,children=len(cases),controls=len(report['controls']),passed=True)))
if __name__=='__main__':main()
