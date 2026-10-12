import pathlib, zipfile, hashlib, xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
payload=root/'artifacts/Luma'
source_manifest=ET.parse(root/'packaging/AppxManifest.xml')
version=next(n for n in source_manifest.iter() if n.tag.endswith('Identity')).attrib['Version']
package=root/f'artifacts/Luma_{version}_x64.msix'
portable=root/f'artifacts/Luma_{version.rsplit(".",1)[0]}_x64.zip'
files=sorted(p for p in payload.rglob('*') if p.is_file())
assert files and (payload/'Luma.App.exe').is_file()
for p in files:
    assert p.suffix.lower() not in {'.jpg','.jpeg','.jxr','.wdp','.hdp','.db','.sqlite','.log','.zip'},p
with zipfile.ZipFile(package) as z:
    names={n.replace('\\','/'):n for n in z.namelist()}
    assert 'AppxSignature.p7x' not in names,'Development MSIX unexpectedly signed'
    for p in files:
        rel=p.relative_to(payload).as_posix()
        assert rel in names,rel
        if rel!='AppxManifest.xml':assert z.read(names[rel])==p.read_bytes(),rel
    manifest=ET.fromstring(z.read(names['AppxManifest.xml']))
    identity=next(n for n in manifest.iter() if n.tag.endswith('Identity'))
    assert identity.attrib['Version']==version,identity.attrib
    formats={n.text for n in manifest.iter() if n.tag.endswith('FileType')}
    assert formats=={'.jpg','.jpeg','.png','.bmp','.gif','.tif','.tiff','.jxr','.wdp','.hdp','.ico'},formats
sdk=root/'external/nvidia/sdk'
for name in ['nvngx_truehdr.dll','nvngx_vsr.dll']:
    assert (payload/name).read_bytes()==(sdk/'bin/Windows/x64/rel'/name).read_bytes()
assert (payload/'NVIDIA_RTX_Video_SDK_License.pdf').read_bytes()==(sdk/'NVIDIA_RTX_Video_SDK_License.pdf').read_bytes()
assert (payload/'VERIFICATION.md').read_bytes()==(root/'docs/VERIFICATION.md').read_bytes()
with zipfile.ZipFile(portable,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in files:z.write(p,'Luma/'+p.relative_to(payload).as_posix())
with zipfile.ZipFile(portable) as z:
    assert z.testzip() is None
    assert len(z.namelist())==len(files)
    for p in files:assert z.read('Luma/'+p.relative_to(payload).as_posix())==p.read_bytes(),p
sums=[]
for p in [portable,package]:
    digest=hashlib.sha256(p.read_bytes()).hexdigest().upper()
    sums.append(f'{digest}  {p.name}')
    print(f'PASS: {p.name}, {p.stat().st_size:,} bytes, SHA256 {digest}')
(root/'artifacts/SHA256SUMS.txt').write_text('\n'.join(sums)+'\n',encoding='utf-8')
print(f'PASS: {len(files)} payload files match both packages; 11 format associations; original NVIDIA DLLs/license; no user photos or databases.')
