# Shell probe: medians, budgets and limits

The budget is the median of the frame mode rounded up (ms to 10, memory to 5 MiB, size to 1 MiB); the limit is the budget × 1.25. Empty is the baseline; frame minus empty is the cost of the frame.

## linux-probook

| Case | Empty | Frame | Frame cost | Budget | Limit |
|---|---|---|---|---|---|
| cold (ms) | 133.67 | 198.22 | 64.55 | 200.0 | 250.0 |
| warm (ms) | 130.71 | 200.33 | 69.62 | 210.0 | 262.5 |
| input median (ms) | – | 16.55 | – | 20.0 | 25.0 |
| input p99 (ms) | – | 33.48 | – | – | – |
| input max (ms) | – | 47.28 | – | – | – |
| memory (MiB) | 99.87 | 124.6 | 24.73 | 125.0 | 156.25 |
| size total (MiB) | – | 34.87 | – | 35.0 | 43.75 |
| size zip (MiB) | – | 15.25 | – | 16.0 | 20.0 |
| size fonts (MiB) | – | 2.81 | – | – | – |
| size native (MiB) | – | 13.33 | – | – | – |
| cold after reboot (ms) | – | 159.11 | – | – | – |

## macos-macbook

| Case | Empty | Frame | Frame cost | Budget | Limit |
|---|---|---|---|---|---|
| cold (ms) | 506.22 | 623.22 | 117.0 | 630.0 | 787.5 |
| warm (ms) | 492.86 | 684.97 | 192.11 | 690.0 | 862.5 |
| input median (ms) | – | 16.66 | – | 20.0 | 25.0 |
| input p99 (ms) | – | 128.26 | – | – | – |
| input max (ms) | – | 212.99 | – | – | – |
| memory (MiB) | 56.88 | 82.93 | 26.06 | 85.0 | 106.25 |
| size total (MiB) | – | 36.77 | – | 37.0 | 46.25 |
| size zip (MiB) | – | 16.48 | – | 17.0 | 21.25 |
| size fonts (MiB) | – | 2.81 | – | – | – |
| size native (MiB) | – | 18.78 | – | – | – |

## windows-main-pc

| Case | Empty | Frame | Frame cost | Budget | Limit |
|---|---|---|---|---|---|
| cold (ms) | 302.68 | 426.36 | 123.68 | 430.0 | 537.5 |
| warm (ms) | 310.4 | 415.61 | 105.21 | 420.0 | 525.0 |
| input median (ms) | – | 4.26 | – | 10.0 | 12.5 |
| input p99 (ms) | – | 9.69 | – | – | – |
| input max (ms) | – | 13.44 | – | – | – |
| memory (MiB) | 87.25 | 123.6 | 36.34 | 125.0 | 156.25 |
| size total (MiB) | – | 38.1 | – | 39.0 | 48.75 |
| size zip (MiB) | – | 15.72 | – | 16.0 | 20.0 |
| size fonts (MiB) | – | 2.81 | – | – | – |
| size native (MiB) | – | 17.97 | – | – | – |
| cold after reboot (ms) | – | 558.33 | – | – | – |
