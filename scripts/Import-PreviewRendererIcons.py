"""Import unmodified renderer app icons from the exact qualified source/runtime pins."""
import hashlib
import json
from pathlib import Path
import struct
import zipfile
import pefile

ROOT=Path(__file__).resolve().parents[1]
vendor=ROOT/'vendor/Office/icons';vendor.mkdir(exist_ok=True)
manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
records=[]
for app in manifest['apps']:
    archive=Path('D:/BuildCache/Pathweft/office/sources')/app['sourceArchive']
    if hashlib.sha256(archive.read_bytes()).hexdigest()!=app['sourceSha256']:raise ValueError('Craft source pin differs')
    member='assets/app-icon/'+app['id']+'.ico'
    with zipfile.ZipFile(archive) as source:
        matches=[name for name in source.namelist() if name.split('/',1)[-1]==member]
        if len(matches)!=1:raise ValueError('Expected one app icon')
        content=source.read(matches[0])
    (vendor/(app['id']+'.ico')).write_bytes(content)
    records.append(dict(id=app['id'],source=app['sourceUrl'],member=member,sha256=hashlib.sha256(content).hexdigest(),license='MIT OR Apache-2.0; see the pinned ATTRIBUTION.md app-icon entries'))

runtime=Path('D:/BuildCache/Pathweft/office/libreoffice/runtime/program/soffice.exe')
fallback=json.loads((ROOT/'vendor/Office/libreoffice-runtime-manifest.json').read_text())
expected=next(f for f in fallback['files'] if f['path']=='program/soffice.exe')['sha256']
if hashlib.sha256(runtime.read_bytes()).hexdigest()!=expected:raise ValueError('LibreOffice icon input differs')
with pefile.PE(str(runtime)) as pe:
    types={entry.id:entry for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries}
    group=sorted(types[14].directory.entries,key=lambda e:e.id or 0)[0]
    entry=group.directory.entries[0].data.struct
    data=pe.get_data(entry.OffsetToData,entry.Size)
    reserved,kind,count=struct.unpack_from('<HHH',data)
    if reserved!=0 or kind!=1 or count<1:raise ValueError('Invalid app icon group')
    chunks=[];directory=bytearray(struct.pack('<HHH',0,1,count));offset=6+count*16
    icons={item.id:item for item in types[3].directory.entries}
    for index in range(count):
        width,height,colors,res,bits,depth,size,identity=struct.unpack_from('<BBBBHHIH',data,6+index*14)
        resource=icons[identity].directory.entries[0].data.struct
        chunk=pe.get_data(resource.OffsetToData,resource.Size)
        if len(chunk)!=size:raise ValueError('App icon resource length differs')
        directory.extend(struct.pack('<BBBBHHII',width,height,colors,res,bits,depth,size,offset));chunks.append(chunk);offset+=size
    content=bytes(directory)+b''.join(chunks)
(vendor/'libreoffice.ico').write_bytes(content)
records.append(dict(id='libreoffice',source=fallback['msiUrl'],member='program/soffice.exe RT_GROUP_ICON '+str(group.id),runtimeSha256=expected,sha256=hashlib.sha256(content).hexdigest(),license='Unmodified LibreOffice identification icon; see bundled LibreOffice LICENSE.html and NOTICE'))
(vendor/'source-manifest.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print(json.dumps(records))
