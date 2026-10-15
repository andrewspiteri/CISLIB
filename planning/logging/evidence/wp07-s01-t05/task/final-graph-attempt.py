from pathlib import Path
import subprocess,json
r=Path.cwd();e=r/'planning/logging/evidence/wp07-s01-t05';t=r/'.artifacts/csharp/tasks/wp07-s01-t05';p=e/'graph/final-refresh.log'
try:
 with p.open('wb') as out:x=subprocess.run(['pwsh','-NoProfile','-File','eng/cis.ps1','graph','build','--workspace','.','--format','agent'],stdout=out,stderr=subprocess.STDOUT,timeout=120)
 code=x.returncode
except subprocess.TimeoutExpired:code=124
context=json.loads((e/'graph/graph-context.json').read_text(encoding='utf-8-sig'));context['finalRefreshAttempt']={'status':'Completed' if code==0 else 'TimedOut' if code==124 else 'Failed','exitCode':code,'deadlineSeconds':120,'log':'final-refresh.log'}
if code==0:
 x=subprocess.run(['pwsh','-NoProfile','-File','eng/cis.ps1','graph','status','--repo','.','--format','agent'],capture_output=True,timeout=30);(e/'graph/final-status.log').write_bytes(x.stdout+x.stderr);context['finalStatus']=x.stdout.decode('utf-8',errors='replace').split('diagnostic=')[0].strip();context['latestObservedFreshness']='fresh' if 'freshness=fresh;' in context['finalStatus'] else 'not-confirmed-fresh'
(e/'graph/graph-context.json').write_bytes((json.dumps(context,indent=2)+'\n').encode());print('Final graph refresh exit',code)
