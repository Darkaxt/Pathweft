"""Inventory the actual restored package graph and selected runtime assets.

Read-only inputs; the output is an explicit provenance deliverable, never a
replacement for NuGet audit or native-library/source qualification.
"""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--assets',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    assets=json.loads(args.assets.read_text('utf-8-sig'))
    caches=[Path(path) for path in assets['packageFolders']]
    packages=[]
    for identity,library in assets['libraries'].items():
        if library['type']!='package':
            continue
        package_id,version=identity.rsplit('/',1)
        directory=next((root/library['path'] for root in caches if (root/library['path']).is_dir()),None)
        if directory is None:
            raise ValueError('Missing actual restored package: '+identity)
        nuspec=next(directory.glob('*.nuspec'))
        metadata=ET.parse(nuspec).getroot().find('{*}metadata')
        def field(name):
            item=metadata.find('{*}'+name)
            return item.text if item is not None else None
        repository=metadata.find('{*}repository')
        license=metadata.find('{*}license')
        targets={}
        for name,target in assets['targets'].items():
            if identity in target:
                item=target[identity]
                targets[name]={key:item[key] for key in ['dependencies','compile','runtime','native','runtimeTargets','build','buildTransitive'] if key in item}
        selected={}
        for target in targets.values():
            for group in ['compile','runtime','native','runtimeTargets']:
                for relative in target.get(group,{}):
                    file=directory/relative
                    if file.is_file() and file.suffix.lower() in ['.dll','.exe']:
                        selected[relative]=hashlib.sha256(file.read_bytes()).hexdigest()
        # Explicit selected APK reference is required because its normal
        # netstandard NuGet asset omits a mandatory embedded resource.
        if package_id.lower()=='quicklook.apkreader':
            file=directory/'lib/net462/QuickLook.ApkReader.dll'
            selected['lib/net462/QuickLook.ApkReader.dll']=hashlib.sha256(file.read_bytes()).hexdigest()
        packages.append(dict(id=package_id,version=version,package_sha512=library['sha512'],
                             repository=repository.attrib if repository is not None else {},
                             license=dict(license.attrib,text=license.text) if license is not None else {},
                             project_url=field('projectUrl'),targets=targets,selected_files=selected))
    report=dict(assets_sha256=hashlib.sha256(args.assets.read_bytes()).hexdigest(),packages=packages)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+'\n','utf-8')
    print(json.dumps([dict(id=p['id'],version=p['version'],repository=p['repository'],license=p['license']) for p in packages],indent=2))

if __name__=='__main__':
    main()
