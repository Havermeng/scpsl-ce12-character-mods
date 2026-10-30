"""Validate Unity-package boundaries, metadata, payload hashes and shared manager identity."""
from pathlib import Path
import hashlib,json,tarfile,zipfile,io,re
ROOT=Path(__file__).resolve().parents[1]
sha=lambda b:hashlib.sha256(b).hexdigest()
def audit(output):
    reports=[];shared=None
    for p in sorted(output.glob('*.unitypackage')):
        with tarfile.open(p,'r:gz') as tar:
            members=tar.getmembers();names=[m.name for m in members]
            assert len(names)==len(set(names)),'duplicate tar member'
            assert all(not m.issym() and not m.islnk() and not m.name.startswith('/') and '..' not in Path(m.name).parts for m in members)
            paths={}
            for m in members:
                if m.name.endswith('/pathname'):
                    assert re.fullmatch('[0-9a-f]{32}',m.name.split('/')[0])
                    name=tar.extractfile(m).read().decode('utf-8')
                    assert name.startswith(('Assets/ScpCeModManager','Assets/ScpCeMods')) and '..' not in Path(name).parts
                    paths[m.name.rsplit('/',1)[0]]=name
            assert len(set(paths.values()))==len(paths)
            files={name:tar.extractfile(uid+'/asset').read() for uid,name in paths.items() if uid+'/asset' in names}
            for uid,name in paths.items():
                meta=tar.extractfile(uid+'/asset.meta').read();assert ('guid: '+uid).encode() in meta
            manager={name:sha(b) for name,b in files.items() if name.startswith('Assets/ScpCeModManager/')}
            if shared is not None:assert manager==shared,'shared manager mismatch'
            shared=manager
            raw=next(b for name,b in files.items() if name.startswith('Assets/ScpCeMods/') and name.endswith('/manifest.json'))
            manifest=json.loads(raw);assert manifest['schema']==2
            payload=next(b for name,b in files.items() if name.endswith('/payload.zip.bytes'))
            targets=set()
            for e in manifest['entries']:
                assert e['path'].startswith(('Assets/','Translations/')) and '\\' not in e['path'] and '..' not in Path(e['path']).parts
                assert e['path'].casefold() not in targets;targets.add(e['path'].casefold())
                assert e['kind'] in ['add','patch','rows']
                if e['kind']!='add':assert re.fullmatch('[0-9a-f]{64}',e['baseNormalizedSha256'])
            with zipfile.ZipFile(io.BytesIO(payload)) as z:
                assert len(z.namelist())==len(set(z.namelist()))
                assert set(z.namelist())=={e['blob'] for e in manifest['entries'] if e['kind']=='add'},'full source patch blob or extra payload'
                for e in manifest['entries']:
                    if e['kind']!='add':continue
                    b=z.read(e['blob']);assert sha(b)==e['sha256'],e['path']
                    assert not e['path'].lower().endswith(('.dll','.exe','.pdb','.ps1','.bat','.cmd','.sh','.env')),'unexpected executable'
                    if e['path'].endswith(('.cs','.txt','.json','.shader')):
                        s=b.decode('utf-8-sig')
                        assert not re.search(r'[A-Za-z]:[\\/]+Users[\\/]',s),'private user path: '+e['path']
                        assert not re.search(r'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|sk-[A-Za-z0-9]{32,})',s),'credential-like string'
            report=dict(file=p.name,id=manifest['id'],version=manifest['version'],bytes=p.stat().st_size,sha256=sha(p.read_bytes()),bootstrapFiles=len(files),payloadEntries=len(targets),patches=sum(e['kind']=='patch' for e in manifest['entries']),passed=True)
            reports.append(report);print(json.dumps(report))
    assert len(reports)==2,'expected exactly two packages'
    expected={}
    for line in (output/'SHA256SUMS.txt').read_text().splitlines():
        digest,name=line.split('  ',1);expected[name]=digest
    assert expected=={r['file']:r['sha256'] for r in reports}
    report=dict(result='PASS',packages=reports,sharedManagerIdentical=True)
    (output/'package_audit.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return report
if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=ROOT/'dist');audit(p.parse_args().output.resolve())

