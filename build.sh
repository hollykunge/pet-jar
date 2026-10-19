#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/dist/宠物罐.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# Swift requires the file containing top-level statements to be called main.swift.
BUILD="$ROOT/.build/native"
mkdir -p "$BUILD"
cp "$ROOT/Main.swift" "$BUILD/main.swift"
xcrun swiftc -swift-version 5 -target arm64-apple-macosx14.0 -O -framework AppKit -framework SwiftUI -framework Carbon -framework ApplicationServices "$ROOT/Delivery.swift" "$ROOT/AccountUsage.swift" "$ROOT/Jar.swift" "$ROOT/PackedScene.swift" "$BUILD/main.swift" -o "$APP/Contents/MacOS/TuanTuan"
cp "$ROOT/Resources/"pet-*.png "$APP/Contents/Resources/"
cp "$ROOT/Resources/"prop-*.png "$APP/Contents/Resources/"
cp "$ROOT/Resources/pile-scene.png" "$ROOT/Resources/pile-expressions.png" "$APP/Contents/Resources/"
cp "$ROOT/Resources/"claude-eye-*.png "$ROOT/Resources/claude-eyes.json" "$APP/Contents/Resources/"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>studio.tuantuan.desktop</string>
<key>CFBundleName</key><string>宠物罐</string>
<key>CFBundleDisplayName</key><string>宠物罐</string>
<key>CFBundleExecutable</key><string>TuanTuan</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.5.3</string>
<key>CFBundleVersion</key><string>12</string>
<key>LSUIElement</key><true/>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$APP"
"$APP/Contents/MacOS/TuanTuan" --self-test
printf 'Built: %s\n' "$APP"
