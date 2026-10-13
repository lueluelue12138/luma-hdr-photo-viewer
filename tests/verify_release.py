"""Verify distributable bytes and explicit RTX/standard capability labeling."""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--variant', choices=['standard', 'rtx'], required=True)
args = p.parse_args()
root = Path(__file__).resolve().parents[1]
payload = root / f'artifacts/release-payload-{args.variant}'
output = root / f'artifacts/release-{args.variant}'
metadata = json.loads((payload/'build-info.json').read_text(encoding='utf-8-sig'))
assert metadata['variant'] == args.variant
assert metadata['rtxHdr'] == metadata['rtxVsr'] == (args.variant == 'rtx')
version = metadata['version']
zip_path = output/f'Luma-{version}-windows-x64-{args.variant}-portable.zip'
installer = output/f'Luma-{version}-windows-x64-{args.variant}-setup.exe'
assert installer.read_bytes()[:2] == b'MZ'
files = sorted(p for p in payload.rglob('*') if p.is_file())
for file in files:
    assert file.suffix.lower() not in {'.db', '.sqlite', '.log', '.lth', '.jpg', '.jpeg', '.jxr', '.zip', '.pfx', '.pem'}, file.name
assert (payload/'licenses/dotnet-LICENSE.txt').exists()
assert (payload/'licenses/dotnet-ThirdPartyNotices.txt').exists()
with zipfile.ZipFile(zip_path) as z:
    assert z.testzip() is None
    assert set(z.namelist()) == {f.relative_to(payload).as_posix() for f in files}
    for file in files: assert z.read(file.relative_to(payload).as_posix()) == file.read_bytes(), file.name
for name in ['nvngx_truehdr.dll', 'nvngx_vsr.dll', 'NVIDIA_RTX_Video_SDK_License.pdf']:
    assert (payload/name).exists() == (args.variant == 'rtx')
    if args.variant == 'rtx':
        source = root/'external/nvidia/sdk'/('bin/Windows/x64/rel' if name.endswith('.dll') else '')/name
        assert (payload/name).read_bytes() == source.read_bytes()
        assert metadata['nvidia']['runtimeSha256'][name] == hashlib.sha256((payload/name).read_bytes()).hexdigest()
terms = payload/'licenses/NVIDIA-runtime-terms.rtf'
assert terms.exists() == (args.variant == 'rtx')
if args.variant == 'rtx':
    assert terms.read_bytes() == (root/'packaging/RTX-EULA.rtf').read_bytes()
    assert terms.read_bytes().startswith(b'{\\rtf1')
    assert b'NVIDIA_RTX_Video_SDK_License.pdf' in terms.read_bytes()
    assert metadata['nvidia']['termsSha256'] == hashlib.sha256(terms.read_bytes()).hexdigest()
    # An SDK input archive, developer header/library or fixture must never be
    # included in the consumer bundle, even though CI has a private SDK checkout.
    assert not any(f.suffix.lower() in {'.lib','.h','.hpp','.obj','.exp'} or f.name=='HdrFixture.exe' for f in files)
for line in (output/'SHA256SUMS.txt').read_text().splitlines():
    digest, name = line.split('  ', 1)
    assert hashlib.sha256((output/name).read_bytes()).hexdigest() == digest
print(f'PASS: {len(files)} portable payload files match exactly; {args.variant} labels, runtime licenses, EXE header and SHA256 sums verified.')
