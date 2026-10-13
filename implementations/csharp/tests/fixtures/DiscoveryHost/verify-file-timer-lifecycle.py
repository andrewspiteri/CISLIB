import argparse,copy,importlib.util,json,os,subprocess,tempfile
from datetime import datetime
from pathlib import Path
spec=importlib.util.spec_from_file_location('timer_lifecycle_records',Path(__file__).with_name('verify-flush-results.py'))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest,decoded,number=base.require,base.load,base.sha,base.digest,base.decoded,base.number
MODES=('close-write','close-flush','close-callback','flush-failure','flush-failure-callback','admission-fault','detached-arm-throw')

def record_names(raw,pid,names,reason):
 rows=base.records(raw,pid)
 require([r['event_name'] for r in rows]==names,reason)
 return rows

def check(mode,a,pid=None):
 require(a['mode']==mode,'mode')
 if pid is not None:require(a['pid']==pid and pid>0,'actual child PID')
 require(a['runtime']=='.NET 10.0.12','actual runtime')
 require(a['sameSharedCompletion'] is True,'shared completion identity')
 require(a['sameDisposal'] is True,'shared disposal identity')
 require(a['sharedJoined'] is True and a['finalControls']==0,'joined cleanup')
 require(a['repeatedClosures']==64,'concurrent repeated closure')
 closing=mode.startswith('close-');physical=mode=='close-callback';callback=mode in ('close-callback','flush-failure-callback');admission=mode=='admission-fault'
 t=a['timers'];h=a['held'];ht=h['timers']
 require(t['periodicDisposed']==1 and t['created']==t['disposed']==(2 if mode.startswith('flush-failure') else 1),'owned timer accounting')
 require((t['active'],t['callbacks'],t['pendingJoins'])==(0,0,0),'final timer join')
 for key,reason in [('suppressedCreation','timer creation flow'),('outsideGate','disposal outside admission gate'),('withoutCaller','disposal execution flow'),('published','published closure before disposal'),('afterCloser','disposal after outer closer')]:
  require(t[key] is True and ht[key] is True,reason)
 require(h['timerDetached'] is True,'early timer detach')
 require((ht['created'],ht['disposed'],ht['periodicDisposed'],ht['active'])==(1,1,1,0),'early timer cancellation')
 require(t['fires']==(0 if mode=='detached-arm-throw' else 2 if mode=='flush-failure-callback' else 1),'no post-cancel periodic work')
 if mode=='detached-arm-throw':
  require(a['startError']=='ObjectDisposedException','detached throwing arm classification')
  require(t['syncDisposes']==0,'no duplicate synchronous disposal')
  require(h['sharedPending'] is False and h['workerPending'] is False,'startup closure joined')
  w=a['writer'];require((w['writes'],w['flushes'],w['maximumIo'])==(0,0,0) and w['order']==[] and decoded(w['bytes'])==b'','no startup I/O')
  return
 if closing or admission:
  require(h['sharedPending'] is True and h['workerPending'] is True,'blocked shared drain')
 if physical:
  require(h['ownershipHeld'] is True and a['ownershipReleased'] is True,'physical ownership join')
  require(h['controls']==0,'closed callback registers no work')
  require((ht['callbacks'],ht['pendingJoins'])==(1,1),'active callback join')
  record_names(decoded(a['fileBytes']),a['pid'],['record.1'],'physical record')
 else:
  w=a['writer'];hw=h['writer'];bw=mode=='close-write';fault=not closing
  require(a['workerFaulted'] is mode.startswith('flush-failure'),'worker failure category')
  require((w['writes'],w['flushes'],w['maximumIo'])==(1,1,1),'actual sequential writer')
  require((hw['writes'],hw['flushes'],hw['maximumIo'])==(1,0 if bw else 1,1),'held actual I/O')
  require(hw['order']==([] if bw else ['write.1']),'held I/O order')
  record_names(decoded(hw['bytes']),a['pid'],[] if bw else ['record.1'],'held file records')
  require(w['order']==(['write.1','flush.1'] if closing or admission else ['write.1']),'actual final I/O order')
  record_names(decoded(w['bytes']),a['pid'],['record.1'],'actual file records')
  require(h['flushPending'] is (closing or admission),'in-flight flush observation')
  require(h['controls']==(1 if closing or admission else 0),'retained flush controls')
  if callback:
   join=a['heldJoin']
   require(h['workerPending'] is True and (join['callbacks'],join['pendingJoins'])==(1,1),'fault callback join')
  hd=h['diagnostics'];require([d['name'] for d in hd]==(['file'] if closing else ['console','file']),'held diagnostic destinations')
  fd=hd[-1]
  require(fd['state']==('Stopping' if closing else 'Faulted'),'held lifecycle state')
  expected=(1,0 if bw else 1,0,0,1 if bw else 0,0,0,0) if closing else (3,1,0,2,0,0,1 if admission else 0,0)
  require(tuple(number(fd[k]) for k in ('accepted','written','failed','abandoned','pending','full','unavailable','filtered'))==expected,'held event accounting')
  require(fd['fault']==(None if closing else 'AdmissionExhausted' if admission else 'FlushFailed'),'held safe fault')
  if not closing:
   try:stamp=datetime.fromisoformat(fd['faultAt'])
   except (TypeError,ValueError):raise AssertionError('held fault UTC observation')
   require(stamp.utcoffset().total_seconds()==0,'held fault UTC observation')
  else:require(fd['faultAt'] is None,'held healthy fault absence')
  if fault:
   c=a['console'];record_names(decoded(c['bytes']),a['pid'],['record.1','record.2','record.3','console.4'],'healthy console continuation')
   require(c['writes']==4 and c['flushes']==(1 if admission else 2) and c['maximumIo']==1,'healthy console I/O')
   require(c['order']==['write.1','write.2','write.3','write.4']+['flush.4']*(1 if admission else 2),'healthy console order')
   if not admission:
    r=a['result'];require(r['completion']=='Completed' and r['succeeded'] is False and r['loss'] is True,'public fault result')
    require(number(r['invalid'])==number(r['truncated'])==0 and len(r['outputs'])==2,'fault result global observations')
    for o,expected in zip(r['outputs'],[('console',4,4,0,0,0,'Succeeded'),('file',3,1,0,2,0,'Failed')]):
     require((o['name'],*(number(o[k]) for k in ('boundary','written','failed','abandoned','pending')),o['flush'])==expected,'fault fixed boundary result')
     require(number(o['full'])==number(o['filtered'])==0 and number(o['unavailable'])==(o['name']=='file'),'fault result rejections')
 names=['file'] if closing else ['console','file']
 require([d['name'] for d in a['diagnostics']]==names,'diagnostic destinations')
 for d in a['diagnostics']:
  file=d['name']=='file';n=1 if closing else 3 if file else 4
  expected=(n,1 if file else n,0,2 if file and not closing else 0,0,0,1 if file and not closing else 0,0)
  require(tuple(number(d[k]) for k in ('accepted','written','failed','abandoned','pending','full','unavailable','filtered'))==expected,'final event accounting')
  require(d['state']=='Stopped','final lifecycle state')
  fault=('AdmissionExhausted' if admission else 'FlushFailed') if file and not closing else None
  require(d['fault']==fault,'safe output fault')
  if fault:
   try:stamp=datetime.fromisoformat(d['faultAt'])
   except (TypeError,ValueError):raise AssertionError('fault UTC observation')
   require(stamp.utcoffset().total_seconds()==0,'fault UTC observation')
  else:require(d['faultAt'] is None,'healthy fault absence')

def corruption_controls(cases):
 samples={c['id']:c['report'] for c in cases}
 edits=[
 ('shared','close-write','shared completion identity',lambda a:a.update(sameSharedCompletion=False)),
 ('disposal','close-write','shared disposal identity',lambda a:a.update(sameDisposal=False)),
 ('joined','close-write','joined cleanup',lambda a:a.update(sharedJoined=False)),
 ('repeat','close-write','concurrent repeated closure',lambda a:a.update(repeatedClosures=63)),
 ('timer-count','close-write','owned timer accounting',lambda a:a['timers'].update(periodicDisposed=2)),
 ('callback','close-callback','final timer join',lambda a:a['timers'].update(callbacks=1)),
 ('gate','close-write','disposal outside admission gate',lambda a:a['held']['timers'].update(outsideGate=False)),
 ('flow','close-write','disposal execution flow',lambda a:a['timers'].update(withoutCaller=False)),
 ('publication','admission-fault','published closure before disposal',lambda a:a['timers'].update(published=False)),
 ('detach','close-flush','early timer detach',lambda a:a['held'].update(timerDetached=False)),
 ('cancel','close-flush','early timer cancellation',lambda a:a['held']['timers'].update(active=1)),
 ('restart','close-write','no post-cancel periodic work',lambda a:a['timers'].update(fires=2)),
 ('drain','close-write','blocked shared drain',lambda a:a['held'].update(sharedPending=False)),
 ('ownership','close-callback','physical ownership join',lambda a:a['held'].update(ownershipHeld=False)),
 ('join','close-callback','active callback join',lambda a:a['held']['timers'].update(pendingJoins=0)),
 ('overlap','close-write','actual sequential writer',lambda a:a['writer'].update(maximumIo=2)),
 ('flush','admission-fault','in-flight flush observation',lambda a:a['held'].update(flushPending=False)),
 ('continued','flush-failure','healthy console continuation',lambda a:a['console'].update(bytes=a['writer']['bytes'])),
 ('result','flush-failure','public fault result',lambda a:a['result'].update(succeeded=True)),
 ('accounting','admission-fault','final event accounting',lambda a:a['diagnostics'][1].update(abandoned='0')),
 ('fault','flush-failure','safe output fault',lambda a:a['diagnostics'][1].update(fault='private controlled flush failure')),
 ('outer','close-write','disposal after outer closer',lambda a:a['held']['timers'].update(afterCloser=False)),
 ('held-accounting','admission-fault','held event accounting',lambda a:a['held']['diagnostics'][1].update(abandoned='0')),
 ('held-fault','flush-failure','held safe fault',lambda a:a['held']['diagnostics'][1].update(fault='FlushFailedX')),
 ('held-utc','flush-failure','held fault UTC observation',lambda a:a['held']['diagnostics'][1].update(faultAt='2026-10-13T19:20:21+01:00')),
 ('held-state','close-write','held lifecycle state',lambda a:a['held']['diagnostics'][0].update(state='Stopped')),
 ('double-dispose','detached-arm-throw','no duplicate synchronous disposal',lambda a:a['timers'].update(syncDisposes=1)),
 ('arm-error','detached-arm-throw','detached throwing arm classification',lambda a:a.update(startError=None)),
 ('pid','close-write','actual child PID',lambda a:a.update(pid=a['pid']+1))]
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
 repo=Path(__file__).resolve().parents[5];mapping=load((repo/'conformance/logging/file-timer-lifecycle-cases.json').read_bytes())
 require([c['id'] for c in mapping['cases']]==list(MODES),'shared case membership')
 host=Path(a.host).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
 with tempfile.TemporaryDirectory(prefix='cislib-file-timer-lifecycle-') as temp:
  for mode in MODES:
   work=Path(temp)/mode;work.mkdir()
   env={k:v for k,v in os.environ.items() if not k.startswith('CORE_')}
   command=[a.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-timer-lifecycle',mode,str(work)]
   process=subprocess.Popen(command,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE);stdout=stderr=b''
   try:
    try:stdout,stderr=process.communicate(timeout=60)
    except BaseException:process.kill();stdout,stderr=process.communicate(timeout=15);raise
   finally:
    (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
   require(process.returncode==0 and stderr==b'','child failure '+mode)
   report=load(stdout);check(mode,report,process.pid)
   require((report['os'],report['coreSha256'],report['fixtureSha256'])==(a.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
   if mode=='close-callback':
    raw=(work/'owned/events.jsonl').read_bytes();require(raw==decoded(report['fileBytes']),'physical active bytes');(out/'close-callback.active.bin').write_bytes(raw)
   cases.append(dict(id=mode,pid=process.pid,command=command,exitCode=0,report=report,stdoutSha256=digest(stdout),stderrSha256=digest(stderr)))
 report=dict(schema='cis.logging.file-timer-lifecycle-native/v1',task='WP05-S09-T03',family=a.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')),caseMappingSha256=sha(repo/'conformance/logging/file-timer-lifecycle-cases.json'),cases=cases,controls=corruption_controls(cases))
 (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
 print(json.dumps(dict(family=a.family,children=len(cases),controls=len(report['controls']),passed=True)))
if __name__=='__main__':main()
