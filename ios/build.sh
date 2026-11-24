#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
version="${APP_VERSION:-0.1.0-alpha.1}"
build_number="${GITHUB_RUN_NUMBER:-1}"
if [[ ! "$version" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "Expected an application SemVer." >&2
    exit 1
fi
marketing_version="${BASH_REMATCH[1]}"
[[ "$build_number" =~ ^[1-9][0-9]*$ ]]
package="bedside-remote-ios-v$version-arm64-sideload.ipa"

mkdir -p build ../artifacts
xcodegen generate
settings=(
    "MARKETING_VERSION=$marketing_version" "CURRENT_PROJECT_VERSION=$build_number" "BEDSIDE_VERSION=$version"
)
if [[ "${1:-}" != --package-only ]]; then
    device=$(xcrun simctl list devices available -j | python3 -c '
import json, sys
devices = json.load(sys.stdin)["devices"]
print(next(d["udid"] for runtime, rows in reversed(sorted(devices.items())) for d in rows if "iOS" in runtime and d["name"] == "iPhone 17"))
')
    xcodebuild test -project BedsideRemote.xcodeproj -scheme BedsideRemote -destination "id=$device" \
        -parallel-testing-enabled NO -derivedDataPath build/simulator -resultBundlePath build/tests.xcresult "${settings[@]}"
    xcrun simctl status_bar "$device" override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 --batteryState unplugged --batteryLevel 100
    xcrun simctl terminate "$device" com.bedside.remote || true
    for appearance in light dark; do
        xcrun simctl ui "$device" appearance "$appearance"
        xcrun simctl launch "$device" com.bedside.remote
        sleep 2
        xcrun simctl io "$device" screenshot "build/remote-$appearance.png"
        xcrun simctl terminate "$device" com.bedside.remote
    done
fi

xcodebuild build -project BedsideRemote.xcodeproj -scheme BedsideRemote -configuration Release \
    -sdk iphoneos -destination 'generic/platform=iOS' -derivedDataPath build/device \
    CODE_SIGNING_ALLOWED=NO ARCHS=arm64 ONLY_ACTIVE_ARCH=NO "${settings[@]}"

app="build/device/Build/Products/Release-iphoneos/BedsideRemote.app"
[[ "$(xcrun lipo -archs "$app/BedsideRemote")" == arm64 ]]
xcrun vtool -show-build "$app/BedsideRemote" | grep -Eq 'platform IOS$'
[[ ! -e "$app/embedded.mobileprovision" && ! -e "$app/_CodeSignature" && ! -e "$app/PlugIns" && -s "$app/LICENSE" ]]
/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$app/Info.plist" | grep -Fx com.bedside.remote
/usr/libexec/PlistBuddy -c 'Print BedsideVersion' "$app/Info.plist" | grep -Fx "$version"
if otool -l "$app/BedsideRemote" | grep -q 'LC_CODE_SIGNATURE'; then
    echo "Unexpected embedded code signature." >&2
    exit 1
fi
mkdir -p build/package/Payload
ditto "$app" build/package/Payload/BedsideRemote.app
ditto -c -k --keepParent build/package/Payload "../artifacts/$package"
unzip -t "../artifacts/$package"
(cd ../artifacts && shasum -a 256 "$package" > "$package.sha256")
