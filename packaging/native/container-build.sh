#!/usr/bin/env bash
# Starts build-linux.sh in the container ubuntu:22.04 (the same floor as the release files: glibc 2.35).
#   packaging/native/container-build.sh [out-dir]       (default artifacts/native/linux-x64)
# RAFFICA_NATIVE_SOURCES=<dir with leptonica/ and tesseract/ checkouts> hands in sources cloned on the host instead of cloning in the
# container (a machine whose container cannot run git over https); build-linux.sh checks the commits either way.
set -euo pipefail
# ubuntu:22.04 pinned by digest, the same value as in release.yml (Docs/DECISIONS.md, "CI on hosted runners once public (M2.7)").
image=${UBUNTU_IMAGE:-ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11}
repo=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:-$repo/artifacts/native/linux-x64}
mkdir -p "$out"
extra=()
if [ -n "${RAFFICA_NATIVE_SOURCES:-}" ]; then
  extra=(-v "$RAFFICA_NATIVE_SOURCES:/sources:ro" -e RAFFICA_NATIVE_SOURCES=/sources)
fi
docker run --rm -v "$repo:/src:ro" -v "$(cd "$out" && pwd):/out" "${extra[@]}" -e HOME=/tmp "$image" \
  bash /src/packaging/native/build-linux.sh /src /out
