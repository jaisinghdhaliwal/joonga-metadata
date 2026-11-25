#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

icon_mode=${1:-}
case "$#:$icon_mode" in
    0:|1:--compat-icon|2:--icon-catalog) ;;
    *) echo "Usage: $0 [--compat-icon | --icon-catalog /path/to/Assets.car]" >&2; exit 2 ;;
esac

app="$PWD/build/Joonga Metadata.app"
executable="$app/Contents/MacOS/Joonga Metadata"
resources="$app/Contents/Resources"
deployment_target="$(/usr/bin/plutil -extract LSMinimumSystemVersion raw Info.plist)"
mkdir -p build/cache "$app/Contents/MacOS" "$resources"

for architecture in arm64 x86_64; do
    swiftc -target "${architecture}-apple-macos${deployment_target}" -module-cache-path "build/cache/$architecture" \
        Sources/*.swift -o "build/Joonga Metadata.$architecture"
done
xcrun lipo -create "build/Joonga Metadata.arm64" "build/Joonga Metadata.x86_64" -output "$executable"

cp Info.plist "$app/Contents/Info.plist"
rm -f "$resources/Presets.json" "$resources/Assets.car" "$resources/AppIcon.icns"

if [[ "$icon_mode" == --icon-catalog ]]; then
    /usr/bin/assetutil --validate-file "$2"
    cp "$2" "$resources/Assets.car"
    /usr/bin/plutil -insert CFBundleIconName -string AppIcon "$app/Contents/Info.plist"
elif [[ "$icon_mode" != --compat-icon ]]; then
    # Use full Xcode for Icon Composer even if Command Line Tools are selected for Swift.
    if [[ -z "${DEVELOPER_DIR:-}" ]] && ! xcrun --find actool >/dev/null 2>&1 &&
        [[ -d /Applications/Xcode.app/Contents/Developer ]]; then
        export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
    fi
    if ! xcrun --find actool >/dev/null 2>&1; then
        echo "Set DEVELOPER_DIR to Xcode 26 or newer, or pass --compat-icon for the static icon." >&2
        exit 1
    fi
    icon_source=""
    while IFS= read -r candidate; do
        if [[ -f "$candidate/icon.json" ]]; then
            icon_source="$candidate"
            break
        fi
    done < <(ls -1dt "$PWD"/AppIcon*.icon 2>/dev/null || true)
    if [[ -z "$icon_source" ]]; then
        echo "No valid AppIcon *.icon directory with icon.json found." >&2
        exit 1
    fi
    icon_app_name="$(basename "$icon_source" .icon)"
    catalog="$PWD/build/icon-catalog"
    mkdir -p "$catalog"
    rm -f "$catalog/Assets.car"
    partial="$catalog/Icon-Info.plist"
    xcrun actool --compile "$catalog" --output-partial-info-plist "$partial" \
        --platform macosx --minimum-deployment-target "$deployment_target" --target-device mac \
        --app-icon "$icon_app_name" --include-all-app-icons "$icon_source" </dev/null
    test -s "$catalog/Assets.car"
    cp "$catalog/Assets.car" "$resources/Assets.car"
    icon_name="$(/usr/bin/plutil -extract CFBundleIconName raw "$partial" 2>/dev/null || printf "%s" "$icon_app_name")"
    /usr/bin/plutil -insert CFBundleIconName -string "$icon_name" "$app/Contents/Info.plist"
fi

# Keep the full-resolution compatibility icon alongside the layered catalog.
cp AppIcon.icns "$resources/"

# Bundle the pinned ExifTool distribution so the app works without a separate install.
exiftool_version="13.64"
exiftool_archive_name="Image-ExifTool-${exiftool_version}.tar.gz"
exiftool_url="https://sourceforge.net/projects/exiftool/files/${exiftool_archive_name}/download"
exiftool_sha256="dcb9f6be1f6705ee826a92d1522e3fec1d0b3749cdc061b03c46873d5fe204fc"

if [[ -n "${EXIFTOOL_DIR:-}" ]]; then
    exiftool_dir="$EXIFTOOL_DIR"
else
    vendor="$PWD/build/vendor"
    mkdir -p "$vendor"
    archive="$vendor/$exiftool_archive_name"
    if [[ ! -f "$archive" ]]; then
        curl -fsSL "$exiftool_url" -o "$archive"
    fi
    actual_sha256="$(/usr/bin/shasum -a 256 "$archive" | /usr/bin/awk '{print $1}')"
    if [[ "$actual_sha256" != "$exiftool_sha256" ]]; then
        echo "ExifTool download checksum mismatch. Remove $archive and retry." >&2
        exit 1
    fi
    rm -rf "$vendor/Image-ExifTool-${exiftool_version}"
    /usr/bin/tar -xzf "$archive" -C "$vendor"
    exiftool_dir="$vendor/Image-ExifTool-${exiftool_version}"
fi

if [[ ! -f "$exiftool_dir/exiftool" || ! -d "$exiftool_dir/lib" ]]; then
    echo "ExifTool distribution at $exiftool_dir must contain exiftool and lib/." >&2
    exit 1
fi

rm -rf "$resources/exiftool"
mkdir -p "$resources/exiftool"
cp "$exiftool_dir/exiftool" "$resources/exiftool/exiftool"
chmod +x "$resources/exiftool/exiftool"
cp -R "$exiftool_dir/lib" "$resources/exiftool/lib"
if [[ -f "$exiftool_dir/README" ]]; then
    cp "$exiftool_dir/README" "$resources/exiftool/README"
fi

codesign --force --sign - "$app"
