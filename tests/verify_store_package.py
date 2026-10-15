"""Compare a generated MSIX with its exact staged and source payloads."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('output', type=Path)
parser.add_argument('--source-payload', type=Path, required=True)
parser.add_argument('--require-store-identity', action='store_true')
args = parser.parse_args()
meta = json.loads((args.output / 'store-build-info.json').read_text(encoding='utf-8-sig'))
package = args.output / meta['package']
assert package.name == meta['package'] and package.suffix == '.msix'
assert package.stat().st_size == meta['bytes']
assert hashlib.sha256(package.read_bytes()).hexdigest() == meta['sha256']
assert not meta['signed'] and not meta['submitted']
source = {p.relative_to(args.source_payload).as_posix(): p for p in args.source_payload.rglob('*') if p.is_file()}
staged = {p.relative_to(args.output / 'payload').as_posix(): p for p in (args.output / 'payload').rglob('*') if p.is_file()}
assert set(staged) == set(source) | {'AppxManifest.xml'}
for name, path in source.items():
    if name != 'AppxManifest.xml':
        assert staged[name].read_bytes() == path.read_bytes(), name
for name in staged:
    assert Path(name).suffix.lower() not in {'.jpg', '.jpeg', '.heic', '.heif', '.jxr', '.db', '.sqlite', '.log', '.pfx', '.key', '.zip'}, name
with zipfile.ZipFile(package) as z:
    assert z.testzip() is None
    names = {n.replace('\\', '/'): n for n in z.namelist()}
    assert 'AppxSignature.p7x' not in names
    assert set(names) == set(staged) | {'AppxBlockMap.xml', '[Content_Types].xml'}
    for name, path in staged.items():
        # MakeAppx normalizes the manifest XML while preserving its semantics.
        if name != 'AppxManifest.xml':
            assert z.read(names[name]) == path.read_bytes(), name
    manifest = ET.fromstring(z.read(names['AppxManifest.xml']))
    identity = manifest.find('{*}Identity')
    assert identity.attrib['Name'] == meta['identity']['name']
    assert identity.attrib['Publisher'] == meta['identity']['publisher']
    assert identity.attrib['Version'] == meta['version'] and identity.attrib['ProcessorArchitecture'] == 'x64'
    assert {n.text for n in manifest.findall('.//{*}FileType')} == {'.jpg', '.jpeg', '.png', '.bmp', '.gif', '.tif', '.tiff', '.jxr', '.wdp', '.hdp', '.ico', '.heic', '.heif'}
    assert {n.attrib['Name'] for n in manifest.findall('.//{*}Capability')} == {'runFullTrust'}
    app = manifest.find('.//{*}Application')
    assert app.attrib['Executable'] == 'Luma.App.exe' and app.attrib['EntryPoint'] == 'Windows.FullTrustApplication'
    logos = [manifest.find('.//{*}Properties/{*}Logo').text]
    visual = manifest.find('.//{*}VisualElements')
    logos += [visual.attrib['Square150x150Logo'], visual.attrib['Square44x44Logo']]
    assert all(logo.replace('\\', '/') in names for logo in logos)
if args.require_store_identity:
    assert meta['storeIdentityConfigured'] and meta['mode'] == 'submission-candidate'
    assert meta['sourceCommit'] == meta['packagingCommit']
    assert meta['identity']['publisher'] != 'CN=Luma Development'
build = json.loads((args.source_payload / 'build-info.json').read_text(encoding='utf-8-sig'))
assert build['commit'] == meta['sourceCommit'] and build['variant'] == 'rtx'
for name, digest in build['nvidia']['runtimeSha256'].items():
    assert hashlib.sha256(staged[name].read_bytes()).hexdigest() == digest
print(f"PASS: {len(staged)} MSIX payload files; exact source bytes, 13 associations, original NVIDIA components, manifest, logos and SHA256. Mode: {meta['mode']}; no Store installation/certification claimed.")
