#!/usr/bin/env bash
# Build + sign the release APK in Docker, publish it to /data/turnly/releases, redeploy the web app.
#   ./turnly-build.sh            APK + web redeploy
#   ./turnly-build.sh --no-web   APK only
#   ./turnly-build.sh --web-only web redeploy only
# The keystore and its password live in ~/.turnly (kept outside the repo). It is never generated
# here: installed apps only update from APKs signed with the original CN=Turnly key.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE=/opt/homeserver/apps/turnly/compose.yml
RELEASES=/data/turnly/releases
KEYDIR="$HOME/.turnly"
KEYSTORE="$KEYDIR/turnly-release.jks"
PASSFILE="$KEYDIR/keystore.pass"
ALIAS="${TURNLY_KEY_ALIAS:-turnly}"
GRADLE="$REPO/web/android/app/build.gradle"
SERVICES="$REPO/web/android/app/google-services.json"
IMAGE=turnly-android-builder

do_apk=1; do_web=1
case "${1:-}" in
  --no-web) do_web=0 ;;
  --web-only) do_apk=0 ;;
  "") ;;
  *) echo "unknown option $1" >&2; exit 1 ;;
esac

if (( do_apk )); then
  [[ -f $KEYSTORE && -f $PASSFILE ]] || { echo "Missing $KEYSTORE or $PASSFILE" >&2; exit 1; }
  # Without it Gradle still builds, but the APK silently ships with push notifications disabled.
  [[ -f $SERVICES ]] || { echo "Missing $SERVICES" >&2; exit 1; }
  mkdir -p "$RELEASES" "$HOME/.cache/turnly-android"

  echo "Preparing builder image"
  docker build -q -t "$IMAGE" "$REPO/tools/android-builder" >/dev/null
  PASS="$(cat "$PASSFILE")"

  # Next version: one past the highest of build.gradle and what is already published.
  old_code=$(grep -oP 'versionCode\s+\K\d+' "$GRADLE")
  old_name=$(grep -oP 'versionName\s+"\K[^"]+' "$GRADLE")
  pub_code=$(ls "$RELEASES" 2>/dev/null | grep -oP '^turnly-v[0-9.]+-\K\d+(?=\.apk$)' | sort -n | tail -1 || true)
  pub_name=$(ls "$RELEASES" 2>/dev/null | grep -oP '^turnly-v\K[0-9.]+(?=-\d+\.apk$)' | sort -V | tail -1 || true)
  base_code=$(( old_code > ${pub_code:-0} ? old_code : ${pub_code:-0} ))
  base_name=$(printf '%s\n%s\n' "$old_name" "${pub_name:-0}" | sort -V | tail -1)
  new_code=$(( base_code + 1 ))
  new_name="${base_name%.*}.$(( ${base_name##*.} + 1 ))"

  # Bump before the build (Gradle reads the file); restore on failure so a failed build burns nothing.
  cp "$GRADLE" "$GRADLE.bak"
  trap 'mv -f "$GRADLE.bak" "$GRADLE"' ERR
  sed -i -E "s/versionCode\s+[0-9]+/versionCode $new_code/; s/versionName\s+\"[^\"]+\"/versionName \"$new_name\"/" "$GRADLE"
  echo "Building versionCode $new_code, versionName $new_name"

  docker run --rm --user "$(id -u):$(id -g)" \
    -e HOME=/home/builder -e GRADLE_USER_HOME=/cache/gradle -e npm_config_cache=/cache/npm \
    -e TURNLY_KEYSTORE=/keys/turnly-release.jks -e TURNLY_KEY_ALIAS="$ALIAS" \
    -e TURNLY_KEYSTORE_PASSWORD="$PASS" -e TURNLY_KEY_PASSWORD="$PASS" \
    -v "$REPO:/work" -v "$KEYDIR:/keys:ro" -v "$HOME/.cache/turnly-android:/cache" \
    "$IMAGE" bash -euc '
      cd web
      npm ci
      npm run build
      npx cap sync android
      cd android
      sh ./gradlew --no-daemon assembleRelease
    '

  trap - ERR
  rm -f "$GRADLE.bak"
  apk="$REPO/web/android/app/build/outputs/apk/release/app-release.apk"
  [[ -f $apk ]] || { echo "APK not produced (is the release signing config unsigned?)" >&2; exit 1; }
  cp "$apk" "$RELEASES/turnly-v$new_name-$new_code.apk"
  cp "$apk" "$RELEASES/turnly.apk"
  echo "Published $RELEASES/turnly-v$new_name-$new_code.apk (and turnly.apk)"
  echo "Note: build.gradle now holds the new version; commit it when you commit."
fi

if (( do_web )); then
  echo "Redeploying web app"
  docker compose -f "$COMPOSE" up -d --build
fi
