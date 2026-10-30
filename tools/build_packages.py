"""Build the two deterministic Unity packages. Standard library only; no game/Unity execution."""
from pathlib import Path
import hashlib,json,zipfile,io,tarfile,gzip,uuid,re,argparse
ROOT=Path(__file__).resolve().parents[1]
sha=lambda b:hashlib.sha256(b).hexdigest()
def text(path):
    return path.read_text(encoding='utf-8-sig').replace('\r\n','\n').replace('\r','\n').encode('utf-8')
def meta(path,folder=False):
    kind='MonoImporter' if path.endswith('.cs') else 'AssemblyDefinitionImporter' if path.endswith('.asmdef') else 'TextScriptImporter' if path.endswith(('.md','.txt','.json','.bytes')) else 'DefaultImporter'
    extra='  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' if kind=='MonoImporter' else ''
    return ('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'havermeng-ce12-mods-bootstrap-v2/'+path).hex+'\n'+('folderAsset: yes\n' if folder else '')+kind+':\n  externalObjects: {}\n'+extra+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n').encode()
def tar_bytes(tar,name,data):
    entry=tarfile.TarInfo(name);entry.size=len(data);entry.mode=0o644;entry.mtime=0
    tar.addfile(entry,io.BytesIO(data))
def package(path,files):
    folders=set()
    for name in files:
        p=Path(name).parent
        while p.as_posix()!='Assets':folders.add(p.as_posix());p=p.parent
    with path.open('wb') as target,gzip.GzipFile(filename='',mode='wb',fileobj=target,mtime=0,compresslevel=6) as compressed,tarfile.open(fileobj=compressed,mode='w') as tar:
        for name in sorted(set(files)|folders):
            m=meta(name,name in folders);guid=re.search(rb'^guid: (\w+)',m,re.M)[1].decode()
            directory=tarfile.TarInfo(guid);directory.type=tarfile.DIRTYPE;directory.mode=0o755;directory.mtime=0;tar.addfile(directory)
            if name in files:tar_bytes(tar,guid+'/asset',files[name])
            tar_bytes(tar,guid+'/asset.meta',m);tar_bytes(tar,guid+'/pathname',name.encode())
def payload(folder,manifest):
    buf=io.BytesIO()
    with zipfile.ZipFile(buf,'w',zipfile.ZIP_DEFLATED,compresslevel=7) as z:
        for e in sorted(manifest['entries'],key=lambda e:e['path']):
            if e['kind']!='add':continue
            p=folder/e['blob'];assert p.resolve().is_relative_to(folder.resolve()),e['path']
            data=p.read_bytes()
            if sha(data)!=e['sha256']:raise ValueError('Source hash mismatch; update the manifest intentionally after editing: '+e['path'])
            info=zipfile.ZipInfo(e['blob'],(2026,10,30,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.create_system=3
            z.writestr(info,data,compresslevel=7)
    return buf.getvalue()
def build(output):
    output.mkdir(parents=True,exist_ok=True);results=[]
    shared={'Assets/ScpCeModManager/Editor/'+p.name:text(p) for p in (ROOT/'Sources/Manager').glob('*.cs')}
    shared['Assets/ScpCeModManager/Editor/ScpCeMods.Editor.asmdef']=json.dumps(dict(name='ScpCeMods.Editor',includePlatforms=['Editor'],autoReferenced=True),indent=2).encode()
    shared['Assets/ScpCeModManager/README.md']=b'# SCP:SL CE12 Character Mod Manager\n\nOpen SCP > Mods > Character Mod Manager in Unity.\n\nSource, instructions and validation: https://github.com/Havermeng/scpsl-ce12-character-mods\n'
    for p in (ROOT/'LICENSES').glob('*'):
        if p.is_file():shared['Assets/ScpCeModManager/LicenseNotices/'+p.name]=text(p)
    for folder in sorted((ROOT/'Sources/Content').iterdir()):
        if not folder.is_dir():continue
        raw=text(folder/'manifest.json');m=json.loads(raw)
        files=dict(shared);prefix='Assets/ScpCeMods/'+folder.name+'/'
        files[prefix+'manifest.json']=raw;files[prefix+'payload.zip.bytes']=payload(folder,m)
        for name,src in [('README.md','README.md'),('README_RU.md','README.md'),('README_EN.md','README_EN.md'),('CREDITS.md','CREDITS.md'),('LICENSE.md','LICENSE.md')]:
            files[prefix+name]=text(ROOT/src)
        for name in ['INSTALL_RU.md','INSTALL_EN.md','COMPONENTS.md','VALIDATION.md']:
            files[prefix+'docs/'+name]=text(ROOT/'docs'/name)
        name=folder.name.upper()+'-CE12-'+m['version']+'.unitypackage';p=output/name;package(p,files)
        results.append(dict(id=m['id'],version=m['version'],file=name,bytes=p.stat().st_size,sha256=sha(p.read_bytes()),bootstrapFiles=len(files)))
        print(name,results[-1]['bytes'],results[-1]['sha256'])
    (output/'SHA256SUMS.txt').write_text(''.join(r['sha256']+'  '+r['file']+'\n' for r in results),encoding='ascii')
    (output/'packages.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
    return results
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT/'dist');args=parser.parse_args();build(args.output.resolve())

