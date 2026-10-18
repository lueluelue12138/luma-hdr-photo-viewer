"""Check every reachable historical blob before publishing original source."""
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
lines = subprocess.check_output(['git', 'rev-list', '--objects', 'HEAD'], cwd=ROOT).decode().splitlines()
files = [(line.split(' ', 1)) for line in lines if ' ' in line]
for entry in subprocess.check_output(['git', 'ls-files', '--stage', '-z'], cwd=ROOT).decode().split('\0'):
    if not entry: continue
    info, name = entry.split('\t', 1)
    _, oid, stage = info.split()
    if stage != '0': raise RuntimeError('Resolve index conflicts before publishing.')
    files.append([oid, name])
forbidden_suffixes = {'.exe', '.dll', '.lib', '.exp', '.obj', '.zip', '.pfx', '.p12', '.pem', '.key', '.db', '.sqlite', '.lth', '.jpg', '.jpeg', '.jxr', '.heic', '.heif', '.wdp', '.hdp', '.avif', '.arw', '.mp4', '.mov', '.m4v', '.livp'}
errors = []
# The homepage hero is one AVIF with an HDR gain map (the maintainer's own photo, see docs/WEBSITE.md); only that folder may hold .avif.
site_avif = lambda name: name.startswith('site/assets/') and name.lower().endswith('.avif')
for oid, name in files:
    p = Path(name)
    if name.startswith(('artifacts/', '.tools/', 'external/nvidia/sdk/')) or (p.suffix.lower() in forbidden_suffixes and not site_avif(name)) or p.name.startswith('.env'):
        errors.append((name, 'generated/private/proprietary payload tracked in history'))

patterns = [
    re.compile(rb'gh[pousr]_[A-Za-z0-9]{36,}'),
    re.compile(rb'github_pat_[A-Za-z0-9_]{60,}'),
    re.compile(rb'AKIA[0-9A-Z]{16}'),
    re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    re.compile(rb'https://[^\s"<>]{2,}:[^\s"<>]{8,}@github\.com'),
]
# Read Git objects in one process; never print credential contents if detected.
oids = sorted({oid for oid, _ in files})
batch = subprocess.run(['git', 'cat-file', '--batch'], input=('\n'.join(oids)+'\n').encode(), cwd=ROOT, capture_output=True, check=True).stdout
offset = 0
names = {}
for oid, name in files: names.setdefault(oid, name)
for _ in oids:
    end = batch.index(b'\n', offset)
    oid, kind, size = batch[offset:end].split()
    offset = end + 1
    content = batch[offset:offset+int(size)]
    offset += int(size)+1
    if kind == b'blob' and any(pattern.search(content) for pattern in patterns):
        errors.append((names[oid.decode()], 'possible credential; inspect locally without logging its value'))
if errors:
    for name, reason in sorted(set(errors)): print(f'FAIL: {name}: {reason}')
    raise SystemExit(1)
print(f'PASS: {len(oids)} reachable objects checked; no tracked SDK/output/private photo payloads or recognized credential patterns.')
