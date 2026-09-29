# Stage 7: publish sizes and startup (September 2026)

`measure.sh` publishes a sample with the Ion publishing presets (`docs/platforms/publishing.md`) and records, per
target, the executable's size, the size of everything shipped (the publish folder without `.dbg` symbols) and the
startup time: process start to exit after one headless frame (`--Ion:Headless=true --Ion:Run:Frames=1`), the median
and minimum of `RUNS` runs. That includes building the host, the schedule and the headless backends, loading the
textures, font and sounds, one frame, `Destroy` and process exit, so it is an upper bound on start to first frame.

```sh
python3 build/arm64-sysroot.py /tmp/sysroot-bionic-arm64          # once, for the arm64 cross build
IonArm64SysRoot=/tmp/sysroot-bionic-arm64 docs/plans/benchmarks/2026-09-stage7-publish/measure.sh ./artifacts/stage7-publish
```

Environment: `SAMPLE` (default `Ion.Examples/Ion.Examples.Breakout.ECS`), `TARGETS` (default `linux-x64 r36s`), `RUNS`
(default 10), `IonArm64SysRoot`, `DESKTOP_LIMIT_MB` (default 30: a desktop executable above it fails the script, and CI).
An arm64 target runs natively on an arm64 machine, under `qemu-aarch64` (user mode, with the sysroot as its root) on
x64, or is only sized when neither is available. The script writes `results.md`, `results.json`, the per-run times and
the publish and run logs; the CI AOT lane uploads them as the `Stage7PublishResults` artifact (with the r36s ArkOS
layout) and adds the table to the job summary.

## Results

Intel Xeon @ 2.10 GHz VM (x86_64), .NET SDK 10.0.112, NativeAOT 10.0.12, 26 September 2026. The files next to this
README (`results.md`, `results.json`, `*-startup.txt`) are that run.

| Target | Executable | Shipped (no .dbg) | Startup median | Startup min | Runs | Where |
|---|---|---|---|---|---|---|
| linux-x64 | 12.6 MB | 16.4 MB | 45.6 ms | 32.4 ms | 10 | native |
| r36s | 11.6 MB | 15.0 MB | 611.1 ms | 570.2 ms | 10 | qemu-aarch64-static (user mode, TCG) |

- **Desktop (linux-x64):** 12.6 MB executable, 46 ms (median; 32 ms best) to the end of the first headless frame.
  Both are well inside the acceptance line (under 30 MB, under 300 ms). The rest of the shipped 16.4 MB is
  `libSDL2-2.0.so` (2.1 MB), `libopenal.so` (1.2 MB), `libglfw.so.3` (0.4 MB) and the assets (0.35 MB). The separate
  symbols file (`.dbg`, 21 MB) is not shipped.
- **Handheld (r36s, linux-arm64):** 11.6 MB, needs `GLIBC_2.27` at most, so it loads on ArkOS (glibc 2.30). The 0.6 s is under QEMU's user-mode emulation (TCG) of a Cortex-A class CPU on the x64
  host, not the R36S; it is recorded as the emulated upper bound, and the device number (acceptance: under one second)
  still has to be taken on the hardware.
- Every sample published with the `linux-x64` preset is listed in `docs/platforms/publishing.md`.

## Not measured

- Windowed start to first presented frame (needs a display and a GPU on the runner); the headless run skips the
  window, swapchain and pipeline creation.
- Windows and macOS sizes: NativeAOT does not cross-compile between operating systems, so they come from CI's
  `publish-desktop` job on Windows and macOS runners (`ION_PUBLISH_CI`), not from this machine.
- Android and iOS package sizes (see `docs/platforms/publishing.md`, Mobile).
