"""Integration tests against a dedicated fake game directory, never the real game."""
import json,pathlib,os,urllib.request,urllib.error,copy,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
GAME=(ROOT/'tests/game').resolve()
session=next(json.loads(f.read_text()) for f in (pathlib.Path(os.environ['LOCALAPPDATA'])/'PigModSuite').glob('*.json') if pathlib.Path(json.loads(f.read_text())['game']).resolve()==GAME)
URL=session['url'];TOKEN=URL.rstrip('/').split('/')[-1];checks=[]
def request(route,body=None,headers=None,status=200):
    hs={'X-Pig-Token':TOKEN,'Content-Type':'application/json'};hs.update(headers or {})
    req=urllib.request.Request(URL+route,data=None if body is None else json.dumps(body).encode(),headers=hs)
    try:
        with urllib.request.urlopen(req,timeout=15) as r: code=r.status;data=r.read()
    except urllib.error.HTTPError as e:code=e.code;data=e.read()
    assert code==status,(route,code,data[:200])
    try:return json.loads(data)
    except:return data
def check(name,test):assert test,name;checks.append(name)
state=request('api/state');defaults=state['defaults'];catalog=request('catalog.json');items=catalog['items'];cards=[x for x in items if x['module']=='cards' and x['available']]
check('Dazhuzha brand is shown', '大猪扎' in request('index.html').decode('utf-8'))
check('brand logo is served',request('dazhuzha-logo.png')[:8]==b'\x89PNG\r\n\x1a\n')
check('brand icon is served',request('dazhuzha-icon.png')[:8]==b'\x89PNG\r\n\x1a\n')
check('1393 cards, 1376 selectable',len([x for x in items if x['module']=='cards'])==1393 and len(cards)==1376)
check('1955 unique catalog controls',len(items)==len({i['id'] for i in items})==1955)
heroes=[x for x in items if x['module']=='heroes']
check('176 distinct hero skin controls',len(heroes)==176)
ready=[x for x in heroes if x['available']]
check('completed skin pairs are selectable',len(ready)>=21)
for h in ready:
    for part in h['parts']:
        check('preview '+h['id']+' '+part['label'],bool(part['pig']) and request('previews/'+part['pig'])[:4]==b'RIFF')
hprofile=copy.deepcopy(defaults);hprofile['items'][ready[0]['id']]=False
check('individual skin can be disabled',request('api/validate',hprofile)['items']==hprofile['items'])
pending=[x for x in heroes if not x['available']]
if pending:
    hprofile=copy.deepcopy(defaults);hprofile['items'][pending[0]['id']]=True
    request('api/validate',hprofile,status=400);checks.append('pending skin cannot be enabled')
else:
    check('all 176 skin pairs available',len(ready)==176)
    hprofile=copy.deepcopy(defaults);hprofile['items'][ready[-1]['id']]=False
    check('last completed skin can be independently disabled',request('api/validate',hprofile)['items']==hprofile['items'])

profile=copy.deepcopy(defaults);profile['items'][cards[0]['id']]=False
saved=request('api/save',{'settings':profile,'revision':state['revision']})
check('one card disabled without affecting others',saved['state']['settings']['items']=={cards[0]['id']:False})
check('save persisted to disk',json.loads((GAME/'BepInEx/config/PigModSuite/settings.json').read_text())['items']==profile['items'])
request('api/save',{'settings':defaults,'revision':state['revision']},status=409);checks.append('stale revision rejected')
profile['modules']['cards']=False
saved2=request('api/save',{'settings':profile,'revision':saved['state']['revision']})
check('module toggle preserves item preference',saved2['state']['settings']['items']==profile['items'] and not saved2['state']['settings']['modules']['cards'])
check('atomic save keeps previous backup',json.loads(pathlib.Path(saved2['backup']).read_text())['modules']['cards'] is True)
check('profile validate/import roundtrip',request('api/validate',profile)==profile)
invalid=copy.deepcopy(defaults);invalid['items']['cards:unknown']=False
request('api/validate',invalid,status=400);checks.append('unknown IDs rejected')
invalid=copy.deepcopy(defaults);invalid['items'][next(x['id'] for x in items if not x['available'])]=True
request('api/validate',invalid,status=400);checks.append('locked concepts cannot be enabled')
for bad in [None,True,2,'1']:
    invalid=copy.deepcopy(defaults);invalid['schemaVersion']=bad
    request('api/validate',invalid,status=400)
checks.append('invalid schema types rejected')
invalid=copy.deepcopy(defaults);invalid['modules']['cards']='false'
request('api/validate',invalid,status=400);checks.append('non-boolean settings rejected')
request('api/save',{'settings':profile,'revision':saved2['state']['revision']},headers={'X-Pig-Token':'invalid'},status=403)
request('api/save',{'settings':profile,'revision':saved2['state']['revision']},headers={'Origin':'https://example.invalid'},status=403)
checks.append('missing token and foreign origin rejected')
request('previews/../../src/Configurator.cs',status=404);checks.append('non-catalog path rejected')
png=request('previews/'+cards[0]['parts'][0]['pig']);check('real preview served',png[:4]==b'RIFF')
cfg=GAME/'BepInEx/config/PigModSuite/settings.json';cfg.write_text('{corrupt',encoding='utf-8')
broken=request('api/state');check('corrupt configuration shown explicitly',bool(broken['warning']))
fixed=request('api/save',{'settings':defaults,'revision':broken['revision']})
check('corrupt original retained in backup',pathlib.Path(fixed['backup']).read_text()=='{corrupt')
check('corrupt configuration repair clears warning',not fixed['state']['warning'])
# First-run migration respects existing per-plugin .cfg values.
cfg.rename(cfg.with_suffix('.saved-fixture'))
legacy=cfg.parents[1]/'local.pigcardart.cfg';legacy.write_text('[Animation]\nEnabled = true\n[General]\nEnabled = false\n')
migrated=request('api/state');check('legacy migration respects General only',not migrated['settings']['modules']['cards'] and migrated['revision']=='missing')
cfg.with_suffix('.saved-fixture').rename(cfg)
request('api/save',{'settings':defaults,'revision':request('api/state')['revision']})
report={'passed':len(checks),'checks':checks,'scope':'Isolated test game; no live game state altered.'}
(ROOT/'tests/api-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
