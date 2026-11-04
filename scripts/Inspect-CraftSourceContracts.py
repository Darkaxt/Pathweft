"""Bounded source evidence for native Office read/import/launch contracts."""
import json
from pathlib import Path
import re
import zipfile

PROJECT=Path(__file__).resolve().parents[1]
manifest=json.loads((PROJECT/'vendor/Office/runtime-manifest.json').read_text())
patterns={
    'filesystem_zip_extraction':re.compile(r'\.extract\(|ZipStreamReader|by_(index|name|path)_decrypt'),
    'native_or_network_calls':re.compile(r'Command::new|reqwest::|ureq::|TcpStream::connect'),
    'ambient_password_or_preferences':re.compile(r'(WORDCRAFT|GRIDCRAFT)_(PASSWORD|NO_PREFS)|refresh.*[Ll]ink|update.*[Ll]ink'),
}
evidence=[]
for app in manifest['apps']:
    matches=[]
    with zipfile.ZipFile(Path('D:/BuildCache/Pathweft/office/sources')/app['sourceArchive']) as source:
        for member in source.infolist():
            name=member.filename.split('/',1)[-1]
            if not name.endswith('.rs') or not name.startswith(('crates/','apps/')):continue
            for number,line in enumerate(source.read(member).decode('utf-8').splitlines(),1):
                for kind,pattern in patterns.items():
                    if pattern.search(line):matches.append(dict(kind=kind,file=name,line=number,text=line.strip()))
    evidence.append(dict(app=app['id'],source=app['sourceCommit'],matches=matches))
output=PROJECT/'.local/evidence/2026-11-04-specialists/craft-source-contracts.json'
output.write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
for app in evidence:
    print(json.dumps(dict(app=app['app'],matches=app['matches']),ensure_ascii=False))
