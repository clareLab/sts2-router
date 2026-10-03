#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/common.sh
router_dotnet run --project tests/unit/RouterTests.csproj -c Release
case "${1:-}" in
  --source) python3 tests/check_package.py --source ;;
  "") ./scripts/build.sh; python3 tests/check_package.py ;;
  *) echo 'Usage: ./scripts/test.sh [--source]' >&2; exit 2 ;;
esac
