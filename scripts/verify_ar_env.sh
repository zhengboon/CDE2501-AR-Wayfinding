#!/usr/bin/env bash
set -euo pipefail

echo "== OS =="
cat /etc/os-release | grep -E 'PRETTY_NAME|VERSION_ID|VERSION_CODENAME' || true

echo

echo "== Core tools =="
for c in adb java; do
  if command -v "$c" >/dev/null 2>&1; then
    echo "OK: $c -> $(command -v "$c")"
  else
    echo "MISSING: $c"
  fi
done

echo

echo "== Java version =="
java -version 2>&1 | head -n 2 || true

echo

echo "== ADB version =="
adb version 2>/dev/null | head -n 2 || true

echo

echo "== Windows Unity Editor detection from WSL =="
if [[ -d "/mnt/c/Program Files/Unity/Hub/Editor" ]]; then
  ls "/mnt/c/Program Files/Unity/Hub/Editor" | sed 's/^/FOUND UNITY VERSION: /'
else
  echo "NOT FOUND: /mnt/c/Program Files/Unity/Hub/Editor"
fi

cat <<'MSG'

Next checks (manual):
1) Install Unity 2022 LTS + Android Build Support in Windows Unity Hub.
2) In Unity Package Manager install: AR Foundation, XR Plugin Management, ARCore XR Plugin, ARKit XR Plugin.
3) Connect ARCore-capable Android device and run: adb devices
MSG
