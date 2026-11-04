## Reference app, measured with its own startup protocol

Repository `benjo-code/Pagewing`, commit `0373bf6fbba568176b848af889be70f9660d3b18`, platform linux.
Machine note: HP ProBook, Core i5-8265U, 15.4 GiB, Intel UHD Graphics 620, 1920x1080, Wayland session with XWayland, CachyOS (kernel 7.3.0-4-cachyos), balanced profile, mains power (same machine as the M1 measurements); reference run of the predecessor v1.0.0

| Case | Median (ms) | Min (ms) | Max (ms) | Runs | Script exit |
|---|---:|---:|---:|---:|---:|
| Cold start, text-basic.pdf (reference protocol) | 210.2 | 197.0 | 232.8 | 10 | 0 |
| Warm start, text-basic.pdf (reference protocol, 3 warm-up runs) | 209.2 | 196.0 | 236.8 | 10 | 0 |
| 1000-page file to the first sharp page (reference protocol) | 208.6 | 203.1 | 216.3 | 10 | 0 |
| Second file in the running instance, text-basic.pdf then hello_world.pdf (reference protocol) | 39.3 | 38.1 | 41.1 | 10 | 0 |
| Cold start, hello_world.pdf (the file of the PDF target) | 210.8 | 196.1 | 230.2 | 10 | 0 |
| Warm start, hello_world.pdf (the file of the PDF target) | 208.0 | 194.5 | 238.3 | 10 | 0 |
| Second file in the running instance, hello_world.pdf then outline.pdf (the files of the PDF target) | 46.4 | 45.2 | 51.5 | 10 | 0 |

Published size of `out/reference` without debug symbols (`*.pdb`, `*.dbg`): 55.90 MiB.

Script exit code: 0 = median within the reference app's own budget limit, 1 = above it or a failed run, 2 = wrong invocation (fails the step). A value of 1 is a result here, not a failure; the budget of the reference app is not a limit of this repository.
The cases without the prefix are the reference app's own protocol; the three `samefiles` cases use the files of the PDF target. Valid only on a machine with a GPU and an active display; the machine notes are in the artifact.
