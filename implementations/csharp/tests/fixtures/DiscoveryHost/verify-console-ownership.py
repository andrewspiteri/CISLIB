"""Original-byte checks for borrowed console lifetime, per-runtime serialization and application stream survival."""
import argparse
import base64
import copy
import hashlib
import itertools
import json
from pathlib import Path
import subprocess
import tempfile
import unicodedata

def require(condition, message):
    if not condition: raise AssertionError(message)

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result, 'Duplicate JSON member'); result[key] = value
    return result

def read(token):
    return json.loads(token, object_pairs_hook=pairs, parse_constant=lambda value: (_ for _ in ()).throw(AssertionError('Invalid JSON number')))

def same(a, b):
    if type(a) is bool or type(b) is bool: return type(a) is type(b) and a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)): return a == b
    if type(a) is not type(b): return False
    if isinstance(a, dict): return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list): return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    return a == b

def human(line):
    text = line.decode('utf-8')
    require(not any(unicodedata.category(c) in ['Cc','Cf','Zl','Zp'] for c in text), 'Unsafe raw human control')
    decoder = json.JSONDecoder(object_pairs_hook=pairs); result = {}; offset = 0
    while offset < len(text):
        while offset < len(text) and text[offset] == ' ': offset += 1
        if offset == len(text): break
        equal = text.index('=', offset); key = text[offset:equal]
        require(key not in result, 'Duplicate display key')
        start = equal + 1
        while start < len(text) and text[start] == ' ': start += 1
        value, end = decoder.raw_decode(text, start); result[key] = value; offset = end
        require(offset == len(text) or text[offset] == ' ', 'Display separator missing')
    return result


def marker(stream): return ('{"application":"'+stream+'-still-open"}\n').encode('utf-8')

def verify(catalog, row, actual):
    e=row['expected'];mode=row['mode'];physical=row['physical'];fail=mode=='fault';count=e['records'];runs=e['runtimes'];per_run=e['perRuntime']
    require(actual['caseId']==row['id'],'Borrowed case identity differs')
    for key in ['selectionCalls','clockCalls','writeCalls','maxActive','disposedBeforeOwner','asyncDisposed','ownerDisposeCalls']:
        require(same(actual[key],e[key]),'Borrowed '+key+' differs')
    for key in ['active','flushes','otherWriteCalls','otherDisposes']: require(same(actual[key],0),'Unexpected stream operation '+key)
    require(actual['inheritedContext'] is False,'Worker inherited creator context')
    results=actual['results'];require(len(results)==count,'Caller count differs')
    require(all(type(v['index']) is int for v in results) and sorted(v['index'] for v in results)==list(range(count)),'Caller identity set differs')
    for v in results:
        require(type(v['generation']) is int and v['generation']==v['index']//per_run,'Runtime generation differs')
        require(v['outcome']=='Accepted' and v['truncated'] is False and same(v['outputCount'],1) and v['name']=='console' and v['status']=='Accepted','Admission differs')
        require(isinstance(v['ticket'],str) and v['ticket'].isdigit() and str(int(v['ticket']))==v['ticket'],'Canonical ticket differs')
    for generation in range(runs):
        cohort=[v for v in results if v['generation']==generation]
        require(sorted(int(v['ticket']) for v in cohort)==list(range(1,per_run+1)),'Successful ticket set differs')
        if mode!='serialized': require(all(v['ticket']==str(v['index']%per_run+1) for v in cohort),'Sequential ticket order differs')
    require(len(actual['stops'])==runs,'Completed runtime count differs')
    wanted=dict(state='Stopped',outputState='Stopped',accepted=per_run,written=e['written'],failed=e['failed'],abandoned=e['abandoned'],pending=0,inFlight=0,queueFull=0,filtered=0,unavailable=0,ticket=per_run,fault='WriteFailed' if fail else None,invalid=0,truncated=0)
    require(all(same(v,wanted) for v in actual['stops']),'Completed dispositions differ')
    if not physical and count:
        held=dict(state='Stopping',accepted=per_run,pending=per_run,written=0,inFlight=1,calls=1,active=1,disposes=0,asyncDisposes=0)
        require(same(actual['held'],held),'Held closure/lifetime/serialization differs')
    else: require(actual['held'] is None,'Unobserved held seam appeared')
    selected='stderr' if row['stream']==0 else 'stdout';other='stdout' if selected=='stderr' else 'stderr'
    raw=base64.b64decode(actual[selected],validate=True);ending=marker(selected)
    require(raw.endswith(ending),'Selected application marker missing')
    require(base64.b64decode(actual[other],validate=True)==marker(other),'Unselected stream contains records or lost application result')
    raw=raw[:-len(ending)]
    if fail:
        require(raw==e['failedPrefix'].encode('utf-8'),'Failed prefix/retry differs');return
    require(not raw or raw.endswith(b'\n'),'Final record LF missing')
    lines=raw[:-1].split(b'\n') if raw else [];ordered=sorted(results,key=lambda v:(v['generation'],int(v['ticket'])))
    require(len(lines)==count,'Whole record count differs')
    for line,v in zip(lines,ordered):
        require(len(line)+1<=65536 and not line.startswith(b'\xef\xbb\xbf'),'Record cap/BOM differs')
        index=v['index'];wanted=copy.deepcopy(catalog['baseRecord']);wanted.update(event_name='borrow.'+str(index),message='owned caf\u00e9 \U0001f642\nend='+str(index),attributes={'index':index})
        if row['format']==1: require(same(read(line.decode('utf-8')),wanted),'JSON facts/types/ticket order differ')
        else:
            parsed=human(line);wanted={d:wanted[j] for d,j in [('timestamp','timestamp'),('severity','status'),('component','component'),('event_name','event_name'),('message','message')]}
            require(same(parsed,wanted),'Human facts/ticket order differ')

def damage(actual,fault):
    a=copy.deepcopy(actual)
    if fault=='dispose': a['disposedBeforeOwner']=1
    elif fault=='overlap': a['maxActive']=2
    elif fault=='ticket': a['results'][0]['ticket']='99'
    elif fault=='state': a['stops'][0]['pending']=1
    elif fault=='held': a['held']['inFlight']=2
    elif fault=='prefix': a['stderr']=base64.b64encode(b'x'+base64.b64decode(a['stderr'])).decode('ascii')
    elif fault=='fault': a['stops'][0]['failed']=0
    elif fault=='stream': a['stdout'],a['stderr']=a['stderr'],a['stdout']
    elif fault=='bytes': a['stderr']=base64.b64encode(base64.b64decode(a['stderr']).replace(b'Console.App',b'Wrong.App')).decode('ascii')
    elif fault=='order':
        lines=base64.b64decode(a['stderr']).split(b'\n');lines[0],lines[1]=lines[1],lines[0];a['stderr']=base64.b64encode(b'\n'.join(lines)).decode('ascii')
    else: raise ValueError('Unknown ownership damage')
    return a

def main():
    parser=argparse.ArgumentParser()
    for name in ['dotnet','host','catalog','output']: parser.add_argument('--'+name,required=True)
    parser.add_argument('--family',choices=['windows','linux'],required=True);args=parser.parse_args()
    catalog=read(Path(args.catalog).read_text(encoding='utf-8'));rows=catalog['cases'];output=Path(args.output);output.mkdir(parents=True,exist_ok=True)
    ids={f'BORROW-{m}-{f}{s}' for m,f,s in itertools.product(['DRAIN','FAULT','SERIALIZED','REUSE','PHYSICAL'],[0,1],[0,1])}|{'BORROW-IDLE-00','BORROW-IDLE-01'}
    require(len(rows)==22 and {r['id'] for r in rows}==ids,'Exact ownership vector set differs')
    report=dict(scope='WP05-S04-T02 actual borrowed/held/failing/chunked Streams and physical destination selection and continued application output; sequential reuse uses controlled streams; per-runtime serialization, not global application writer coordination, public Stop or release',family=args.family,cases=[],negativeControlsRejected=[]);actuals={}
    for row in rows:
        command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',args.host,'console-ownership',str(Path(args.catalog).resolve()),row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-borrow-') as cwd:
            process=subprocess.run(command,cwd=cwd,capture_output=True,timeout=30);require(not list(Path(cwd).iterdir()),'Unexpected files')
        (output/(row['id']+'.stdout.bin')).write_bytes(process.stdout);(output/(row['id']+'.stderr.bin')).write_bytes(process.stderr)
        require(process.returncode==0,'Child failure '+row['id']+' '+process.stderr.decode('utf-8',errors='replace'))
        raw=process.stdout.rstrip(b'\r\n');start=raw.rfind(b'\n')+1;actual=read(raw[start:].decode('utf-8'))
        if row['physical']:
            actual['stdout']=base64.b64encode(raw[:start]).decode('ascii');actual['stderr']=base64.b64encode(process.stderr).decode('ascii')
        else: require(start==0 and process.stderr==b'','Controlled child emitted extra stdout/stderr')
        verify(catalog,row,actual);actuals[row['id']]=actual;observed=actual['observed']
        require(observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==args.family,'Exact runtime/platform differs')
        require(observed['fixtureSha256']==sha(args.host) and observed['coreSha256']==sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256']==sha(args.catalog),'Actual input/binary identity differs')
        report['cases'].append(dict(id=row['id'],passed=True,command=command,observed=observed))
    for fault in ['dispose','overlap','ticket','order','bytes','stream','state','fault','held','prefix','catalog-written','catalog-failed','catalog-abandoned']:
        row=next(r for r in rows if r['id']==('BORROW-FAULT-10' if fault in ['fault','prefix','catalog-failed','catalog-abandoned'] else 'BORROW-DRAIN-10'))
        changed_row=copy.deepcopy(row)
        if fault.startswith('catalog-'):
            key=fault[8:];changed_row['expected'][key]+=1;changed=actuals[row['id']]
        else: changed=damage(actuals[row['id']],fault)
        messages={'dispose':'Borrowed disposedBeforeOwner differs','overlap':'Borrowed maxActive differs','ticket':'Successful ticket set differs','order':'JSON facts/types/ticket order differ','bytes':'JSON facts/types/ticket order differ','stream':'Selected application marker missing','held':'Held closure/lifetime/serialization differs','prefix':'Failed prefix/retry differs'}
        try: verify(catalog,changed_row,changed)
        except AssertionError as error:
            require(str(error)==messages.get(fault,'Completed dispositions differ'),'Wrong rejection reason for '+fault)
            report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Accepted ownership damage '+fault)
    require(len(report['negativeControlsRejected'])==13,'Ownership controls missing')
    report.update(passed=True,count=22,coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')),hostSha256=sha(args.host),inputsSha256=sha(args.catalog))
    (output/'summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,count=22,passed=True)))

if __name__=='__main__': main()
