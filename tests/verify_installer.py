"""Install/upgrade/uninstall the separately compiled isolated fixture only."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import sqlite3
import subprocess
import time
import uuid
import winreg
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--previous-installer',type=Path,help='Optional isolated Luma-installer-test.exe carrying an older version')
options=parser.parse_args()
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

cleanup_waits = []

def uninstall_fixture(log_name):
    # Inno can choose unins001.exe when an earlier uninstaller is still being
    # cleaned up. Read the registered executable rather than assume its number.
    registered = read(UNINSTALL, 'UninstallString')
    match = re.fullmatch(r'"([^"]+)"', registered)
    assert match, 'Unexpected isolated uninstall command'
    uninstaller = Path(match.group(1)).resolve()
    assert uninstaller.parent == APP.resolve()
    assert re.fullmatch(r'unins\d+\.exe', uninstaller.name)
    assert uninstaller.is_file()
    run(uninstaller, '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', f'/LOG={OUT}/{log_name}')
    # The uninstall clone deletes its original EXE after the original process
    # exits. Wait for those actual files and the fixture uninstall key to go
    # away before installing again; do not race cleanup with the next install.
    start = time.monotonic()
    while ((APP/'Luma.App.exe').exists() or any(APP.glob('unins*')) or
           optional_value(UNINSTALL, 'UninstallString') is not None):
        assert time.monotonic()-start < 30, 'Isolated uninstall cleanup did not finish'
        time.sleep(.05)
    cleanup_waits.append(round((time.monotonic()-start)*1000, 1))

args = ['/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', f'/DIR={APP}', f'/LOG={OUT}/install.log', '/TASKS=']
previous_version=None
profile=OUT/'upgrade-profile';profile_snapshot=None
def profile_data():
    with sqlite3.connect(profile/'library.db') as db:
        return {table:db.execute('SELECT * FROM '+table+' ORDER BY 1').fetchall() for table in ['albums','photos','album_photos','settings']}
if options.previous_installer:
    previous=options.previous_installer.resolve()
    assert previous.is_relative_to((ROOT/'artifacts').resolve()) and previous.name=='Luma-installer-test.exe','Only a locally built isolated installer fixture is allowed'
    run(previous,*args)
    previous_version=json.loads((APP/'build-info.json').read_text(encoding='utf-8-sig'))['version']
    run(APP/'Luma.App.exe','--test-library',str(profile),'--verify-startup')
    assert json.loads((profile/'startup.json').read_text())['complete'],'Previous app must open its own profile'
    originals=DATA/'originals';originals.mkdir();photo=originals/'upgrade-photo.png'
    from PIL import Image
    Image.new('RGB',(64,48),(60,100,140)).save(photo)
    modified=photo.stat().st_mtime_ns//100+621355968000000000
    with sqlite3.connect(profile/'library.db') as db:
        db.execute('INSERT INTO albums(id,name,folder,cover,sort) VALUES(1,?,?,?,?)',('Upgrade album',str(originals),str(photo),'newest'))
        db.execute('INSERT INTO photos(path,name,size,modified,favorite,opened) VALUES(?,?,?,?,1,1700000000000)',(str(photo),photo.name,photo.stat().st_size,modified))
        db.execute('INSERT INTO album_photos(album_id,path) VALUES(1,?)',(str(photo),))
        db.executemany('INSERT OR REPLACE INTO settings(key,value) VALUES(?,?)',[('theme','dark'),('viewer.preferences',json.dumps(dict(Hdr=True,Vsr=False,Preset=2,AutoBrightness=True,AutoPeak=True,MiddleGray=100,Peak=1000)))])
    cache=profile/'thumbnail-cache-v1';cache.mkdir(exist_ok=True);(cache/'upgrade-marker').write_bytes(b'cached-data-must-survive')
    profile_snapshot=profile_data();photo_hash=hashlib.sha256(photo.read_bytes()).hexdigest()
    run(APP/'Luma.App.exe','--test-library',str(profile),'--verify-startup')
    assert json.loads((profile/'startup.json').read_text())['complete'],'Old app must read the populated profile before upgrade'
    assert profile_data()==profile_snapshot,'Old app did not preserve its seeded library and settings'
# On upgrade omit /DIR entirely: Setup must discover the previous custom
# location through its stable AppId, rather than receiving it from the test.
install_args=[arg for arg in args if not arg.startswith('/DIR=')] if previous_version is not None else args
run(EXE, *install_args)
assert (APP/'Luma.App.exe').exists() and (APP/'unins000.exe').exists()
metadata = json.loads((APP/'build-info.json').read_text(encoding='utf-8-sig'))
if previous_version is not None:
    assert tuple(map(int,previous_version.split('.'))) < tuple(map(int,metadata['version'].split('.'))),'Fixture must genuinely upgrade an older version'
    assert profile_data()==profile_snapshot,'Upgrade changed albums, favourites, history or preferences'
    run(APP/'Luma.App.exe','--test-library',str(profile),'--verify-startup')
    assert json.loads((profile/'startup.json').read_text())['complete'],'New app must read the retained profile'
    assert profile_data()==profile_snapshot,'Opening the new version changed retained user data'
    assert hashlib.sha256(photo.read_bytes()).hexdigest()==photo_hash
    assert (cache/'upgrade-marker').read_bytes()==b'cached-data-must-survive'
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
uninstall_fixture('uninstall.log')
assert not (APP/'Luma.App.exe').exists()
for key in [REG, PROG, UNINSTALL]:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key): pass
    except FileNotFoundError: continue
    raise AssertionError(f'Fixture registration remained after uninstall: {key}')
assert sentinel.read_bytes() == before
if profile_snapshot is not None:assert profile_data()==profile_snapshot,'Uninstall must preserve the upgraded profile'
assert production_registration()==registration_before
assert (hashlib.sha256(production_db.read_bytes()).hexdigest() if production_db.exists() else None)==database_before
# Another copy's registration, simulated entirely within the fixture namespace,
# must survive uninstall rather than being treated as this installer's property.
run(EXE, *args)
replacement=f'"{DATA}\\portable-copy.exe" "%1"'
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,PROG+r'\shell\open\command',0,winreg.KEY_SET_VALUE) as h:winreg.SetValueEx(h,'',0,winreg.REG_SZ,replacement)
uninstall_fixture('other-owner-uninstall.log')
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
report = {'installed_payload_byte_matches': True, 'repeat_install_passed': True, 'format_associations': len(formats), 'uninstall_cleaned_fixture_registration': True, 'other_copy_registration_preserved':True, 'uninstall_cleanup_wait_ms': cleanup_waits, 'isolated_library_preserved': True, 'production_library_and_default_associations_untouched': True, 'variant': metadata['variant']}
if previous_version is not None:report['cross_version_upgrade']={'from':previous_version,'to':metadata['version'],'previousCustomDirectoryDiscovered':True,'retainedProfileOpenedByBothVersions':True,'albumsFavouritesHistoryPreferencesPreserved':True,'originalAndCachePreserved':True}
(OUT/'verification.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
