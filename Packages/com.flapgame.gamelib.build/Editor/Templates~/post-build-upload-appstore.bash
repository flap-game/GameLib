#!/bin/bash
# Unity Cloud Build post-build script (iOS targets): uploads the built IPA to App Store Connect.
# Installed by GameLib > Cloud Build > Install App Store Upload Script. Set in the build target's
# Advanced settings > Post-build script, with these environment variables on the target:
#   ITUNES_USERNAME  Apple ID that can upload to the app
#   ITUNES_PASSWORD  an app-specific password of that Apple ID (appleid.apple.com > Sign-In and Security)
set -u

if [ -z "${ITUNES_USERNAME:-}" ] || [ -z "${ITUNES_PASSWORD:-}" ]; then
    echo "[upload] ITUNES_USERNAME / ITUNES_PASSWORD are not set on this build target." >&2
    exit 1
fi

# Cloud Build passes the player path as $UNITY_PLAYER_PATH (also $2); fall back to the build output folder.
ipa="${UNITY_PLAYER_PATH:-${2:-}}"
if [ -d "$ipa" ]; then
    ipa="$(find "$ipa" -maxdepth 2 -name '*.ipa' -print -quit)"
fi
if [ -z "$ipa" ] || [ ! -f "$ipa" ]; then
    ipa="$(find "${WORKSPACE:-.}/.build/last/${TARGET_NAME:-}" -maxdepth 3 -name '*.ipa' -print -quit 2>/dev/null)"
fi
if [ -z "$ipa" ] || [ ! -f "$ipa" ]; then
    echo "[upload] No .ipa found (UNITY_PLAYER_PATH='${UNITY_PLAYER_PATH:-}', WORKSPACE='${WORKSPACE:-}', TARGET_NAME='${TARGET_NAME:-}')." >&2
    exit 1
fi

echo "[upload] Uploading $ipa"
if xcrun altool --upload-app --type ios -f "$ipa" -u "$ITUNES_USERNAME" -p "$ITUNES_PASSWORD"; then
    echo "[upload] Done. The build shows in App Store Connect / TestFlight after Apple finishes processing it."
    exit 0
fi
echo "[upload] altool failed - see the output above." >&2
exit 1
