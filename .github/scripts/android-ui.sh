#!/usr/bin/env bash
set -uo pipefail

# Calibrate the isolated emulator before running UI interaction tests.
adb shell wm size || exit 1
bash android/gradlew -p android --no-daemon connectedDebugAndroidTest
test_status=$?

output="$GITHUB_WORKSPACE/artifacts/android-ui"
mkdir -p "$output"
# Exercise the exact packaged APK, including release signing and shrinking when
# configured, rather than substituting the debug instrumentation target.
mapfile -t apks < <(find "$GITHUB_WORKSPACE/artifacts/android-package" -maxdepth 1 -name '*.apk')
if [ "${#apks[@]}" -ne 1 ]; then
  echo 'Expected exactly one distribution APK.' >&2
  exit 1
fi
if adb shell pm path com.bedside.remote | grep -q '^package:'; then
  # This is an isolated CI emulator; instrumentation state is disposable.
  adb uninstall com.bedside.remote || exit 1
fi
adb install "${apks[0]}" || exit 1
sha256sum "${apks[0]}" > "$output/apk-sha256.txt"
adb shell dumpsys package com.bedside.remote > "$output/package.txt"
adb logcat -c || exit 1
adb shell am force-stop com.bedside.remote
adb shell am start -W -n com.bedside.remote/.MainActivity || exit 1
# UI Automator waits for an idle window; inspect the actual initial screen.
adb shell uiautomator dump /sdcard/bedside-view.xml || exit 1
adb pull /sdcard/bedside-view.xml "$output/view.xml" || exit 1
adb shell screencap -p /sdcard/bedside-screen.png || exit 1
adb pull /sdcard/bedside-screen.png "$output/remote.png" || exit 1
adb shell pidof com.bedside.remote || exit 1
adb logcat -d -s AndroidRuntime:E > "$output/crashes.txt"
if grep -q 'FATAL EXCEPTION' "$output/crashes.txt"; then exit 1; fi
exit "$test_status"
