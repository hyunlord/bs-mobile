#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
stage="${1:?Usage: package-stage.sh S0 YYYYMMDD [destination]}"
date_stamp="${2:?YYYYMMDD required}"
destination="${3:-$HOME/Downloads}"
python3 - "$stage" "$date_stamp" "$destination" <<'PY'
import hashlib,json,pathlib,subprocess,sys,tempfile,zipfile
stage,date_stamp,destination=sys.argv[1:]
assert stage in ('S0','S1','S2','S3','S4','S5')
assert len(date_stamp)==8 and date_stamp.isdigit()
root=pathlib.Path.cwd()
files=sorted(p for p in subprocess.check_output(['git','ls-files','-z']).decode().split('\0') if p)
rows=[]
for name in files:
 p=root/name
 assert p.is_file() and not p.is_symlink(),name
 payload=p.read_bytes()
 rows.append({'path':name,'bytes':len(payload),'sha256':hashlib.sha256(payload).hexdigest()})
manifest={'stage':stage,'date':date_stamp,'commit':subprocess.check_output(['git','rev-parse','HEAD']).decode().strip(),'files':rows,'excludes':['.git','ignored build/cache files','external credentials']}
out=pathlib.Path(destination)/f'bs-mobile-{stage}-{date_stamp}.zip'
out.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
 for row in rows:z.write(root/row['path'],row['path'])
 z.writestr('MANIFEST.json',json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
with zipfile.ZipFile(out) as z, tempfile.TemporaryDirectory(prefix='bs-mobile-verify-') as tmp:
 assert z.testzip() is None
 assert set(z.namelist())=={r['path'] for r in rows}|{'MANIFEST.json'}
 z.extractall(tmp)
 for row in rows:
  p=pathlib.Path(tmp)/row['path']
  assert p.stat().st_size==row['bytes']
  assert hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
print(json.dumps({'zip':str(out),'fileCount':len(rows),'sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'crc':'PASS','cleanExtractionHashes':'PASS','exactFileSet':'PASS'}))
PY
