#!/usr/bin/env bash
# Throwaway wrapper for the owner's manual Finder check on a Mac (milestone 3.1). It is only an aid: no icon, no
# version, no .dmg. Milestone 3.3 replaces it by the real bundle step of tools/package.cs.
#
#   packaging/macos/dev-app.sh <publish-dir> <out-dir>
#
# Builds "<out-dir>/Raffica PDF.app" from a publish folder that holds RafficaPdf (for example the artifact
# raffica-pdf-osx-x64 of the CI job build-macos), signs it ad hoc and leaves it there. Runs on macOS (bash 3.2, BSD tools).
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <publish-dir> <out-dir>" >&2
  exit 2
fi

publish_dir="$1"
out_dir="$2"
script_dir="$(cd "$(dirname "$0")" && pwd)"
versions_file="$script_dir/../native/versions.txt"

if [ ! -f "$publish_dir/RafficaPdf" ]; then
  echo "error: '$publish_dir' has no RafficaPdf; pass the publish folder." >&2
  exit 1
fi
if [ ! -f "$versions_file" ]; then
  echo "error: '$versions_file' not found." >&2
  exit 1
fi

# The last line may have no trailing newline: read it with a default and strip carriage returns.
min_version=""
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    MACOS_DEPLOYMENT_TARGET=*) min_version="${line#MACOS_DEPLOYMENT_TARGET=}" ;;
  esac
done < "$versions_file"
min_version="${min_version//$'\r'/}"
if [ -z "$min_version" ]; then
  echo "error: MACOS_DEPLOYMENT_TARGET is missing in '$versions_file'." >&2
  exit 1
fi

mkdir -p "$out_dir"
app="$out_dir/Raffica PDF.app"
if [ -e "$app" ]; then
  rm -rf "$app"
fi

mkdir -p "$app/Contents/MacOS"
# ditto keeps the executable bit, symlinks and the whole folder (the dylibs and tessdata next to the executable).
ditto "$publish_dir" "$app/Contents/MacOS"
chmod +x "$app/Contents/MacOS/RafficaPdf"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>io.github.benjo-code.raffica.pdf</string>
  <key>CFBundleExecutable</key>
  <string>RafficaPdf</string>
  <key>CFBundleName</key>
  <string>Raffica PDF</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>${min_version}</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>
      <string>PDF Document</string>
      <key>CFBundleTypeRole</key>
      <string>Editor</string>
      <key>LSHandlerRank</key>
      <string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>com.adobe.pdf</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
PLIST

if command -v plutil >/dev/null 2>&1; then
  plutil -lint "$app/Contents/Info.plist"
fi

# Ad hoc signing, after the bundle is complete (required on Apple Silicon).
codesign --force --deep -s - "$app"

echo "Built: $app"
