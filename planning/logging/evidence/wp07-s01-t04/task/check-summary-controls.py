from pathlib import Path
import importlib.util,json,shutil,tempfile,xml.etree.ElementTree as E
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t04';root=t/'coverage-qualified';language=r/'implementations/csharp'
spec=importlib.util.spec_from_file_location('coverage_summary',language/'eng/summarize-shipping-coverage.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
families=['unit','integration','architecture','business'];inputs=[root/f for f in families]
valid=m.summarize(language,inputs);assert valid['qualified'] is False;assert len(valid['assemblies'])==2
observations=[{'control':'complete-native-scope','passed':True,'detail':'Two assemblies retained; failing helper threshold remains failing.'}]
def control(name,edit,expected):
 with tempfile.TemporaryDirectory(prefix='cislib-coverage-') as temporary:
  dest=Path(temporary)/'results';shutil.copytree(root/'architecture',dest)
  edit(dest)
  try:m.summarize(language,[root/'unit',root/'integration',dest,root/'business'])
  except (ValueError,FileNotFoundError) as error:
   assert expected in str(error),(name,str(error));observations.append({'control':name,'passed':True,'detail':str(error)})
  else:raise AssertionError('Accepted invalid coverage: '+name)
def change_xml(directory,action):
 # Both VSTest attachment copies must remain identical; change the native document in each copy.
 for path in directory.rglob('*.cobertura.xml'):
  tree=E.parse(path);action(tree.getroot());tree.write(path,encoding='utf-8')
def change_trx(directory,action):
 p=directory/'report.trx';x=E.parse(p);action(x.getroot());x.write(p,encoding='utf-8')
control('failed-test',lambda d:change_trx(d,lambda x:next(e for e in x.iter() if e.tag.endswith('Counters')).set('passed','0')),'failed or skipped')
control('missing-attachment',lambda d:[p.unlink() for p in d.rglob('*.cobertura.xml')],'Absent or conflicting')
control('wrong-native-denominator',lambda d:change_xml(d,lambda x:x.set('lines-valid','1')),'native report totals')
control('unknown-shipping-assembly',lambda d:change_xml(d,lambda x:x.find('./packages/package').set('name','Unknown')),'Unexpected shipping assembly')
control('unknown-source',lambda d:change_xml(d,lambda x:x.find('.//class').set('filename','/unknown/source.cs')),'Unmapped source')
def omit_helper(x):
 x.find('./packages').remove(next(p for p in x.findall('./packages/package') if p.get('name')=='CIS.TypeManager.Build'))
 lines=x.findall('./packages/package/classes/class/lines/line')
 import re
 covered=total=0
 for line in lines:
  if line.get('branch','').lower()=='true':
   c,t=map(int,re.search(r'\((\d+)/(\d+)\)',line.get('condition-coverage')).groups());covered+=c;total+=t
 for name,value in [('lines-covered',sum(int(v.get('hits'))>0 for v in lines)),('lines-valid',len(lines)),('branches-covered',covered),('branches-valid',total)]:x.set(name,str(value))
control('missing-helper-with-consistent-native-totals',lambda d:change_xml(d,omit_helper),'Unmeasured executable source')
def branches(x):
 line=next(v for v in x.findall('.//class/lines/line') if v.get('branch','').lower()=='true');line.set('condition-coverage','invalid')
control('missing-branch-counts',lambda d:change_xml(d,branches),'Missing native branch counts')
def duplicate(x):
 import copy
 c=x.find('.//class');c.find('./lines').append(copy.deepcopy(c.find('./lines/line')))
control('duplicate-native-point',lambda d:change_xml(d,duplicate),'Duplicate native')
# Same observations repeated cannot earn additional coverage or branch credit.
again=m.summarize(language,inputs+inputs)
assert valid['assemblies']==again['assemblies'] and valid['aggregate']==again['aggregate']
observations.append({'control':'duplicate-run-no-additive-credit','passed':True,'detail':'Repeated observations leave all covered counts and denominators unchanged.'})
(t/'summary-controls.json').write_bytes((json.dumps({'controls':observations,'passed':len(observations)},indent=2)+'\n').encode());print('Coverage controls PASS',len(observations))
