"""Stage a narrowly verified edit; never writes to the installed game's save directory."""
from pathlib import Path
from datetime import datetime
import hashlib,json,re,shutil,zipfile,xml.etree.ElementTree as E
source=Path.home()/'Library/Application Support/RimWorld/MpReplays/Autosave-3.zip'
stamp=datetime.now().strftime('%Y%m%d-%H%M%S')
out=Path(__file__).resolve().parent
original=source.read_bytes()
backup=out/f'before-Strange-Signal-{stamp}.zip'
backup.write_bytes(original)
assert hashlib.sha256(backup.read_bytes()).digest()==hashlib.sha256(original).digest()
with zipfile.ZipFile(source) as zin:
 data={i.filename:zin.read(i) for i in zin.infolist()}
 world=data['world/000_save']; root=E.fromstring(world)
 factions=root.findall('.//factionManager/allFactions/li')
 assert any(f.findtext('loadID')=='22' and f.findtext('name')=="Nagi's Worshippers" for f in factions)
 quests=root.findall('.//questManager/quests/li')
 q=next(q for q in quests if q.findtext('id')=='221')
 assert q.findtext('name')=='Strange Signal' and q.findtext('root')=='MonolithMigration'
 assert q.findtext('acceptanceTick') in (None,'-1') and q.findtext('ended') not in ('True','true')
 objects=root.findall('.//worldObjects/worldObjects/li')
 new=next(o for o in objects if o.findtext('ID')=='265')
 old=next(o for o in objects if o.findtext('ID')=='377')
 assert new.findtext('faction')=='Faction_22' and old.findtext('faction')=='Faction_20'
 # Match complete XML elements by their distinctive beginning and next sibling,
 # then change only the one saved quest map reference and its identifying tag.
 text=world.decode('utf-8')
 qmatch=re.search(r'<li>\s*<id>221</id>.*?(?=<li>\s*<id>222</id>)',text,re.S)
 assert qmatch and qmatch.group().count('<mapParent>WorldObject_377</mapParent>')==1
 qt=qmatch.group().replace('<mapParent>WorldObject_377</mapParent>','<mapParent>WorldObject_265</mapParent>')
 text=text[:qmatch.start()]+qt+text[qmatch.end():]
 tag='<li>Quest221.monolithMap</li>'
 assert text.count(tag)==1
 oldmatch=re.search(r'<li Class="Settlement"><def>Settlement</def><tile>191981,0</tile><ID>377</ID>.*?</questTags>',text,re.S)
 assert oldmatch and tag in oldmatch.group()
 text=text[:oldmatch.start()]+oldmatch.group().replace(tag,'')+text[oldmatch.end():]
 newmatch=re.search(r'<li Class="Settlement"><def>Settlement</def><tile>221373,0</tile><ID>265</ID>.*?</questTags>',text,re.S)
 assert newmatch
 text=text[:newmatch.start()]+newmatch.group().replace('</questTags>',tag+'</questTags>')+text[newmatch.end():]
 updated=text.encode('utf-8'); parsed=E.fromstring(updated)
 # Reversing those exact three edits must recover every byte of the original.
 q2=next(q for q in parsed.findall('.//questManager/quests/li') if q.findtext('id')=='221')
 assert q2.findtext('.//mapParent')=='WorldObject_265'
 # Compare entire XML trees after reversing only the authorized fields.
 q2.find('.//mapParent').text='WorldObject_377'
 obs=parsed.findall('.//worldObjects/worldObjects/li')
 n2=next(o for o in obs if o.findtext('ID')=='265');o2=next(o for o in obs if o.findtext('ID')=='377')
 n2.find('questTags').remove(next(x for x in n2.findall('questTags/li') if x.text=='Quest221.monolithMap'))
 t=E.Element('li');t.text='Quest221.monolithMap';o2.find('questTags').insert(0,t)
 assert E.tostring(root)==E.tostring(parsed),'Unexpected XML changes'
 destination=out/f'Nagi-Strange-Signal-fixed-{stamp}.zip'
 with zipfile.ZipFile(destination,'w') as zout:
  for info in zin.infolist():zout.writestr(info,updated if info.filename=='world/000_save' else data[info.filename])
with zipfile.ZipFile(destination) as z:
 assert z.testzip() is None
 assert all(z.read(k)==v for k,v in data.items() if k!='world/000_save')
manifest=dict(source=str(source),source_sha256=hashlib.sha256(original).hexdigest(),backup=str(backup),patched=str(destination),quest=221,old_map_parent=377,new_map_parent=265,faction=22)
(out/'strange-signal-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(manifest,indent=2))
