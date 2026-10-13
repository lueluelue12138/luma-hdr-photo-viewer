"""Install/upgrade/uninstall the separately compiled isolated fixture only."""
from pathlib import Path
import hashlib
import json
import subprocess
import uuid
import winreg
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
EXE = ROOT/'artifacts/installer-verification/Luma-installer-test.exe'
OUT = ROOT/'artifacts/installer-verification'/uuid.uuid4().hex
APP = OUT/'installed-app'
DATA = OUT/'library-preserved'
REG = r'Software\Luma.InstallerTest\Capabilities'
PROG = r'Software\Classes\Luma.InstallerTest.Photo'
UNINSTALL = r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{BB91BFB9-16F6-4FFB-8655-21B0156CBE91}_is1'
manifest = ET.parse(ROOT/'packaging/AppxManifest.xml')
formats = {node.text for node in manifest.iter() if node.tag.endswith('FileType')}
def optional_value(key, name):
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key) as h:return winreg.QueryValueEx(h,name)
    except FileNotFoundError:return None
def production_registration():
    result={}
    for extension in formats:
        result[(extension,'default')]=optional_value('Software\\Classes\\'+extension,'')
        for name in ['ProgId','Hash']:
            result[(extension,name)]=optional_value('Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\'+extension+r'\UserChoice',name)
    result['registered']=optional_value(r'Software\RegisteredApplications','Luma')
    result['command']=optional_value(r'Software\Classes\Luma.Photo\shell\open\command','')
    return result
registration_before=production_registration()
production_db=Path.home()/'AppData/Local/Luma/library.db'
database_before=hashlib.sha256(production_db.read_bytes()).hexdigest() if production_db.exists() else None
for key in [REG, PROG, UNINSTALL]:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key): pass
    except FileNotFoundError: continue
    raise RuntimeError('Previous isolated installer registration exists; inspect before replacing it.')
assert EXE.is_file()
assert APP.resolve().is_relative_to((ROOT/'artifacts/installer-verification').resolve())
DATA.mkdir(parents=True)
sentinel = DATA/'library.db'
sentinel.write_bytes(b'isolated-user-library-must-survive')
before = sentinel.read_bytes()
def run(exe, *args):
    p = subprocess.run([str(exe), *args], timeout=180, creationflags=subprocess.CREATE_NO_WINDOW)
    assert p.returncode == 0, (exe.name, p.returncode)
def read(key, name):
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key) as h: return winreg.QueryValueEx(h, name)[0]
args = ['/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', f'/DIR={APP}', f'/LOG={OUT}/install.log', '/TASKS=']
run(EXE, *args)
assert (APP/'Luma.App.exe').exists() and (APP/'unins000.exe').exists()
metadata = json.loads((APP/'build-info.json').read_text(encoding='utf-8-sig'))
payload = ROOT/f'artifacts/release-payload-{metadata["variant"]}'
for file in payload.rglob('*'):
    if not file.is_file() or file.suffix.lower() == '.pdb' or file.name == 'AppxManifest.xml': continue
    assert (APP/file.relative_to(payload)).read_bytes() == file.read_bytes(), file.name
assert read(r'Software\RegisteredApplications', 'Luma.InstallerTest') == REG
command = read(PROG+r'\shell\open\command', '')
assert command == f'"{APP}\\Luma.App.exe" "%1"'
for extension in formats: assert read(REG+r'\FileAssociations', extension) == 'Luma.InstallerTest.Photo'
run(EXE, *args)  # Repeat/upgrade must keep the same installation and registrations.
assert read(PROG+r'\shell\open\command', '') == command
run(APP/'unins000.exe', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', f'/LOG={OUT}/uninstall.log')
assert not (APP/'Luma.App.exe').exists()
for key in [REG, PROG, UNINSTALL]:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key): pass
    except FileNotFoundError: continue
    raise AssertionError(f'Fixture registration remained after uninstall: {key}')
assert sentinel.read_bytes() == before
assert production_registration()==registration_before
assert (hashlib.sha256(production_db.read_bytes()).hexdigest() if production_db.exists() else None)==database_before
# Another copy's registration, simulated entirely within the fixture namespace,
# must survive uninstall rather than being treated as this installer's property.
run(EXE, *args)
replacement=f'"{DATA}\\portable-copy.exe" "%1"'
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,PROG+r'\shell\open\command',0,winreg.KEY_SET_VALUE) as h:winreg.SetValueEx(h,'',0,winreg.REG_SZ,replacement)
run(APP/'unins000.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',f'/LOG={OUT}/other-owner-uninstall.log')
assert read(PROG+r'\shell\open\command','')==replacement
assert read(r'Software\RegisteredApplications','Luma.InstallerTest')==REG
def remove_fixture_key(key):
    assert key==REG or key.startswith(REG+'\\') or key==PROG or key.startswith(PROG+'\\')
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key,0,winreg.KEY_READ) as h:
        children=[]
        while True:
            try:children.append(winreg.EnumKey(h,len(children)))
            except OSError:break
    for child in children:remove_fixture_key(key+'\\'+child)
    winreg.DeleteKey(winreg.HKEY_CURRENT_USER,key)
for key in [REG,PROG]:remove_fixture_key(key)
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Software\RegisteredApplications',0,winreg.KEY_SET_VALUE) as h:winreg.DeleteValue(h,'Luma.InstallerTest')
for extension in formats:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,'Software\\Classes\\'+extension+r'\OpenWithProgids',0,winreg.KEY_SET_VALUE) as h:winreg.DeleteValue(h,'Luma.InstallerTest.Photo')
assert production_registration()==registration_before
report = {'installed_payload_byte_matches': True, 'repeat_install_passed': True, 'format_associations': len(formats), 'uninstall_cleaned_fixture_registration': True, 'other_copy_registration_preserved':True, 'isolated_library_preserved': True, 'production_library_and_default_associations_untouched': True, 'variant': metadata['variant']}
(OUT/'verification.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
