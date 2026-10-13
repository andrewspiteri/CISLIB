"""Read-only development example: select a consistent stopped managed JSONL group."""
import json,re,sys
from pathlib import Path
INVENTORY_BYTES=2*1024*1024
ENTRY_BYTES=16*1024*1024
def fail():raise ValueError('Cannot select a consistent stopped JSONL group.')
def unique(pairs):
    result={}
    for key,value in pairs:
        if key in result:fail()
        result[key]=value
    return result
def invalid_constant(value):fail()
def select_inputs(active_path):
    active=Path(active_path).absolute();metadata=Path(str(active)+'.cis.inventory.json')
    pending=Path(str(active)+'.cis.transition.json')
    if pending.exists() or pending.is_symlink() or metadata.is_symlink() or not metadata.is_file():fail()
    with metadata.open('rb') as stream:raw=stream.read(INVENTORY_BYTES+1)
    if len(raw)>INVENTORY_BYTES or raw.startswith(b'\xef\xbb\xbf'):fail()
    document=json.loads(raw.decode('utf-8',errors='strict'),object_pairs_hook=unique,parse_constant=invalid_constant)
    if not isinstance(document,dict) or type(document.get('schema')) is not int or document['schema']!=1 or document.get('format')!='jsonl' or document.get('encoding')!='utf-8' or document.get('bom') is not False or document.get('newline')!='lf':fail()
    group=document.get('groupId');members=document.get('segments')
    if not isinstance(group,str) or not re.fullmatch('[0-9a-f]{32}',group) or group=='0'*32 or not isinstance(members,list) or not 1<=len(members)<=1001:fail()
    result=[];previous=0;names=set()
    for index,member in enumerate(members):
        if not isinstance(member,dict):fail()
        identity=member.get('id');state=member.get('state');name=member.get('fileName');last=index==len(members)-1
        if type(identity) is not int or not previous<identity<=9223372036854775807 or state not in ('active','closed','damaged') or last!=(state=='active'):fail()
        expected=active.name if last else '.cis-log-'+group+'-'+str(identity).zfill(20)+'.'+state
        key=name.casefold() if sys.platform=="win32" and isinstance(name,str) else name
        if name!=expected or key in names:fail()
        names.add(key)
        path=active.parent/name
        if path.is_symlink() or not path.is_file():fail()
        if state!='damaged':
            # Validate the stopped snapshot before exposing any paths; never ingest a partial tail.
            with path.open('rb') as stream:
                first=True
                while True:
                    line=stream.readline(ENTRY_BYTES+1)
                    if not line:break
                    if len(line)>ENTRY_BYTES or not line.endswith(b'\n') or b'\r' in line or first and line.startswith(b'\xef\xbb\xbf'):fail()
                    record=json.loads(line.decode('utf-8',errors='strict'),object_pairs_hook=unique,parse_constant=invalid_constant)
                    if not isinstance(record,dict):fail()
                    first=False
            result.append(str(path))
        previous=identity
    return result
def main():
    if len(sys.argv)!=2:
        print('Usage: select-jsonl-inputs.py <active-path>',file=sys.stderr);return 2
    try:paths=select_inputs(sys.argv[1])
    except (OSError,ValueError,TypeError,RecursionError):
        print('Cannot select a consistent stopped JSONL group.',file=sys.stderr);return 2
    print(json.dumps(paths,ensure_ascii=True));return 0
if __name__=='__main__':raise SystemExit(main())
