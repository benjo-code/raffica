## Reference app, measured with its own startup protocol

Repository `benjo-code/Pagewing`, commit `0373bf6fbba568176b848af889be70f9660d3b18`, platform windows.
Machine note: Ryzen 7 7800X3D, 30.9 GiB, NVIDIA RTX 4070 SUPER, 1920x1080 at 60 Hz, scaling 100 %, Windows 11 build 26300, balanced power plan, mains power (same machine as the M1 measurements); reference run of the predecessor v1.0.0

| Case | Median (ms) | Min (ms) | Max (ms) | Runs | Script exit |
|---|---:|---:|---:|---:|---:|
| Cold start, text-basic.pdf (reference protocol) | 183.7 | 168.5 | 1081.3 | 10 | 0 |
| Warm start, text-basic.pdf (reference protocol, 3 warm-up runs) | 182.4 | 168.3 | 183.2 | 10 | 0 |
| 1000-page file to the first sharp page (reference protocol) | 183.1 | 180.2 | 184.8 | 10 | 0 |
| Second file in the running instance, text-basic.pdf then hello_world.pdf (reference protocol) | 31.1 | 30.2 | 32.1 | 10 | 0 |
| Cold start, hello_world.pdf (the file of the PDF target) | 175.5 | 161.2 | 177.7 | 10 | 0 |
| Warm start, hello_world.pdf (the file of the PDF target) | 174.7 | 161.2 | 178.4 | 10 | 0 |
| Second file in the running instance, hello_world.pdf then outline.pdf (the files of the PDF target) | 30.9 | 19.1 | 31.4 | 10 | 0 |

Published size of `out/reference` without debug symbols (`*.pdb`, `*.dbg`): 58.45 MiB.

Script exit code: 0 = median within the reference app's own budget limit, 1 = above it or a failed run, 2 = wrong invocation (fails the step). A value of 1 is a result here, not a failure; the budget of the reference app is not a limit of this repository.
The cases without the prefix are the reference app's own protocol; the three `samefiles` cases use the files of the PDF target. Valid only on a machine with a GPU and an active display; the machine notes are in the artifact.
