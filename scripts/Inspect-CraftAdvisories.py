"""Retain complete primary OSV records for the selected native dependency graph."""
import json, urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]/'.local/evidence/2026-11-04-specialists'
review=json.loads((root/'craft-core-dependency-review.json').read_text())
identities=sorted({v['id'] for result in review['results'] for v in result.get('vulns',[])})
records=[]
for identity in identities:
    with urllib.request.urlopen('https://api.osv.dev/v1/vulns/'+identity) as response:record=json.load(response)
    if record['id']!=identity:raise ValueError('Advisory identity differs')
    records.append(record)
(root/'craft-core-advisories.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
for record in records:print(json.dumps({key:record.get(key) for key in ['id','summary','details','references']},ensure_ascii=True))
