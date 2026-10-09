#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/resolve-plugin}"
mkdir -p "$OUT"
SDK="$(xcrun --show-sdk-path)"
FLAGS=(-std=c++17 -Wall -Wextra -Werror -O2 -isysroot "$SDK" -isystem "$SDK/usr/include/c++/v1" -I"$ROOT/plugins/resolve/include")
BUNDLE="$OUT/RevelareNegative.ofx.bundle"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
clang++ "${FLAGS[@]}" -fvisibility=hidden -bundle "$ROOT/plugins/resolve/RevelarePlugin.cpp" -framework OpenGL -o "$BUNDLE/Contents/MacOS/RevelareNegative.ofx"
clang++ "${FLAGS[@]}" "$ROOT/plugins/resolve/tests/NativeSmoke.cpp" -o "$OUT/native-smoke"
clang++ "${FLAGS[@]}" "$ROOT/plugins/resolve/tests/HostEditRegression.cpp" -framework OpenGL -o "$OUT/host-edit-test"
"$OUT/host-edit-test"
"$OUT/native-smoke"
RID=osx-arm64
if [[ "$(uname -m)" == x86_64 ]]; then RID=osx-x64; fi
dotnet publish "$ROOT/src/OpenRevelare.Calibration.Native" -c Release -r "$RID" -o "$OUT/native"
cp "$OUT/native/OpenRevelare.Calibration.Native.dylib" "$BUNDLE/Contents/MacOS/"
cp "$ROOT/LICENSE" "$BUNDLE/Contents/Resources/LICENSE-GPL-3.0"
cp "$ROOT/plugins/resolve/include/LICENSE" "$BUNDLE/Contents/Resources/LICENSE-OpenFX"
cp "$ROOT/plugins/resolve/README.md" "$BUNDLE/Contents/Resources/README.md"
cat > "$BUNDLE/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>org.openrevelare.negative.prototype</string>
<key>CFBundleName</key><string>Revelare Negative</string>
<key>CFBundleExecutable</key><string>RevelareNegative.ofx</string>
<key>CFBundlePackageType</key><string>BNDL</string>
<key>CFBundleVersion</key><string>0.1.6</string>
</dict></plist>
PLIST
codesign --force --sign - "$BUNDLE/Contents/MacOS/OpenRevelare.Calibration.Native.dylib"
codesign --force --sign - "$BUNDLE"
codesign --verify --deep --strict "$BUNDLE"
"$OUT/native-smoke" "$BUNDLE/Contents/MacOS/OpenRevelare.Calibration.Native.dylib" "$BUNDLE/Contents/MacOS/RevelareNegative.ofx"
ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$OUT/RevelareNegative-$RID.zip"

# Double-click installer. Installer owns upgrades at the stable OFX bundle path,
# so no destructive preinstall script is needed.
PKGROOT="$OUT/pkgroot"
mkdir -p "$PKGROOT/Library/OFX/Plugins"
ditto "$BUNDLE" "$PKGROOT/Library/OFX/Plugins/RevelareNegative.ofx.bundle"
pkgbuild \
  --root "$PKGROOT" \
  --identifier org.openrevelare.negative.pkg \
  --version 0.1.6 \
  --install-location / \
  "$OUT/RevelareNegative-$RID.pkg"
