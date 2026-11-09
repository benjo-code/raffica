#!/usr/bin/env bash
# Builds libtesseract.dylib (Tesseract + Leptonica in one shared library, C API exported) for macOS, one architecture per run.
# Meant to run on a macOS runner (macos-15); the workflow native-tesseract.yml starts it once for x86_64 and once for arm64 (cross build on arm64).
#   packaging/native/build-macos.sh <repo-root> <out-dir> <x86_64|arm64>
# No OpenMP, no libcurl, no libarchive, no image decoders; no Homebrew prefix is searched, so only the system libraries (libSystem, libc++) are linked.
set -euo pipefail

repo=$(cd "${1:?repo root}" && pwd)
out=$(mkdir -p "${2:?out dir}" && cd "$2" && pwd)
arch="${3:?architecture (x86_64 or arm64)}"
case "$arch" in
  x86_64|arm64) ;;
  *) echo "build-macos: architecture must be x86_64 or arm64, not '$arch'" >&2; exit 1 ;;
esac
# eval, not source <(...): the bash 3.2 of macOS reads nothing from a process substitution and does not fail.
eval "$(grep -E '^[A-Z_]+=' "$repo/packaging/native/versions.txt")"
: "${MACOS_DEPLOYMENT_TARGET:?MACOS_DEPLOYMENT_TARGET is not set in versions.txt}"

# cmake and ninja come with the runner image; Homebrew only fills a gap.
if ! command -v cmake >/dev/null || ! command -v ninja >/dev/null; then
  brew install cmake ninja
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# RAFFICA_NATIVE_SOURCES=<dir with leptonica/ and tesseract/ checkouts> skips the clone; the commit is checked either way.
fetch() { # url tag commit dir
  name=$(basename "$4")
  if [ -n "${RAFFICA_NATIVE_SOURCES:-}" ] && [ -d "$RAFFICA_NATIVE_SOURCES/$name" ]; then
    cp -r "$RAFFICA_NATIVE_SOURCES/$name" "$4"
  else
    git clone --quiet --depth 1 --branch "$2" "$1" "$4"
  fi
  actual=$(git -C "$4" rev-parse 'HEAD^{commit}')
  [ "$actual" = "$3" ] || { echo "build-macos: $1 $2 is $actual, expected $3" >&2; exit 1; }
}

fetch "$LEPTONICA_URL" "$LEPTONICA_TAG" "$LEPTONICA_COMMIT" "$work/leptonica"
fetch "$TESSERACT_URL" "$TESSERACT_TAG" "$TESSERACT_COMMIT" "$work/tesseract"

# Cross build pitfall (the x86_64 library is built on an arm64 runner): Tesseract's CMakeLists.txt chooses the SIMD sources from
# CMAKE_SYSTEM_PROCESSOR, and CMake sets that from the HOST (arm64) whenever CMAKE_SYSTEM_NAME is not given, so a -D on the command line
# is overwritten by CMakeDetermineSystem (seen in CI: "CMAKE_SYSTEM_PROCESSOR=<arm64>", HAVE_NEON TRUE, the link of the x86_64 library
# fails on tesseract::DotProductNEON). Setting CMAKE_SYSTEM_NAME instead would turn the configure into a cross compile (try_run is no
# longer run and needs preset results, find roots and tool lookup change) for both projects. So the build stays native and a small file,
# included by CMAKE_PROJECT_INCLUDE as the last step of project(), sets CMAKE_SYSTEM_PROCESSOR to the target; the Tesseract
# configure output is checked for it. With x86_64 Tesseract then compiles the AVX, AVX2, AVX512F, FMA and SSE4.1 sources with their own
# per-file -m flags and picks one at run time (simddetect); -march=native only exists behind ENABLE_NATIVE, which is OFF (also passed
# explicitly: a -march flag would crash the library on an older Intel Mac). Leptonica reads no processor variable and is left alone.
cat > "$work/target-processor.cmake" <<CMAKE
set(CMAKE_SYSTEM_PROCESSOR "$arch")
message(STATUS "build-macos: CMAKE_SYSTEM_PROCESSOR set to $arch for the target")
CMAKE
common=(-G Ninja -DCMAKE_BUILD_TYPE=Release
  "-DCMAKE_OSX_ARCHITECTURES=$arch" -DENABLE_NATIVE=OFF "-DCMAKE_OSX_DEPLOYMENT_TARGET=$MACOS_DEPLOYMENT_TARGET"
  "-DCMAKE_IGNORE_PREFIX_PATH=/opt/homebrew;/usr/local")

cmake -S "$work/leptonica" -B "$work/lept-build" "${common[@]}" \
  -DCMAKE_INSTALL_PREFIX="$work/prefix" -DBUILD_SHARED_LIBS=OFF -DBUILD_PROG=OFF -DSW_BUILD=OFF \
  -DENABLE_ZLIB=OFF -DENABLE_PNG=OFF -DENABLE_GIF=OFF -DENABLE_JPEG=OFF -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF \
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON
cmake --build "$work/lept-build" --target install

cmake -S "$work/tesseract" -B "$work/tess-build" "${common[@]}" "-DCMAKE_PROJECT_INCLUDE=$work/target-processor.cmake" \
  -DBUILD_SHARED_LIBS=ON -DSW_BUILD=OFF -DOPENMP_BUILD=OFF -DDISABLE_CURL=ON -DDISABLE_ARCHIVE=ON \
  -DBUILD_TRAINING_TOOLS=OFF -DBUILD_TESTS=OFF -DGRAPHICS_DISABLED=ON -DENABLE_LTO=OFF \
  -DLeptonica_DIR="$work/prefix/lib/cmake/leptonica" \
  -DCMAKE_BUILD_WITH_INSTALL_NAME_DIR=ON -DCMAKE_INSTALL_NAME_DIR=@loader_path \
  "-DCMAKE_SHARED_LINKER_FLAGS=-Wl,-headerpad_max_install_names -Wl,-dead_strip" | tee "$work/tess-configure.log"
# The SIMD choice must follow the target: Tesseract prints the processor it read.
grep -q "CMAKE_SYSTEM_PROCESSOR=<$arch>" "$work/tess-configure.log" || { echo "build-macos: Tesseract configured for another processor than $arch" >&2; exit 1; }
cmake --build "$work/tess-build" --target libtesseract

lib=$(find "$work/tess-build" -maxdepth 1 -name 'libtesseract*.dylib' -type f | head -1)
[ -n "$lib" ] || { echo "build-macos: libtesseract.dylib not found" >&2; exit 1; }
cp "$lib" "$out/libtesseract.dylib"
# CMake writes the install name itself: CMAKE_BUILD_WITH_INSTALL_NAME_DIR with CMAKE_INSTALL_NAME_DIR=@loader_path gives
# @loader_path/<file name>, and the file name still carries the version (libtesseract.5.5.dylib). The copy is named libtesseract.dylib,
# so the id is set once more here; -headerpad_max_install_names above leaves room for it. The id is then checked and the build fails if it is wrong.
install_name_tool -id @loader_path/libtesseract.dylib "$out/libtesseract.dylib"
id=$(otool -D "$out/libtesseract.dylib" | tail -n 1)
[ "$id" = "@loader_path/libtesseract.dylib" ] || { echo "build-macos: install name is '$id', expected @loader_path/libtesseract.dylib" >&2; exit 1; }
strip -x "$out/libtesseract.dylib"
# Editing and stripping invalidate the signature, and an arm64 library without one does not load: sign it again (ad-hoc).
codesign --force --sign - "$out/libtesseract.dylib"
# The result must be the requested architecture only, and an x86_64 library must carry no NEON code.
got=$(lipo -archs "$out/libtesseract.dylib")
[ "$got" = "$arch" ] || { echo "build-macos: lipo says '$got', expected '$arch'" >&2; exit 1; }
if [ "$arch" = x86_64 ] && nm -gU "$out/libtesseract.dylib" | grep -qi 'neon'; then
  echo "build-macos: the x86_64 library lists NEON symbols (CMAKE_SYSTEM_PROCESSOR taken from the host?)" >&2
  nm -gU "$out/libtesseract.dylib" | grep -i 'neon' >&2 || true
  exit 1
fi
echo "build-macos: $(stat -f %z "$out/libtesseract.dylib") bytes ($arch) in $out/libtesseract.dylib"
