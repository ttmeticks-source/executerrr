#!/usr/bin/env bash
set -euo pipefail

SRC="/Applications/Roblox.app"
WORK="$HOME/Library/Application Support/bozo/Roblox.app"
DYLIB="$HOME/bozo/native/bozo.dylib"
ENT="$HOME/bozo/native/entitlements.plist"

cat > "$ENT" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
 "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-executable-page-protection</key><true/>
</dict></plist>
PLIST

[[ -f "$DYLIB" ]] || { echo "build the dylib first"; exit 1; }

pkill -f RobloxPlayer 2>/dev/null || true
rm -rf "$WORK"
mkdir -p "$(dirname "$WORK")"
cp -R "$SRC" "$WORK"

codesign --force --deep --sign - --entitlements "$ENT" "$WORK"
codesign --force --sign - "$DYLIB"

BIN="$WORK/Contents/MacOS/RobloxPlayer"
DYLD_INSERT_LIBRARIES="$DYLIB" "$BIN" &
echo "launched — now run: ./bozo  and press Ctrl+R"
