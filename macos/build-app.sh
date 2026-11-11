#!/usr/bin/env bash
# Builds a universal (Apple Silicon + Intel) MX Master Portable.app and a zip.
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

APP="build/MX Master Portable.app"
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/MXMasterPortable" "$APP/Contents/MacOS/MXMasterPortable"
cp Resources/Info.plist "$APP/Contents/Info.plist"

# Ad-hoc signature: required on Apple Silicon. Not notarized.
codesign --force --sign - "$APP"
codesign --verify --verbose "$APP"
lipo -info "$APP/Contents/MacOS/MXMasterPortable"

ditto -c -k --keepParent "$APP" build/MXMasterPortable-macOS.zip
echo "Bitti: macos/build/MXMasterPortable-macOS.zip"
