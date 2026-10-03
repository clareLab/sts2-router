#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/common.sh
router_game_paths "${1:-}"
router_dotnet build src/router.csproj -c Release "-p:Sts2DataDir=$data_dir"
python3 - <<'PY'
import hashlib, json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
package = Path('artifacts/dist') / f"router-{json.loads(Path('src/router.json').read_text())['version']}.zip"
with ZipFile(package, 'w', ZIP_DEFLATED) as archive:
    for file in [Path('artifacts/dist/router/router.dll'), Path('artifacts/dist/router/router.json'), Path('artifacts/dist/router/LICENSE')]:
        archive.write(file, 'router/' + file.name)
files = [Path('artifacts/dist/router/router.dll'), package]
Path('artifacts/dist/SHA256SUMS').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to("artifacts/dist")}\n' for p in files))
print(f'Package: {package}')
PY
