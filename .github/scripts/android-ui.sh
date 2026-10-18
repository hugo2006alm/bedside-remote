#!/usr/bin/env bash
set -uo pipefail

# Calibrate the isolated emulator before running UI interaction tests.
adb shell wm size || exit 1
bash android/gradlew -p android --no-daemon connectedDebugAndroidTest
test_status=$?

output="$GITHUB_WORKSPACE/artifacts/android-ui"
mkdir -p "$output"
# The connected-test runner removes its target APK after instrumentation.
# Install the built app again before capturing the standalone application.
adb install -r android/app/build/outputs/apk/debug/app-debug.apk || exit 1
adb shell am force-stop com.bedside.remote
adb shell am start -W -n com.bedside.remote/.MainActivity || exit 1
# UI Automator waits for an idle window; inspect the actual initial screen.
adb shell uiautomator dump /sdcard/bedside-view.xml || exit 1
adb pull /sdcard/bedside-view.xml "$output/view.xml" || exit 1
adb shell screencap -p /sdcard/bedside-screen.png || exit 1
adb pull /sdcard/bedside-screen.png "$output/remote.png" || exit 1
adb logcat -d -s AndroidRuntime:E > "$output/crashes.txt"
exit "$test_status"
