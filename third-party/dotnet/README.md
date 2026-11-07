# .NET runtime (Native AOT) – third-party licenses

Raffica PDF is a Native AOT program: the runtime (GC, type system) and the needed parts of the class library are linked into the binary (MIT). The license files come from the runtime package of the released version 10.0.14 (`microsoft.netcore.app.runtime.nativeaot.win-x64`); the Linux runtime package and the platform packages of the compiler carry the same `THIRD-PARTY-NOTICES.TXT` (compared for 10.0.12 and 10.0.14: identical text apart from the line endings).

The build packages `Microsoft.DotNet.ILCompiler` and `Microsoft.NET.ILLink.Tasks` (MIT) are needed only when building and are not part of the program.

| File here | Source (NuGet cache, read only) | SHA-256 of the original file |
|---|---|---|
| `LICENSE` | `microsoft.netcore.app.runtime.nativeaot.win-x64/10.0.14/LICENSE.TXT` | `d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b` (CRLF; same text as the 10.0.12 file, whose LF form is stored here) |
| `THIRD-PARTY-NOTICES.txt` | `microsoft.netcore.app.runtime.nativeaot.win-x64/10.0.14/THIRD-PARTY-NOTICES.TXT` | `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21` (CRLF; same text as the 10.0.12 file, whose LF form is stored here) |

Taken over unchanged except for LF line endings.
