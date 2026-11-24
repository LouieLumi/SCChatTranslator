"""Fetch pinned Windows dependencies and assemble the staging payload."""
import argparse,hashlib,json,shutil,urllib.request,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def extract(archive,destination):
    destination.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        for entry in z.infolist():
            out=(destination/entry.filename).resolve()
            if not out.is_relative_to(destination.resolve()):raise ValueError('Unsafe archive path')
        z.extractall(destination)

def main():
    manifest=json.loads((ROOT/'dependencies.json').read_text())
    cache=ROOT/'.cache';cache.mkdir(exist_ok=True)
    payload=ROOT/'build/payload';payload.mkdir(parents=True,exist_ok=True)
    for name,item in manifest.items():
        file=cache/name
        if not file.exists() or hashlib.sha256(file.read_bytes()).hexdigest()!=item['sha256']:
            print('Downloading',name,flush=True);temp=file.with_suffix('.download')
            with urllib.request.urlopen(item['url'],timeout=120) as response,temp.open('wb') as output:shutil.copyfileobj(response,output)
            if hashlib.sha256(temp.read_bytes()).hexdigest()!=item['sha256']:raise ValueError('SHA-256 mismatch: '+name)
            temp.replace(file)
        target=payload/('runtime/Lib/site-packages' if item['kind']=='wheel' else 'models' if item['kind']=='model' else 'runtime')
        extract(file,target)
    (payload/'runtime/python312._pth').write_text('python312.zip\n.\nLib\\site-packages\n..\nimport site\n')
    for p in (ROOT/'src').iterdir():
        if p.suffix in {'.py','.xaml','.ico','.json','.txt'}:shutil.copy2(p,payload/p.name)
    for name in ('LICENSE','README.md','THIRD_PARTY_NOTICES.md','dependencies.json'):shutil.copy2(ROOT/name,payload/name)
    if (ROOT/'licenses').exists():shutil.copytree(ROOT/'licenses',payload/'licenses',dirs_exist_ok=True)
    print('Prepared',payload)
if __name__=='__main__':main()
