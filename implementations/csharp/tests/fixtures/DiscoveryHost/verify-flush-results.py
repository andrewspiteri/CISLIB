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
