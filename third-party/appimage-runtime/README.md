# AppImage runtime: third-party licenses

Only the AppImage contains these components: the statically linked type2 runtime of the AppImage project (release `20251108`, `tools/package.cs`, constant `AppImageRuntime`; see `Docs/DECISIONS.md`, entry "Raffica PDF: packaging and release (M2.6)"). They are in no publish folder, so the entries in `licenses.json` have no `files`. Their sources: `https://github.com/AppImage/type2-runtime` (the runtime with its build scripts) and the projects named below. **Source offer (LGPL-2.1, libfuse):** every release carries `fuse-3.15.0.tar.xz` (libfuse 3.15.0, SHA-256 `70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0`) and `type2-runtime-20251108.tar.gz` (source of the runtime with the build scripts and the patch `patches/libfuse/mount.c.diff`, SHA-256 `4c4f6df4647c9f01f871d7edd3716d8aeeffda9d22ffbebe3fccd95a6ab52c95`); both are also listed in `SHA256SUMS-linux.txt`. Built and recorded by `dotnet run tools/package.cs -- runtime-sources`. The runtime names its components itself with `--appimage-help` (section "License").

All files are unchanged (except LF line endings, which they had anyway), apart from `zlib-LICENSE`.

| File | Component (version) | Source | SHA-256 of the original file |
| --- | --- | --- | --- |
| `LICENSE` | type2-runtime `20251108` (MIT) | `https://raw.githubusercontent.com/AppImage/type2-runtime/20251108/LICENSE` | `aa154fc9070614bbe7921f89db11efd1dba7a1f3a41685958110e2230f9c0ca1` |
| `libfuse-LGPL-2.1.txt` | libfuse 3.15.0 (LGPL-2.1-only for the library) | `https://raw.githubusercontent.com/libfuse/libfuse/fuse-3.15.0/LGPL2.txt` | `dc626520dcd53a22f727af3ee42c770e56c97a64fe3adb063799d8ab032fe551` |
| `squashfuse-LICENSE` | squashfuse 0.5.2 (BSD-2-Clause) | `https://raw.githubusercontent.com/vasi/squashfuse/0.5.2/LICENSE` | `9e909cc8a8ba27b1a649c964cf9eda37de911f3138c2b64e6d2f01703904ac13` |
| `zstd-LICENSE` | zstd 1.5.6 (BSD-3-Clause; zstd is also licensed under GPL-2.0, the BSD license is the one used) | `https://raw.githubusercontent.com/facebook/zstd/v1.5.6/LICENSE` | `7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8` |
| `zlib-LICENSE` | zlib 1.3.1 (Zlib) | header comment of `https://raw.githubusercontent.com/madler/zlib/v1.3.1/zlib.h` (lines 2 to 24, without comment characters and indentation; `zlib.h` has SHA-256 `8a5579af72ea4f427ff00a4150f0ccb3fc5c1e4379f726e101133b1ab9fc600c`) | `310aa3a481cd76f612f9e1c8f6c385d080f58791f260fc287d334c0688d969df` (the extract) |
| `musl-COPYRIGHT` | musl libc 1.2.5 (MIT, plus notes on other licenses of single files) | `COPYRIGHT` in the tarball `https://musl.libc.org/releases/musl-1.2.5.tar.gz` (SHA-256 of the tarball `a9a118bbe84d8764da0ea0d28b3ab3fae8477fc7e4085d90102b8596fc7c75e4`); equal to the file at `https://git.musl-libc.org/cgit/musl/plain/COPYRIGHT?h=v1.2.5` | `f9bc4423732350eb0b3f7ed7e91d530298476f8fec0c6c427a1c04ade22655af` |
| `mimalloc-LICENSE` | mimalloc 2.1.7 (MIT) | `https://raw.githubusercontent.com/microsoft/mimalloc/v2.1.7/LICENSE` | `19c99805e7a44a34b297a75d1edea9985e300066dfc024d5c99d4236d4573b5d` |

Origin of the versions:

- libfuse 3.15.0 and squashfuse 0.5.2 are built from source by the script `scripts/common/install-dependencies.sh` of runtime release `20251108` (fixed versions and checksums in it). The strings `3.15.0`, `squashfuse 0.5.2`, `1.5.6` (zstd) and `inflate 1.3.1` (zlib) are also in the runtime file.
- zstd, zlib, musl and mimalloc come from the packages of Alpine 3.21 (`scripts/docker/Dockerfile`). zstd 1.5.6 and zlib 1.3.1 are checked against the runtime file. **musl 1.2.5 and mimalloc 2.1.7 are not checked against the file** (neither contains a version string); they are the versions Alpine 3.21 ships to the best of our knowledge. Both license texts change between patch versions at most in the year; compare again with a new runtime release.
- zlib ships its license only in the head of `zlib.h` (and in `README`); `zlib-LICENSE` is that comment without the `/*` frame and indentation.
