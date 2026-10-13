"""Independent checks of literal console outcomes and original controlled/physical stream bytes."""
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

def check_lines(catalog, row, token, destination, fmt, detail):
    raw = base64.b64decode(token, validate=True); cap = row['maxEntryBytes']
    levels = [r['level'] for r in row['expected']['results'] if any(o['name'] == destination and o['status'] == 'Accepted' for o in r['outputs'])]
    require(not raw or raw.endswith(b'\n'), 'Final LF missing')
    require(b'\r' not in raw and b'\x1b' not in raw, 'Raw controls')
    lines = raw[:-1].split(b'\n') if raw else []
    require(len(lines) == len(levels), 'Selected stream line count differs')
    for line, level in zip(lines, levels):
        require(len(line) + 1 <= cap and not line.startswith(b'\xef\xbb\xbf'), 'Actual entry byte bound/BOM differs')
        wanted = copy.deepcopy(catalog['baseRecord']); wanted['status'] = ['trace','debug','info','warn','error','critical'][level]
        large = row['mode'] == 'cap'; capture_loss = row['expected']['captureTruncated']; loss = row['expected']['truncatedEvents'] > 0
        wanted['message'] = '\u200d' * 8192 if large else f'message-{level} caf\u00e9 \U0001f642\nnext \u202e'
        if large: wanted['attributes'] = {'payload':'\u200d' * 8192}
        if fmt == 1:
            value = read(line.decode('utf-8'))
            if not capture_loss: require(same(value, wanted), 'Original JSON facts/types differ')
            else:
                for key in ['schema_version','timestamp','status','event_name','service','env','version','component','resource','operation_id']:
                    require(same(value[key], wanted[key]), 'Protected JSON fact differs')
                require(value['truncated'] is True and 0 < len(value['message']) < 8192 and set(value['message']) == {'\u200d'}, 'Captured truncation differs')
                require(set(value) <= set(wanted) | {'truncated'}, 'Unknown JSON fact appeared')
        else:
            value = human(line)
            for display, original in [('timestamp','timestamp'),('severity','status'),('component','component'),('event_name','event_name')]:
                require(same(value[display], wanted[original]), 'Human identity differs')
            require(value['message'] == wanted['message'] if not capture_loss else 0 < len(value['message']) < 8192 and set(value['message']) == {'\u200d'}, 'Human captured message differs')
            require(value.get('truncated') is True if loss else 'truncated' not in value, 'Human loss differs')
            keys = {'timestamp','severity','component','event_name','message'}
            if detail == 1:
                for key in ['service','env','version','operation_id']:
                    require(same(value[key], wanted[key]), 'Verbose identity differs'); keys.add(key)
                for key, field in wanted['resource'].items():
                    require(same(value['resource.'+key], field), 'Verbose resource differs'); keys.add('resource.'+key)
                omitted = large and cap <= 65536
                require(('attributes' in value) == (not omitted), 'Verbose payload selection differs')
                if not omitted:
                    require(same(value['attributes'], wanted['attributes']), 'Verbose typed payload differs'); keys.add('attributes')
                has_template = not large or cap >= 65536
                require(('message_template' in value) == has_template, 'Verbose template selection differs')
                if has_template:
                    require(value['message_template'] == wanted['message_template'], 'Literal template changed'); keys.add('message_template')
            if loss: keys.add('truncated')
            require(set(value) == keys, 'Human field set differs')

def verify(catalog, row, actual):
    expected = row['expected']; require(actual['caseId'] == row['id'], 'Console identity differs')
    require(same(actual['results'], expected['results']), 'Frozen outcomes/tickets/loss differ')
    for key in ['clockCalls','selectionCalls']: require(type(actual[key]) is int and actual[key] == expected[key], 'Console '+key+' differs')
    require(actual['state'] == 'Stopped' and actual['invalid'] == '0' and actual['truncated'] == str(expected['truncatedEvents']), 'Global loss/state differs')
    names = {'console':['console'],'both':['console','file'],'file':['file'],'disabled':[]}[row['profile']]
    require(len(actual['outputs']) == len(names), 'Configured output set differs')
    for output, name in zip(actual['outputs'], names):
        records = expected[name+'Records']; filtered = sum(any(o['name'] == name and o['status'] == 'Filtered' for o in r['outputs']) for r in expected['results'])
        require(output == dict(name=name,accepted=str(records),written=str(records),filtered=str(filtered),pending='0',failed='0',abandoned='0'), 'Final output accounting differs')
    selected = 'stderr' if row['stream'] == 0 else 'stdout'; other = 'stdout' if selected == 'stderr' else 'stderr'
    require(actual[other] == '', 'Records reached unselected stream')
    check_lines(catalog,row,actual[selected],'console',row['format'],row['detail'])
    check_lines(catalog,row,actual['file'],'file',1,0)

def damage(actual, fault):
    value = copy.deepcopy(actual)
    if fault == 'stream': value['stdout'],value['stderr'] = value['stderr'],value['stdout']
    elif fault == 'bom': value['stderr'] = base64.b64encode(b'\xef\xbb\xbf'+base64.b64decode(value['stderr'])).decode('ascii')
    elif fault == 'outcome': value['results'][0]['outcome'] = 'Accepted'
    elif fault == 'ticket': value['results'][1]['outputs'][0]['ticket'] = '99'
    elif fault == 'loss': value['truncated'] = '99'
    elif fault == 'clock': value['clockCalls'] = 99
    elif fault == 'identity': value['stderr'] = base64.b64encode(base64.b64decode(value['stderr']).replace(b'Console.App',b'Wrong.App')).decode('ascii')
    else: raise ValueError('Unknown damage')
    return value

def main():
    parser = argparse.ArgumentParser()
    for name in ['dotnet','host','catalog','output']: parser.add_argument('--'+name,required=True)
    parser.add_argument('--family',choices=['windows','linux'],required=True); args=parser.parse_args()
    catalog = read(Path(args.catalog).read_text(encoding='utf-8')); rows=catalog['cases']; output=Path(args.output); output.mkdir(parents=True,exist_ok=True)
    ids = {f'CONSOLE-{p}-{f}{d}{s}' for p,(f,d,s) in itertools.product(['CONSOLE','BOTH','FILE','DISABLED'],itertools.product([0,1],[0,1],[0,1]))}
    ids |= {f'CONSOLE-CAP-{f}{d}-{c}' for f,d,c in [(0,0,65536),(0,1,65536),(0,1,16384),(0,1,131072),(1,1,65536)]}
    ids |= {f'CONSOLE-PHYSICAL-{f}{s}' for f,s in itertools.product([0,1],[0,1])}
    require(len(rows)==41 and {r['id'] for r in rows}==ids,'Exact declared console set differs')
    report=dict(scope='WP05-S04-T01 actual configured rendering/admission/controlled and physical console streams, not complete S04 ownership/lifecycle/env consumers or release',family=args.family,cases=[],negativeControlsRejected=[]); actuals={}
    for row in rows:
        command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',args.host,'console-composition',str(Path(args.catalog).resolve()),row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-console-') as cwd:
            process=subprocess.run(command,cwd=cwd,capture_output=True,timeout=30); require(not list(Path(cwd).iterdir()),'Unexpected files')
        (output/(row['id']+'.stdout.bin')).write_bytes(process.stdout); (output/(row['id']+'.stderr.bin')).write_bytes(process.stderr)
        require(process.returncode==0,'Child failure')
        raw=process.stdout.rstrip(b'\r\n'); start=raw.rfind(b'\n')+1; actual=read(raw[start:].decode('utf-8'))
        if row['physical']:
            actual['stdout']=base64.b64encode(raw[:start]).decode('ascii'); actual['stderr']=base64.b64encode(process.stderr).decode('ascii')
        else: require(start==0 and process.stderr==b'','Controlled child emitted extra stdout/stderr')
        verify(catalog,row,actual); actuals[row['id']]=actual; observed=actual['observed']
        require(observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==args.family,'Exact runtime/platform differs')
        # OperatingSystem uses linux/windows values, matching the lane names.
        require(observed['fixtureSha256']==sha(args.host) and observed['coreSha256']==sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256']==sha(args.catalog),'Actual input/binary identity differs')
        report['cases'].append(dict(id=row['id'],passed=True,command=command,observed=observed))
    row=next(r for r in rows if r['id']=='CONSOLE-CONSOLE-110')
    for fault in ['stream','outcome','ticket','loss','clock','identity','bom']:
        try: verify(catalog,row,damage(actuals[row['id']],fault))
        except AssertionError: report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Accepted console damage: '+fault)
    require(len(report['negativeControlsRejected'])==7,'Controls missing')
    report.update(passed=True,count=41,coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')),hostSha256=sha(args.host),inputsSha256=sha(args.catalog))
    (output/'summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n'); print(json.dumps(dict(family=args.family,count=41,passed=True)))

if __name__=='__main__': main()
