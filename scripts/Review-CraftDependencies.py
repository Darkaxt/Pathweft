"""Review feature/Windows-target selected core graphs with OSV; no restore/build/update."""
from collections import defaultdict
from datetime import datetime, timezone
import json
from pathlib import Path
import urllib.request

PROJECT=Path(__file__).resolve().parents[1]
EVIDENCE=PROJECT/'.local/evidence/2026-11-04-specialists'
queries={}
closures={}
for app in ('wordcraft','gridcraft','deckcraft'):
    graph=json.loads((PROJECT/f'vendor/Office/{app}-core-dependencies.json').read_text())
    if graph['target']!='x86_64-pc-windows-msvc' or graph['root']!='pathweft-'+app:raise ValueError('Unexpected bridge/target graph')
    closures[app]=dict(root=graph['root'],packages=graph['packages'],limitation=graph['scope'])
    for package in graph['packages']:
        name,version,source=package['name'],package['version'],package.get('source') or 'workspace'
        if source.startswith('registry+'):
            queries[('crates.io',name,version)]={'package':{'name':name,'ecosystem':'crates.io'},'version':version}
        elif source.startswith('git+') and '#' in source:
            commit=source.rsplit('#',1)[1]
            queries[('git',source,commit)]={'commit':commit}
packages=json.loads((PROJECT/'vendor/Specialists/runtime-manifest.json').read_text())['packages']
for package in packages:
    queries[('NuGet',package['id'],package['version'])]={'package':{'name':package['id'],'ecosystem':'NuGet'},'version':package['version']}
ordered=list(queries.items())
results=[]
for offset in range(0,len(ordered),500):
    batch=ordered[offset:offset+500]
    request=urllib.request.Request('https://api.osv.dev/v1/querybatch',data=json.dumps({'queries':[q for _,q in batch]}).encode(),headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(request) as response:payload=json.load(response)
    if len(payload['results'])!=len(batch):raise ValueError('OSV response coverage differs')
    results.extend(dict(identity=key,query=query,**result) for (key,query),result in zip(batch,payload['results']))
report=dict(checked_utc=datetime.now(timezone.utc).isoformat(),source='https://api.osv.dev/v1/querybatch',closures=closures,results=results)
(EVIDENCE/'craft-core-dependency-review.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
findings=[r for r in results if r.get('vulns')]
print(json.dumps(dict(coverage=len(results),core_graphs={name:len(c['packages']) for name,c in closures.items()},findings=findings),indent=2))
