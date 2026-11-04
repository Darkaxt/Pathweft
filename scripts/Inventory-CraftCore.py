"""Record the target/feature-selected Cargo graph of each actual native bridge."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tomllib

ROOT=Path(__file__).resolve().parents[1]
scratch=Path('D:/Temp/pathweft-s13-s9-s11/craft-cores')
cache=Path('D:/BuildCache/Pathweft')
compiler=cache/'rustup-craft/toolchains/1.95.0-x86_64-pc-windows-msvc/bin'
environment=os.environ.copy()
environment.update(CARGO_HOME=str(cache/'cargo'),RUSTUP_HOME=str(cache/'rustup-craft'),
    RUSTC=str(compiler/'rustc.exe'),TEMP=str(scratch/'temp'),TMP=str(scratch/'temp'))
for engine in ('wordcraft','gridcraft','deckcraft'):
    donor=scratch/'sources'/engine
    command=[str(compiler/'cargo.exe'),'tree','--offline','--locked','--target','x86_64-pc-windows-msvc',
             '--edges','normal,build','--prefix','none','--format','{p}|{l}',
             '--package','pathweft-'+engine,'--manifest-path',str(donor/'Cargo.toml')]
    result=subprocess.run(command,cwd=donor,env=environment,capture_output=True,text=True,check=True)
    packages={}
    for line in result.stdout.splitlines():
        if line.endswith(' (*)'):line=line[:-4]
        identity,license=line.rsplit('|',1)
        parts=identity.split(' ',2)
        name,version=parts[0],parts[1].removeprefix('v')
        # Workspace paths differ across machines; source commit is recorded by
        # the runtime manifest. Preserve the actual lock identities below.
        packages[(name,version)]=dict(name=name,version=version,license=license)
    lock=tomllib.loads((donor/'Cargo.lock').read_text())
    for entry in lock['package']:
        key=(entry['name'],entry['version'])
        if key in packages:packages[key].update({k:entry[k] for k in ('source','checksum') if k in entry})
    report=dict(engine=engine,root='pathweft-'+engine,target='x86_64-pc-windows-msvc',
        edges='normal,build',scope='Feature/target-selected build graph. Build-time packages are included; this is not a claim that all code is linked.',
        sourceLockSha256=hashlib.sha256((donor/'Cargo.lock').read_bytes()).hexdigest(),
        packages=sorted(packages.values(),key=lambda p:(p['name'],p['version'])))
    (ROOT/f'vendor/Office/{engine}-core-dependencies.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(engine=engine,packages=len(packages),missingLicenses=[p for p in packages.values() if not p['license'] or p['license']=='UNKNOWN'])),flush=True)
