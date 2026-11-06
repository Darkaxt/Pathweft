"""Read-only selected core PE, graph and notice inventory; no Craft binary runs."""
import argparse
import hashlib
import json
from pathlib import Path
import pefile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--core-root', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
manifest = json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
records = []
for app in manifest['apps']:
    members = []
    for member in app['nativeFiles']:
        path = args.core_root/member['path']
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        if digest != member['sha256'] or path.stat().st_size != member['bytes']:
            raise ValueError('Selected native member differs: '+member['path'])
        with pefile.PE(str(path), fast_load=True) as pe:
            pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'], pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT']])
            imports = sorted({item.dll.decode('ascii') for item in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', [])})
            delayed = sorted({item.dll.decode('ascii') for item in getattr(pe, 'DIRECTORY_ENTRY_DELAY_IMPORT', [])})
            if pe.FILE_HEADER.Machine != 0x8664:
                raise ValueError('The selected core is not Windows x64: '+str(path))
            members.append(dict(path=member['path'], sha256=digest, bytes=member['bytes'], imports=imports, delayed=delayed,
                                minimumOs=[pe.OPTIONAL_HEADER.MajorOperatingSystemVersion, pe.OPTIONAL_HEADER.MinorOperatingSystemVersion]))
    graph_path = ROOT/'vendor/Office'/(app['id']+'-core-dependencies.json')
    graph = json.loads(graph_path.read_text())
    missing = [p['name'] for p in graph['packages'] if not p.get('license')]
    if missing:
        raise ValueError('Missing selected package license declaration: '+repr(missing))
    records.append(dict(id=app['id'], version=app['version'], sourceCommit=app['sourceCommit'],
                        graphSha256=hashlib.sha256(graph_path.read_bytes()).hexdigest(), selectedPackages=len(graph['packages']), members=members))
licenses = json.loads((ROOT/'vendor/Office/license-manifest.json').read_text())
for notice in licenses['files']:
    if hashlib.sha256((ROOT/'vendor/Office'/notice['path']).read_bytes()).hexdigest() != notice['sha256']:
        raise ValueError('Original notice digest differs: '+notice['path'])
report = dict(engines=records, originalNotices=len(licenses['files']), scope='Selected normal/build Windows x64 graphs and PE imports; real worker execution is verified separately')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
