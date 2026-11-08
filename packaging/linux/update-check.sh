#!/usr/bin/env bash
# Manual check of the AppImage self-update on a real Linux desktop (the owner's laptop: real display, real FUSE mount).
# Called by .github/workflows/linux-update-check.yml, or by hand from the repository root:
#
#   bash packaging/linux/update-check.sh
#
# What it does, in this order (packaging/README.md, section "Manual update check", step 4):
#   1. Checks the machine: display, /dev/fuse, fusermount, dotnet, clang, mksquashfs, a /tmp that may run programs.
#   2. Makes a throw-away test key (it lives only in a folder below /tmp, which is removed at the end), builds the CLI and
#      the PDF app twice as the TEST BUILD of the updater (versions OLD_VERSION and NEW_VERSION, default 0.0.1 and 0.0.2),
#      packs both as AppImages and puts the old one into a folder of its own.
#   3. Starts the test server of tools/update-testserver.cs on 127.0.0.1 with the new AppImage (signed with the test key).
#   4. Starts the OLD AppImage the way a user does, through the FUSE mount (APPIMAGE_EXTRACT_AND_RUN is never set), on the
#      real display, with its own settings and data folders, and WAITS for you: "Update available" must show in the title
#      bar; click "Install" (if nothing shows after a minute: command palette, "Check for updates").
#   5. Checks what the updater did to the file: it is now the new AppImage (SHA-256), still mode 755, no leftover file next
#      to it, a new process started from it, and the new file starts through FUSE as well. You check by eye that the new
#      version is open (About card) and the document is back; closing the window ends the run.
#
# SECURITY: this runs repository code with the rights of the user who started the runner. It uses no secret and no
# network except the downloads of the pinned AppImage runtime and of the NuGet packages. The server answers on
# 127.0.0.1 only; the test key is useless against any release (a release build trusts only its own keys).
set -euo pipefail

OLD_VERSION="${OLD_VERSION:-0.0.1}"
NEW_VERSION="${NEW_VERSION:-0.0.2}"
WAIT_MINUTES="${WAIT_MINUTES:-20}"   # how long to wait for the click on "Install"
HOLD_MINUTES="${HOLD_MINUTES:-5}"    # how long the new version stays open for you to look at it

note() { echo "== $*"; }
fail() { echo "::error title=Linux update check::$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "missing: $1 ($2)"; }

[[ "$OLD_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$OLD_VERSION" != "$NEW_VERSION" ]] \
  || fail "OLD_VERSION and NEW_VERSION must be two different versions X.Y.Z"
[[ "$WAIT_MINUTES" =~ ^[0-9]{1,3}$ && "$HOLD_MINUTES" =~ ^[0-9]{1,3}$ ]] || fail "WAIT_MINUTES and HOLD_MINUTES must be numbers of minutes"

note "Checking the machine"
[ "$(uname -s)" = Linux ] && [ "$(uname -m)" = x86_64 ] || fail "needs Linux on x86_64"
[ -n "${WAYLAND_DISPLAY:-}${DISPLAY:-}" ] \
  || fail "no display: start the runner (./run.sh) in the desktop session of the user, not through ssh or as a service"
[ -e /dev/fuse ] || fail "/dev/fuse is missing"
command -v fusermount3 >/dev/null 2>&1 || command -v fusermount >/dev/null 2>&1 \
  || fail "missing: fusermount3 or fusermount (sudo pacman -S fuse3)"
need dotnet "sudo pacman -S dotnet-sdk"
need clang "sudo pacman -S clang zlib (Native AOT)"
need mksquashfs "sudo pacman -S squashfs-tools"
need ss "sudo pacman -S iproute2"
need curl "sudo pacman -S curl"
need sha256sum "coreutils"
# The build refuses these as environment variables (Directory.Build.props); better to say so before a long build.
for name in RafficaUpdateTestBuild RafficaUpdateTestPort RafficaUpdateTestKey; do
  [ -z "${!name:-}" ] || fail "$name is set as an environment variable; the test build takes it only as a -p: property"
done
# An AppImage started from another AppImage's environment would not touch its own file (AppImageUpdater): start clean.
unset APPIMAGE APPDIR ARGV0 OWD APPIMAGE_EXTRACT_AND_RUN
test -f global.json && test -f tools/package.cs || fail "run it from the repository root"
repo="$PWD"
dotnet --version

work="$(mktemp -d "${TMPDIR:-/tmp}/raffica-update-check.XXXXXX")"
if findmnt -no OPTIONS -T "$work" 2>/dev/null | tr ',' '\n' | grep -qx noexec; then
  rm -rf "$work"
  fail "$work lies on a file system mounted noexec; set TMPDIR to a folder that may run programs"
fi
user_appimage=""
server_pid=""

# The ids of the processes that were started from the AppImage under test (the runtime sets APPIMAGE for the program).
app_pids() {
  [ -n "$user_appimage" ] || return 0
  local dir
  for dir in /proc/[0-9]*; do
    if [ -r "$dir/environ" ] && tr '\0' '\n' <"$dir/environ" 2>/dev/null | grep -qxF "APPIMAGE=$user_appimage"; then
      echo "${dir#/proc/}"
    fi
  done
}

cleanup() {
  local code=$?
  trap - EXIT
  local pid
  for pid in $(app_pids); do kill "$pid" 2>/dev/null || true; done
  if [ -n "$server_pid" ]; then
    kill -- "-$server_pid" 2>/dev/null || kill "$server_pid" 2>/dev/null || true
  fi
  # The throw-away private key goes with the folder.
  rm -rf "$work"
  exit "$code"
}
trap cleanup EXIT

# A free port on the loopback interface.
port=""
for _ in $(seq 1 50); do
  candidate=$((20000 + RANDOM % 40000))
  if ! ss -ltn | grep -q ":$candidate "; then port="$candidate"; break; fi
done
[ -n "$port" ] || fail "no free port found"

note "Test key and test builds ($OLD_VERSION, $NEW_VERSION), port $port"
key="$work/test.key"
dotnet run tools/package.cs -- update-keygen --id test-1 --private-out "$key" >"$work/keygen.out"
pub="$(sed -n 's/.*FromBase64String("\([^"]*\)").*/\1/p' "$work/keygen.out")"
[ -n "$pub" ] || fail "could not read the public test key from the output of update-keygen"

test_props=("-p:RafficaUpdateTestBuild=true" "-p:RafficaUpdateTestPort=$port" "-p:RafficaUpdateTestKey=$pub")
dotnet publish src/Raffica.Cli -c Release -r linux-x64 -p:Version="$OLD_VERSION" -o "$work/cli"
dotnet publish src/Raffica.Pdf -c Release -r linux-x64 "${test_props[@]}" -p:Version="$OLD_VERSION" -o "$work/app-old"
dotnet publish src/Raffica.Pdf -c Release -r linux-x64 "${test_props[@]}" -p:Version="$NEW_VERSION" -o "$work/app-new"

note "AppImages"
dotnet run tools/package.cs -- runtime-fetch
dotnet run tools/package.cs -- layout --rid linux-x64 --app "$work/app-old" --cli "$work/cli" --out "$work/layout-old"
dotnet run tools/package.cs -- layout --rid linux-x64 --app "$work/app-new" --cli "$work/cli" --out "$work/layout-new"
old_name="RafficaPdf-$OLD_VERSION-x86_64.AppImage"
new_name="RafficaPdf-$NEW_VERSION-x86_64.AppImage"
mkdir -p "$work/release-old" "$work/release-new" "$work/user"
dotnet run tools/package.cs -- appimage --layout "$work/layout-old" --version "$OLD_VERSION" --out "$work/release-old/$old_name"
dotnet run tools/package.cs -- appimage --layout "$work/layout-new" --version "$NEW_VERSION" --out "$work/release-new/$new_name"
new_sha="$(sha256sum "$work/release-new/$new_name" | cut -d' ' -f1)"

user_appimage="$work/user/RafficaPdf.AppImage"
cp "$work/release-old/$old_name" "$user_appimage"
chmod 755 "$user_appimage"

note "Test server on 127.0.0.1:$port with version $NEW_VERSION"
dotnet run tools/update-testserver.cs -- prepare --version "$NEW_VERSION" --key "$key" --out "$work/server" "$work/release-new/$new_name"
setsid dotnet run tools/update-testserver.cs -- serve --root "$work/server" --port "$port" >"$work/server.log" 2>&1 &
server_pid=$!
ready=false
for _ in $(seq 1 90); do
  if curl -fsS "http://127.0.0.1:$port/releases/latest" 2>/dev/null | grep -q "\"v$NEW_VERSION\""; then ready=true; break; fi
  kill -0 "$server_pid" 2>/dev/null || { cat "$work/server.log"; fail "the test server ended"; }
  sleep 1
done
$ready || { cat "$work/server.log"; fail "the test server does not answer on port $port"; }

note "The old AppImage starts through the FUSE mount"
# Own settings and data folders: the restarted new version inherits the environment, so the user's real settings stay untouched.
export XDG_CONFIG_HOME="$work/xdg/config" XDG_DATA_HOME="$work/xdg/data" XDG_CACHE_HOME="$work/xdg/cache"
mkdir -p "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME"
probe="$("$user_appimage" --print-string TabView --lang en)" || fail "the old AppImage does not start (FUSE mount): see above"
[ -n "$probe" ] || fail "the old AppImage printed nothing for --print-string TabView"
echo "AppImage answered through FUSE: TabView = $probe"

"$user_appimage" --settings-file "$work/settings.json" "$repo/testdata/pdfs/text-basic.pdf" >"$work/app.log" 2>&1 &
sleep 15
before="$(app_pids | sort -n | tr '\n' ' ')"
[ -n "$before" ] || { cat "$work/app.log"; fail "the old version did not stay open"; }
echo "The old version ($OLD_VERSION) is open (processes: $before)"

old_inode="$(stat -c %i "$user_appimage")"
msg="Raffica update check: in the Raffica window ($OLD_VERSION) the title bar shows 'Update available'. Click it, then 'Install'. If it does not show within a minute: Ctrl+Shift+P, 'Check for updates'."
echo "::notice title=Your turn::$msg"
echo "$msg"
command -v notify-send >/dev/null 2>&1 && notify-send -u critical "Raffica update check" "Click 'Update available', then 'Install' in the Raffica window." || true

note "Waiting for the install (up to $WAIT_MINUTES minutes)"
deadline=$(($(date +%s) + WAIT_MINUTES * 60))
gone=0
changed=false
while [ "$(date +%s)" -lt "$deadline" ]; do
  if [ "$(stat -c %i "$user_appimage" 2>/dev/null || echo 0)" != "$old_inode" ]; then changed=true; break; fi
  if [ -z "$(app_pids)" ]; then gone=$((gone + 1)); else gone=0; fi
  [ "$gone" -lt 3 ] || { cat "$work/app.log"; fail "the app was closed (or crashed) before the update was installed"; }
  sleep 2
done
$changed || fail "no install within $WAIT_MINUTES minutes: the file was not replaced"

note "What the updater did"
failures=0
check() { if [ "$2" = true ]; then echo "ok   $1"; else echo "FAIL $1"; failures=$((failures + 1)); fi; }
sleep 3
now_sha="$(sha256sum "$user_appimage" | cut -d' ' -f1)"
check "the file is now the new AppImage ($NEW_VERSION, SHA-256 matches)" "$([ "$now_sha" = "$new_sha" ] && echo true || echo false)"
check "the mode is 755" "$([ "$(stat -c %a "$user_appimage")" = 755 ] && echo true || echo false)"
leftovers="$(ls -A "$work/user" | grep -vxF "RafficaPdf.AppImage" || true)"
check "no leftover file next to the AppImage${leftovers:+ ($leftovers)}" "$([ -z "$leftovers" ] && echo true || echo false)"

restarted=false
for _ in $(seq 1 30); do
  for pid in $(app_pids); do
    case " $before " in *" $pid "*) ;; *) restarted=true ;; esac
  done
  $restarted && break
  sleep 1
done
check "a new process started from the updated file" "$restarted"
if ! $restarted; then
  echo "Processes that look like the app (pid ppid seconds-running command, then APPIMAGE of its environment):"
  ps -eo pid,ppid,etimes,args | grep -E 'RafficaPdf|\.mount_' | grep -v grep || true
  for dir in /proc/[0-9]*; do
    if [ -r "$dir/cmdline" ] && tr '\0' ' ' <"$dir/cmdline" 2>/dev/null | grep -qE 'RafficaPdf|\.mount_'; then
      echo "  ${dir#/proc/}: $(tr '\0' '\n' <"$dir/environ" 2>/dev/null | grep -E '^APPIMAGE=' || echo 'no APPIMAGE')"
    fi
  done
fi

# unset again: the new file is also started through FUSE by a user
probe_new="$("$user_appimage" --print-string TabView --lang en 2>/dev/null || true)"
check "the updated file starts through FUSE" "$([ -n "$probe_new" ] && echo true || echo false)"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Linux update check ($OLD_VERSION to $NEW_VERSION)"
    echo "- $failures automatic check(s) failed"
    echo "- By eye: the new version is open (About card shows $NEW_VERSION) and the document is back"
  } >>"$GITHUB_STEP_SUMMARY"
fi
[ "$failures" -eq 0 ] || fail "$failures check(s) failed (see above)"

echo
echo "All automatic checks passed. Look at the screen now: the new version ($NEW_VERSION, About card) is open and the"
echo "document is back. Close the Raffica window to end the run (it is closed after $HOLD_MINUTES minutes in any case)."
hold_deadline=$(($(date +%s) + HOLD_MINUTES * 60))
while [ "$(date +%s)" -lt "$hold_deadline" ] && [ -n "$(app_pids)" ]; do sleep 2; done
note "Done"
