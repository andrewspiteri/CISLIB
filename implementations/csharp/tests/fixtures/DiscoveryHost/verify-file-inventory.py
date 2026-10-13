"""Real native inventory children, literal metadata and preserved original bytes."""
import argparse,hashlib,json,os,platform,queue,subprocess,tempfile,threading,uuid
from pathlib import Path
LIMIT=15
GROUP="0123456789abcdef0123456789abcdef"
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def metadata(path):return Path(str(path)+".cis.inventory.json")
def closed_name(id,state="closed"):return f".cis-log-{GROUP}-{id:020d}.{state}"
def baseline(path):return dict(schema=1,groupId=GROUP,format="jsonl",encoding="utf-8",bom=False,newline="lf",segments=[dict(id=1,fileName=path.name,state="active")])
def main():
    parser=argparse.ArgumentParser()
    for key in ("dotnet","host","output","family"):parser.add_argument("--"+key,required=True)
    args=parser.parse_args();out=Path(args.output);out.mkdir(parents=True,exist_ok=True);verifier_hash=sha(__file__)
    host=Path(args.host).resolve();host_hash=sha(host);core_hash=sha(host.parent/"CIS.Core.dll")
    command=[args.dotnet,"exec","--fx-version","10.0.12","--roll-forward","Disable",str(host),"file-inventory"]
    env=dict(os.environ);env.pop("DOTNET_SYSTEM_IO_DISABLEFILELOCKING",None);cases=[]
    def check(report,status,format="jsonl",code=None,length=0,created=False,count=1,id=1,group=None,path=None):
        assert report["status"]==status,"status differs"
        assert report["runtime"]==".NET 10.0.12" and report["os"]==args.family,"runtime/platform differs"
        assert report["coreSha256"]==core_hash and report["fixtureSha256"]==host_hash,"assembly bytes differ"
        if status=="acquired":
            assert report["error"] is None,"unexpected error"
            assert (report["format"],report["length"],report["created"])==(format,length,created),"acquired facts differ"
            assert (report["segments"],report["segmentId"])==(count,id),"segment identity differs"
            assert uuid.UUID(report["groupId"]).hex==report["groupId"] and report["groupId"]!="0"*32,"group identity differs"
            if group is not None:assert report["groupId"]==group,"persisted group identity differs"
            raw=metadata(path).read_bytes();assert not raw.startswith(b"\xef\xbb\xbf"),"metadata BOM"
            stored=json.loads(raw.decode("utf-8",errors="strict"))
            assert (stored["schema"],stored["groupId"],stored["format"])==(1,report["groupId"],format),"stored identity differs"
            assert (stored["encoding"],stored["bom"],stored["newline"])==("utf-8",False,"lf"),"stored encoding differs"
            assert report["inventorySha256"]==hashlib.sha256(raw).hexdigest(),"metadata bytes differ"
            assert not list(path.parent.glob(metadata(path).name+".*.tmp")),"successful publication left private candidates"
        else:
            for key in ("length","created","groupId","segmentId","segments","format","inventorySha256"):assert report[key] is None,"failed/released owner leaked inventory facts"
            if status=="failed":assert report["error"]==dict(output="file",code=code,message=f"Logging output 'file' could not initialize ({code}).",inner=False,dataCount=0),"safe error differs"
            else:assert report["error"] is None,"release error differs"
    def retain(name,cmd,cwd,stdout,stderr,reports,exit_code,preserved=None):
        assert exit_code==0 and stderr==b"","child failure"
        (out/(name+".stdout.bin")).write_bytes(stdout);(out/(name+".stderr.bin")).write_bytes(stderr)
        cases.append(dict(id=name,command=cmd,workingDirectory=str(cwd),exitCode=exit_code,reports=reports,stdoutSha256=hashlib.sha256(stdout).hexdigest(),stderrSha256=hashlib.sha256(stderr).hexdigest(),preserved=preserved or {}))
    def probe(name,path,cwd,format="jsonl",status="acquired",code=None,length=0,created=False,count=1,id=1,group=None):
        before={}
        for label,p in (("active",path),("inventory",metadata(path))):
            if p.is_file():before[label]=(p,p.read_bytes());(out/(name+"."+label+".before.bin")).write_bytes(before[label][1])
        cmd=command+[format,str(path)];run=subprocess.run(cmd,cwd=cwd,env=env,capture_output=True,timeout=LIMIT);report=json.loads(run.stdout)
        check(report,status,format,code,length,created,count,id,group,path);preserved={}
        for label,(p,raw) in before.items():
            assert p.read_bytes()==raw,"pre-existing "+label+" changed";preserved[label]=dict(beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=sha(p))
        retain(name,cmd,cwd,run.stdout,run.stderr,[report],run.returncode,preserved);return report
    with tempfile.TemporaryDirectory(prefix="cislib-file-inventory-") as temp:
        root=Path(temp);later=root/"later";later.mkdir();groups=[]
        for format in ("jsonl","human"):
            path=root/format/"\u03a9-\U0001f642.log";first=probe(format+"-create",path,later,format,created=True);groups.append(first["groupId"])
            old=b"pre-existing-records\n";path.write_bytes(old)
            second=probe(format+"-restart",path,later,format,length=len(old),group=first["groupId"])
            assert first["inventorySha256"]==second["inventorySha256"],"restart rewrote inventory"
            probe(format+"-switch-fails",path,later,"human" if format=="jsonl" else "jsonl","failed","FileInventoryIncompatible")
        assert len(set(groups))==2,"distinct groups reused identity"
        for name,old in (("missing-jsonl",b'{"schemaVersion":1}\n'),("missing-human",b"INFO prior human record\n"),("missing-partial",b"partial-json")):
            path=root/(name+".log");path.write_bytes(old);probe(name,path,later,status="failed",code="FileInventoryMissing");assert not metadata(path).exists()
        for damage in ("format","encoding","bom","newline","schema-kind","id-kind","duplicate-key","invalid-utf8","bom-prefix","too-large","too-deep"):
            path=root/(damage+".log");path.write_bytes(b"existing\n");doc=baseline(path)
            for key,value in {"format":("format","human"),"encoding":("encoding","utf-16"),"bom":("bom",True),"newline":("newline","crlf"),"schema-kind":("schema","1")}.items():
                if damage==key:doc[value[0]]=value[1]
            if damage=="id-kind":doc["segments"][0]["id"]="1"
            if damage=="too-deep":
                nested=1
                for _ in range(14):nested=[nested]
                doc["future"]=nested
            raw=json.dumps(doc,separators=(",",":")).encode("utf-8")
            if damage=="duplicate-key":raw=raw.replace(b'"schema":1',b'"schema":1,"schema":1')
            if damage=="invalid-utf8":raw=raw[:-1]+b',"future":"\xff"}'
            if damage=="bom-prefix":raw=b"\xef\xbb\xbf"+raw
            if damage=="too-large":raw=b" "*(2*1024*1024+1)
            metadata(path).write_bytes(raw);probe(damage,path,later,status="failed",code="FileInventoryIncompatible")
        for mode in ("valid-members","escaped-member","missing-member"):
            directory=root/mode;directory.mkdir();path=directory/"active.log";path.write_bytes(b"active\n");doc=baseline(path)
            doc["segments"]=[dict(id=1,fileName=closed_name(1),state="closed"),dict(id=2,fileName=closed_name(2,"damaged"),state="damaged"),dict(id=3,fileName=path.name,state="active")];closed={}
            for id,state in ((1,"closed"),(2,"damaged")):
                member=directory/closed_name(id,state);member.write_bytes(b"keep-"+state.encode());closed[member]=member.read_bytes()
            if mode=="escaped-member":doc["segments"][0]["fileName"]="../unrelated.txt";(root/"unrelated.txt").write_bytes(b"keep-unrelated")
            if mode=="missing-member":
                member=directory/closed_name(1);assert member.resolve().is_relative_to(root.resolve());member.unlink();closed.pop(member)
            metadata(path).write_text(json.dumps(doc),encoding="utf-8")
            probe(mode,path,later,status="acquired" if mode=="valid-members" else "failed",code=None if mode=="valid-members" else "FileInventoryIncompatible",length=7,count=3,id=3,group=GROUP)
            for member,raw in closed.items():assert member.read_bytes()==raw,"retained member changed"
        path=root/"lost-active.log";metadata(path).write_text(json.dumps(baseline(path)),encoding="utf-8");
        for attempt in range(3):
            probe("lost-active-"+str(attempt),path,later,status="failed",code="FileInventoryIncompatible");assert not path.exists()
        for count in (1000,1001):
            directory=root/("count-"+str(count));directory.mkdir();path=directory/"active.log";path.write_bytes(b"active\n");doc=baseline(path);entries=[]
            for id in range(1,count+1):
                name=closed_name(id);(directory/name).write_bytes(b"keep");entries.append(dict(id=id,fileName=name,state="closed"))
            entries.append(dict(id=count+1,fileName=path.name,state="active"));doc["segments"]=entries;metadata(path).write_text(json.dumps(doc),encoding="utf-8")
            probe("count-"+str(count),path,later,status="acquired" if count==1000 else "failed",code=None if count==1000 else "FileInventoryIncompatible",length=7,count=count+1,id=count+1,group=GROUP)
            assert len(list(directory.glob("*.closed")))==count,"count guard changed retained files"
        path=root/"metadata-directory.log";path.write_bytes(b"existing\n");metadata(path).mkdir();kept=metadata(path)/"keep.txt";kept.write_bytes(b"keep");probe("metadata-directory",path,later,status="failed",code="FileInventoryInitializationFailed");assert kept.read_bytes()==b"keep"
        path=root/"held.log";cmd=command+["hold-jsonl",str(path)];child=subprocess.Popen(cmd,cwd=later,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        try:
            ready=queue.Queue();threading.Thread(target=lambda:ready.put(child.stdout.readline()),daemon=True).start();first=ready.get(timeout=LIMIT);report=json.loads(first);check(report,"acquired",created=True,path=path);before=metadata(path).read_bytes()
            probe("held-contender",path,later,status="failed",code="FileOwnershipUnavailable");assert metadata(path).read_bytes()==before
            distinct=probe("held-distinct",root/"distinct.log",later,"human",created=True);assert distinct["groupId"]!=report["groupId"]
            rest,stderr=child.communicate(input=b"release\n",timeout=LIMIT);released=json.loads(rest);check(released,"released");retain("holder",cmd,later,first+rest,stderr,[report,released],child.returncode)
            probe("held-restart",path,later,group=report["groupId"])
        finally:
            if child.poll() is None:child.kill();child.communicate(timeout=LIMIT)
        assert not list(later.iterdir()),"captured path redirected output";native_root=str(root)
    controls=[];good=next(case["reports"][0] for case in cases if case["id"]=="held-contender")
    for field,reason in (("status","status differs"),("runtime","runtime/platform differs"),("coreSha256","assembly bytes differ"),("groupId","failed/released owner leaked inventory facts"),("error","safe error differs")):
        bad=json.loads(json.dumps(good));bad[field]="damaged" if field!="error" else dict(good["error"],inner=True)
        try:check(bad,"failed",code="FileOwnershipUnavailable")
        except AssertionError as error:assert str(error)==reason,"control failed for an unintended reason";controls.append(dict(field=field,rejection=str(error)))
        else:raise AssertionError("damaged report was accepted")
    report=dict(schema="cis.logging.file-inventory-native/v1",taskId="WP05-S05-T02",family=args.family,platform=platform.platform(),nativeTemporaryRoot=native_root,children=len(cases),runtime=".NET 10.0.12",coreSha256=core_hash,fixtureSha256=host_hash,verifierSha256=verifier_hash,cases=cases,controls=controls,scope="Actual created/restarted metadata, format/encoding/missing inventory rejection, preserved content, exact closed/damaged members, bounded reader and live two-process ownership. No append/rotation/recovery/public runtime qualification.")
    (out/"report.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8");print(json.dumps(dict(family=args.family,children=len(cases),controls=len(controls),failed=0)))
if __name__=="__main__":main()
