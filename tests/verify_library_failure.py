"""Real SQLite index corruption in fresh, disposable profiles; never edit a user's library."""
import argparse,json,pathlib,sqlite3,struct,subprocess,tempfile
from PIL import Image

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--app',type=pathlib.Path,required=True)
parser.add_argument('--control',type=pathlib.Path)
parser.add_argument('--photo',type=pathlib.Path,help='Optional real photo, read only')
parser.add_argument('--output',type=pathlib.Path,default=root/'artifacts/library-failure')
args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)

def fixture(label):
    folder=pathlib.Path(tempfile.mkdtemp(prefix=label+'-',dir=args.output)).resolve()
    first=folder/'01.jpg';second=folder/'02.png';missing=folder/'03-missing.jpg'
    Image.new('RGB',(1200,800),(80,130,190)).save(first)
    Image.new('RGB',(800,1200),(190,130,80)).save(second)
    first=args.photo.resolve(strict=True) if args.photo else first
    database=folder/'library.db'
    with sqlite3.connect(database) as db:
        db.executescript('''
            CREATE TABLE albums(id INTEGER PRIMARY KEY,name TEXT NOT NULL,folder TEXT NOT NULL COLLATE NOCASE UNIQUE,cover TEXT,offline INTEGER NOT NULL DEFAULT 0,sort TEXT NOT NULL DEFAULT 'name',pinned INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE photos(path TEXT PRIMARY KEY COLLATE NOCASE,name TEXT NOT NULL,size INTEGER NOT NULL,modified INTEGER NOT NULL,favorite INTEGER NOT NULL DEFAULT 0,opened INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE album_photos(album_id INTEGER NOT NULL,path TEXT NOT NULL COLLATE NOCASE,missing INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(album_id,path));
            CREATE INDEX ix_photos_opened ON photos(opened);
            CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            PRAGMA user_version=2;
        ''')
        # Keep startup's normal folder refresh from deleting the intentionally
        # missing-photo link or discovering generated verification files.
        db.execute('INSERT INTO albums(id,name,folder) VALUES(1,?,?)',('Fixture',str(folder/'offline-album')))
        for n,path in enumerate([first,second,missing]):
            db.execute('INSERT INTO photos(path,name,size,modified) VALUES(?,?,?,0)',(str(path),f'{n+1:02}.jpg',path.stat().st_size if path.exists() else 1))
            db.execute('INSERT INTO album_photos VALUES(1,?,0)',(str(path),))
        page=db.execute("SELECT rootpage FROM sqlite_schema WHERE name='ix_photos_opened'").fetchone()[0]
        size=db.execute('PRAGMA page_size').fetchone()[0]
    # Only this just-created fixture's index leaf is emptied. Table pages stay intact.
    content=bytearray(database.read_bytes());offset=(page-1)*size
    assert page>1 and content[offset]==10 and struct.unpack('>H',content[offset+3:offset+5])[0]==3
    content[offset+1:offset+8]=struct.pack('>HHHB',0,0,size,0)
    database.write_bytes(content)
    with sqlite3.connect(database) as db:
        errors=[r[0] for r in db.execute('PRAGMA integrity_check')]
        assert any('ix_photos_opened' in e for e in errors) and db.execute('SELECT COUNT(*) FROM photos NOT INDEXED').fetchone()[0]==3
    return folder,first

if args.control:
    folder,photo=fixture('control')
    result=subprocess.run([str(args.control.resolve(strict=True)),'--test-library',str(folder),'--verify-startup',str(photo)],timeout=70)
    report=json.loads((folder/'startup.json').read_text())
    assert not report['complete'] and 'SQLite Error 11' in report['failure'],report
    print(json.dumps({'controlReproduced':True,'photoPresentedBeforeFailure':bool(report['results']),'report':str(folder/'startup.json')}),flush=True)
folder,photo=fixture('candidate')
result=subprocess.run([str(args.app.resolve(strict=True)),'--test-library',str(folder),'--verify-library-failure',str(photo)],timeout=100)
report=json.loads((folder/'library-failure.json').read_text())
assert result.returncode==0 and report['complete'],report
print(json.dumps({'complete':True,'scenarios':len(report['results']),'report':str(folder/'library-failure.json')}),flush=True)
