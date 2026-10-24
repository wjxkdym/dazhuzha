"""Build distributable ZIPs from a complete local suite. No network or game mutation."""
import argparse,hashlib,json,re,shutil,zipfile
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

REPO=Path(__file__).resolve().parents[1]
PLUGINS={'PigCardArt':('cards.json','cards'),'PigEncounterArt':('encounters.json','artworks'),'PigPortraitArt':('portraits.json','heroes'),'PigMainMenuArt':('layers.json','layers'),'PigStorageArt':('storage.json',None)}
def read(path):return json.loads(path.read_text('utf-8-sig'))
def digest(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def safe(root,rel):
    path=(root/rel).resolve()
    if not path.is_relative_to(root.resolve()) or path==root.resolve():raise ValueError('Path escapes root: '+str(rel))
    return path
def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--suite',type=Path,required=True);ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args();suite=args.suite.resolve();out=args.output.resolve()
    status=read(REPO/'release-status.json');version=status['version']
    assert re.fullmatch(r'[0-9A-Za-z.-]+',version)
    package_name='Dazhuzha-v'+version;stage=out/package_name
    if stage.exists():raise ValueError('Use a fresh output directory; existing release is preserved.')
    out.mkdir(parents=True,exist_ok=True);stage.mkdir()
    selected={};art_hashes={};catalog=read(suite/'Configurator/catalog.json')
    def add(rel):
        p=safe(suite,rel)
        if not p.is_file():raise FileNotFoundError(rel)
        selected[rel]=p
    for name,(manifest,key) in PLUGINS.items():
        prefix='BepInEx/plugins/'+name+'/'
        add(prefix+name+'.dll');add(prefix+manifest)
        data=read(suite/(prefix+manifest));rows=data[key] if key else [data]
        for row in rows:
            for field,hfield in [('file','sha256'),('fullFile','fullSha256')]:
                if field not in row:continue
                rel=prefix+('art/' if key else '')+row[field]
                add(rel);art_hashes[rel]=row[hfield]
    for p in (suite/'Configurator/web').iterdir():
        if p.is_file() and p.suffix in {'.html','.css','.js','.png','.ico'}:add(p.relative_to(suite).as_posix())
    add('Configurator/PigModConfigurator.exe');add('Configurator/catalog.json')
    for item in catalog['items']:
        for part in item['parts']:
            for key in ['original','pig']:
                if part.get(key):add('Configurator/previews/'+part[key])
    add('Open-PigModConfigurator.cmd')
    hero_rows=read(suite/'BepInEx/plugins/PigPortraitArt/portraits.json')['heroes']
    ready=[x for x in catalog['items'] if x['module']=='heroes' and x['available']]
    assert len(hero_rows)==len(ready)==status['heroSkins']['completed']
    assert len({x['assetName'] for x in hero_rows})==len(hero_rows)
    assert len(catalog['items'])==len({x['id'] for x in catalog['items']})==status['catalogControls']
    for p in selected.values():
        if p.suffix in {'.json','.html','.js','.css'}:
            content=p.read_text('utf-8-sig')
            assert not re.search(r'(?i)[A-Z]:[\\/]|(?:localhost|127\.0\.0\.1):\d{2,}',content),'Private local data in '+p.name
    copy_jobs=[]
    for rel,src in sorted(selected.items()):
        dst=safe(stage,rel)
        dst.parent.mkdir(parents=True,exist_ok=True)
        copy_jobs.append((rel,src,dst))
    def copy_record(pair):
        rel,src,dst=pair;sha=digest(src)
        if rel in art_hashes:assert sha==art_hashes[rel],'Artwork hash mismatch: '+rel
        shutil.copy2(src,dst)
        target='PigModConfigurator/'+rel[len('Configurator/'):] if rel.startswith('Configurator/') else rel
        return {'source':rel,'target':target,'sha256':sha,'bytes':src.stat().st_size}
    with ThreadPoolExecutor(max_workers=4) as pool:records=list(pool.map(copy_record,copy_jobs))
    for name in ['Install.ps1','安装大猪扎.cmd','打开大猪扎配置器.cmd','README.md','RELEASE_NOTES.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md','LICENSE','release-status.json']:
        shutil.copy2(REPO/name,stage/name)
    shutil.copytree(REPO/'docs',stage/'docs')
    (stage/'package-files.json').write_text(json.dumps({'schemaVersion':1,'version':'1.1.0','brand':'大猪扎','releaseVersion':version,'files':records},ensure_ascii=False,indent=2),'utf-8')
    expected={r['source']:r for r in records}
    assert not any(p.name in {'installation.json','settings.json'} for p in stage.rglob('*'))
    limit=int(1.5*1024**3);core=[];groups=[];current=[];size=0
    for p in sorted(stage.rglob('*')):
        if not p.is_file():continue
        rel=p.relative_to(stage).as_posix()
        if '/art/' in rel or rel.startswith('Configurator/previews/'):
            if current and size+p.stat().st_size>limit:groups.append(current);current=[];size=0
            current.append(p);size+=p.stat().st_size
        else:core.append(p)
    if current:groups.append(current)
    parts=[('Core',core)]+[('Assets-%02d'%(i+1),g) for i,g in enumerate(groups)]
    archives=[];verified=set();count=0
    for suffix,files in parts:
        dest=out/(package_name+'-'+suffix+'.zip')
        with zipfile.ZipFile(dest,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as z:
            for f in files:
                rel=f.relative_to(stage).as_posix();z.write(f,package_name+'/'+rel,compress_type=zipfile.ZIP_STORED if f.suffix in {'.png','.webp'} else zipfile.ZIP_DEFLATED)
        assert dest.stat().st_size<2*1024**3
        with zipfile.ZipFile(dest) as z:
            assert z.testzip() is None
            for info in z.infolist():
                rel=info.filename[len(package_name)+1:]
                assert rel not in verified;verified.add(rel)
                with z.open(info) as f:sha=hashlib.file_digest(f,'sha256').hexdigest()
                if rel in expected:assert sha==expected[rel]['sha256']
                else:assert sha==digest(stage/rel)
                count+=1
        archives.append({'file':dest.name,'bytes':dest.stat().st_size,'sha256':digest(dest),'files':len(files)})
        print('Verified '+dest.name+' ('+str(len(files))+' files)',flush=True)
    assert set(expected).issubset(verified)
    (out/'SHA256SUMS.txt').write_text(''.join(a['sha256']+'  '+a['file']+'\n' for a in archives),'utf-8')
    shutil.copy2(REPO/'RELEASE_NOTES.md',out/'RELEASE_NOTES.md')
    report={'version':version,'brand':'大猪扎','heroSkins':status['heroSkins'],'catalogControls':len(catalog['items']),'runtimeArtworksVerified':len(art_hashes),'allArchiveFilesVerified':count,'privateStateExcluded':True,'originalPngBytesPreserved':True,'archives':archives,'publishedToGitHub':False,'inGameVisualVerified':False}
    (out/'release-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps(report,ensure_ascii=False),flush=True)
if __name__=='__main__':main()
