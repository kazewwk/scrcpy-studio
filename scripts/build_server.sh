#!/usr/bin/env bash
set -euo pipefail

source_dir="$(realpath "$1")"
output="$(realpath -m "$2")"
sdk="${ANDROID_HOME:?Set ANDROID_HOME to an Android SDK with platform 36 and build-tools 36.0.0}"
platform="$sdk/platforms/android-36"
tools="$sdk/build-tools/36.0.0"
version="$(sed -n "s/.*version: '\([^']*\)'.*/\1/p" "$source_dir/meson.build" | head -n 1)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/generated/com/genymobile/scrcpy" "$work/classes" "$(dirname "$output")"
cat > "$work/generated/com/genymobile/scrcpy/BuildConfig.java" <<JAVA
package com.genymobile.scrcpy;

public final class BuildConfig {
    public static final boolean DEBUG = false;
    public static final String VERSION_NAME = "$version";
}
JAVA

aidl="$source_dir/server/src/main/aidl"
for input in "$aidl/android/content/IOnPrimaryClipChangedListener.aidl" "$aidl/android/view/IDisplayWindowListener.aidl"; do
    "$tools/aidl" -o"$work/generated" -I"$aidl" -p "$platform/framework.aidl" "$input"
done

mapfile -d '' java_sources < <(find "$source_dir/server/src/main/java" "$work/generated" -name '*.java' -print0 | sort -z)
javac -encoding UTF-8 -source 8 -target 8 -bootclasspath "$platform/android.jar" \
    -classpath "$tools/core-lambda-stubs.jar" -d "$work/classes" "${java_sources[@]}"
jar --create --file "$work/classes.jar" -C "$work/classes" .
"$tools/d8" --release --min-api 21 --lib "$platform/android.jar" \
    --output "$work/scrcpy-server.zip" "$work/classes.jar"
cp "$work/scrcpy-server.zip" "$output"
unzip -t "$output"
