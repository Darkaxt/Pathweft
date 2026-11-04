"""Inspect extracted official PE exports without loading the Office runtime."""
import json
from pathlib import Path
import pefile

ROOT=Path(__file__).resolve().parents[1]
runtime=Path('D:/Temp/pathweft-s13-s9-s11/office-fallback/runtime')
results=[]
for name in ('mergedlo.dll','sofficeapp.dll','soffice.bin','sal3.dll'):
    path=runtime/'program'/name
    if not path.exists():continue
    with pefile.PE(str(path),fast_load=True) as pe:
        pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT'],pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT']])
        exports=[s.name.decode('ascii') for s in getattr(getattr(pe,'DIRECTORY_ENTRY_EXPORT',None),'symbols',[]) if s.name]
        results.append(dict(file=name,bytes=path.stat().st_size,exports=[n for n in exports if n.startswith(('lok_','libreofficekit_','cppu_'))],imports=[i.dll.decode('ascii') for i in getattr(pe,'DIRECTORY_ENTRY_IMPORT',[])]))
output=ROOT/'.local/evidence/2026-11-04-specialists/libreoffice-core-exports.json'
output.write_text(json.dumps(results,indent=2)+'\n')
print(output.read_text())
