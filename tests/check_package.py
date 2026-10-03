import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

root = Path(__file__).resolve().parent.parent
manifest = json.loads((root / 'src/router.json').read_text())
project = ET.parse(root / 'src/router.csproj')
assert manifest['version'] == project.findtext('.//Version')
assert manifest['author'] == 'clareLab' and manifest['dependencies'] == []
assert not manifest['affects_gameplay']
if '--source' not in sys.argv:
    directory = root / 'artifacts/dist'
    dll = (directory / 'router/router.dll').read_bytes()
    assert b'SelfTests' not in dll
    assert b'ROUTER_SELFTEST' not in dll
    with ZipFile(directory / f"router-{manifest['version']}.zip") as package:
        assert set(package.namelist()) == {'router/router.dll', 'router/router.json', 'router/LICENSE'}
print('PASS package checks')
