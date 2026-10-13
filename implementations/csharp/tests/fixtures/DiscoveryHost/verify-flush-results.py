import argparse,base64,copy,hashlib,json,os,re,subprocess,tempfile
from datetime import datetime
from pathlib import Path
MODES=('empty','filtered','closed','physical','write-timeout','flush-timeout','cancel','pre-cancel','loss','write-failure','flush-failure','later-failure','exhaustion','clock','dual','long-timeout','system-timeout')
def require(value,reason):
 if not value:raise AssertionError(reason)
def pairs(items):
 d={}
 for k,v in items:require(k not in d,'duplicate key');d[k]=v
 return d
def load(raw):return json.loads(raw.decode('utf-8-sig'),object_pairs_hook=pairs,parse_constant=lambda x:(_ for _ in ()).throw(AssertionError('nonfinite JSON')))
def digest(raw):return hashlib.sha256(raw).hexdigest()
def sha(p):return digest(Path(p).read_bytes())
def decoded(s):return base64.b64decode(s,validate=True)
def number(s):
 require(isinstance(s,str) and re.fullmatch('0|[1-9][0-9]*',s) is not None,'exact counter')
 v=int(s);require(v<=2**63-1,'counter range');return v
def records(raw,pid):
 require(not raw.startswith(b'\xef\xbb\xbf') and (not raw or raw.endswith(b'\n')) and b'\r' not in raw,'record framing')
 result=[load(line) for line in raw.splitlines()]
 for row in result:
  require(row['attributes']['producer_pid']==pid,'wire PID')
  require(row['message']=='Ω 🙂' and row['status']=='info','actual event payload')
 return result
def check(mode,a,pid=None):
 require(a['mode']==mode,'mode')
 if pid is not None:require(a['pid']==pid and pid>0,'actual child PID')
 require(a['runtime']=='.NET 10.0.12','actual runtime')
 require(a['immutableAfterIo'] is True,'owned observation')
 cancelled=mode in ('cancel','pre-cancel','long-timeout')
 require(a['cancelled'] is cancelled,'caller cancellation')
 if mode!='physical':
  require(a['timers']['created']==a['timers']['disposed'],'timer ownership')
  require(a['heldControls']==(0 if mode in ('empty','closed') else 1),'registered control')
  # This field is the console gauge; dual's console settled while file stayed pending.
  require(a['controlsAfterWait']==(0 if mode in ('empty','loss','write-failure','flush-failure','later-failure','exhaustion','filtered','closed','dual') else 1),'retained control')
 if cancelled:require('result' not in a,'cancelled result')
 else:
  r=a['result'];outputs=r['outputs']
  for label in ('captured','observed'):
   try:stamp=datetime.fromisoformat(r[label])
   except ValueError:raise AssertionError('UTC labels')
   require(stamp.utcoffset().total_seconds()==0,'UTC labels')
  expected_names=[] if mode=='empty' else ['console','file'] if mode in ('physical','dual') else ['console']
  require([o['name'] for o in outputs]==expected_names,'enabled output order')
  require((number(r['invalid']),number(r['truncated']))==((1,1) if mode=='loss' else (0,0)),'initial global facts')
  for o in outputs:
   b,w,f,ab,p=[number(o[k]) for k in ('boundary','written','failed','abandoned','pending')]
   require(b==w+f+ab+p,'boundary identity')
   require(o['flushSucceeded'] is (o['flush']=='Succeeded'),'writer flush flag')
   expected={
    'filtered':(0,0,0,0,0,'Succeeded'),'closed':(0,0,0,0,0,'Failed'),
    'physical':(2,2,0,0,0,'Succeeded'),'loss':(2,2,0,0,0,'Succeeded'),
    'write-failure':(2,0,1,1,0,'Failed'),'flush-failure':(1,1,0,0,0,'Failed'),
    'later-failure':(1,1,0,0,0,'Succeeded'),
    'exhaustion':(2**63-1,2**63-3,0,1,1,'Failed'),
    'flush-timeout':(1,1,0,0,0,'Pending')
   }.get(mode,(1,0,0,0,1,'Pending'))
   if mode=='dual' and o['name']=='console':expected=(1,1,0,0,0,'Succeeded')
   require((b,w,f,ab,p,o['flush'])==expected,'covered dispositions')
   require(tuple(number(o[k]) for k in ('filtered','full','unavailable'))==((1,2,0) if mode=='loss' else (1,0,0) if mode=='filtered' else (0,0,0)),'initial rejection facts')
  completed=all(number(o['pending'])==0 and o['flush'] in ('Succeeded','Failed') for o in outputs)
  succeeded=all(number(o['pending'])==number(o['failed'])==number(o['abandoned'])==0 and o['flush']=='Succeeded' for o in outputs)
  loss=number(r['invalid'])>0 or number(r['truncated'])>0 or any(number(o['failed'])>0 or number(o['abandoned'])>0 or number(o['full'])>0 or number(o['unavailable'])>0 or o['flush']=='Failed' for o in outputs)
  require((r['completion'],r['succeeded'],r['loss'])==('Completed' if completed else 'TimedOut',succeeded,loss),'summary flags')
 if mode=='clock':require(a['survivedUtcCorrections'] is True,'clock corrections')
 if mode=='long-timeout':require(a['longTimerPhase']==dict(created=1,disposed=0,fires=1,nextDueTicks='1728000000000'),'long timer slicing')
 if mode=='exhaustion':require(a['stillPendingAfterFault'] is True,'unresolved in-flight write')
 if mode=='physical':require(a['leaseReleased'] is True,'physical ownership')
 expected_names=[] if mode in ('empty','write-failure','filtered','closed') else ['first','second'] if mode in ('loss','physical') else ['first']
 console=records(decoded(a['bytes']),a['pid']);file=records(decoded(a['fileBytes']),a['pid'])
 require([row['event_name'] for row in console]==expected_names,'actual console records')
 require([row['event_name'] for row in file]==(['first','second'] if mode=='physical' else ['first'] if mode=='dual' else []),'actual file records')
 if mode!='physical':
  for o in a['diagnostics']:require(number(o['pending'])==0,'eventual disposition')
  if mode=='loss':require((a['diagnostics'][0]['invalid'],a['diagnostics'][0]['filtered'])==('2','2'),'later facts excluded')
  if mode=='later-failure':require(a['diagnostics'][0]['failed']=='1','later write failure')
def corruption_controls(cases):
 samples={c['id']:c['report'] for c in cases};controls=[]
 edits=[
  ('completion','write-timeout','summary flags',lambda a:a['result'].update(completion='Completed')),
  ('pending','write-timeout','boundary identity',lambda a:a['result']['outputs'][0].update(pending='0')),
  ('flush-flag','flush-timeout','writer flush flag',lambda a:a['result']['outputs'][0].update(flushSucceeded=True)),
  ('initial-global','loss','initial global facts',lambda a:a['result'].update(invalid='2')),
  ('initial-reject','loss','initial rejection facts',lambda a:a['result']['outputs'][0].update(full='3')),
  ('success','loss','summary flags',lambda a:a['result'].update(succeeded=False)),
  ('immutable','write-timeout','owned observation',lambda a:a.update(immutableAfterIo=False)),
  ('UTC','physical','UTC labels',lambda a:a['result'].update(observed='bad')),
  ('framing','physical','record framing',lambda a:a.update(fileBytes=base64.b64encode(decoded(a['fileBytes'])[:-1]).decode('ascii'))),
  ('timer','clock','timer ownership',lambda a:a['timers'].update(disposed=0)),
  ('clock','clock','clock corrections',lambda a:a.update(survivedUtcCorrections=False)),
  ('cancel','pre-cancel','caller cancellation',lambda a:a.update(cancelled=False)),
  ('later-boundary','later-failure','covered dispositions',lambda a:a['result']['outputs'][0].update(boundary='2',failed='1')),
  ('exhaustion','exhaustion','unresolved in-flight write',lambda a:a.update(stillPendingAfterFault=False)),
  ('lease','physical','physical ownership',lambda a:a.update(leaseReleased=False)),
  ('pid','empty','actual child PID',lambda a:a.update(pid=a['pid']+1)),
  ('long-fire','long-timeout','long timer slicing',lambda a:a['longTimerPhase'].update(fires=0)),
  ('long-due','long-timeout','long timer slicing',lambda a:a['longTimerPhase'].update(nextDueTicks='9223372036854775807')),
  ('registered','write-timeout','registered control',lambda a:a.update(heldControls=0)),
  ('retained','cancel','retained control',lambda a:a.update(controlsAfterWait=0)),
  ('later-facts','loss','later facts excluded',lambda a:a['diagnostics'][0].update(invalid='1')),
  ('eventual','write-timeout','eventual disposition',lambda a:a['diagnostics'][0].update(pending='1')),
  ('outputs','dual','enabled output order',lambda a:a['result']['outputs'].reverse()),
  ('console-record','cancel','actual console records',lambda a:a.update(bytes='')),
  ('file-record','physical','actual file records',lambda a:a.update(fileBytes='')),
  ('later-failure','later-failure','later write failure',lambda a:a['diagnostics'][0].update(failed='0'))]
 for identity,mode,reason,edit in edits:
  bad=copy.deepcopy(samples[mode]);edit(bad)
  try:check(mode,bad,samples[mode]['pid'])
  except AssertionError as error:require(str(error)==reason,'unexpected control reason '+identity);controls.append(dict(id=identity,rejected=True,reason=reason))
  else:raise AssertionError('control accepted '+identity)
 return controls
def main():
 p=argparse.ArgumentParser()
 for key in ('dotnet','host','output','family'):p.add_argument('--'+key,required=True)
 a=p.parse_args();require((os.name=='nt')==(a.family=='windows'),'native OS')
 host=Path(a.host).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True);cases=[]
 with tempfile.TemporaryDirectory(prefix='cislib-flush-results-') as temp:
  for mode in MODES:
   work=Path(temp)/mode;work.mkdir()
   command=[a.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'flush-results',mode,str(work)]
   process=subprocess.Popen(command,cwd=work,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
   try:
    try:stdout,stderr=process.communicate(timeout=35)
    except BaseException:process.kill();process.communicate(timeout=15);raise
   finally:
    if process.poll() is None:process.kill();process.wait(timeout=15)
   (out/(mode+'.stdout.bin')).write_bytes(stdout);(out/(mode+'.stderr.bin')).write_bytes(stderr)
   require(process.returncode==0 and stderr==b'','child failure '+mode)
   report=load(stdout);check(mode,report,process.pid)
   require((report['os'],report['coreSha256'],report['fixtureSha256'])==(a.family,sha(host.parent/'CIS.Core.dll'),sha(host)),'actual assemblies')
   case=dict(id=mode,pid=process.pid,command=command,exitCode=0,report=report,stdoutSha256=digest(stdout),stderrSha256=digest(stderr))
   if mode=='physical':
    raw=(work/'flush-results.jsonl').read_bytes();require(raw==decoded(report['fileBytes']),'physical bytes')
    (out/'physical.active.bin').write_bytes(raw);case['activeSha256']=digest(raw)
   cases.append(case)
 controls=corruption_controls(cases)
 report=dict(schema='cis.logging.flush-results-native/v1',task='WP05-S08-T03',family=a.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=sha(host.parent/'CIS.Core.dll'),fixtureSha256=sha(host),verifierSha256=sha(__file__),cases=cases,controls=controls)
 (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
 print(json.dumps(dict(family=a.family,children=len(cases),controls=len(controls),passed=True)))
if __name__=='__main__':main()
