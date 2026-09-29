#!/usr/bin/env bash
# Stage 7 publish size and startup measurement (docs/plans/benchmarks/2026-09-stage7-publish/README.md).
#
# Publishes a sample with the Ion publishing presets and records, per target:
#   - the executable's size and the size of everything shipped (the publish folder without .dbg symbols),
#   - the startup time: process start to exit after one headless frame (--Ion:Headless=true --Ion:Run:Frames=1), the
#     median and minimum of RUNS runs. This is an upper bound on startup to first frame (it includes Destroy and exit).
#
# Targets: linux-x64 (native) and r36s (linux-arm64 NativeAOT, cross-compiled, run under qemu-aarch64 when available).
#
# Usage: measure.sh [output dir]      (default: ./artifacts/stage7-publish)
# Environment:
#   SAMPLE            project to publish (default Ion.Examples/Ion.Examples.Breakout.ECS)
#   RUNS              startup runs per target (default 10)
#   TARGETS           presets to measure (default "linux-x64 r36s")
#   IonArm64SysRoot   arm64 sysroot for the cross build and for qemu (build/arm64-sysroot.py); without it the Ubuntu cross
#                     packages link (/usr/aarch64-linux-gnu, also used as the qemu root)
#   DESKTOP_LIMIT_MB  desktop executable size gate (default 30, the Stage 7 acceptance line); exceeding it fails
#
# Prints a Markdown table and writes it, with results.json, to the output directory.
set -euo pipefail

repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
out="$(mkdir -p "${1:-$repo/artifacts/stage7-publish}" && cd "${1:-$repo/artifacts/stage7-publish}" && pwd)"
sample="${SAMPLE:-Ion.Examples/Ion.Examples.Breakout.ECS}"
runs="${RUNS:-10}"
targets="${TARGETS:-linux-x64 r36s}"
limit_mb="${DESKTOP_LIMIT_MB:-30}"
name="$(basename "$sample")"
sysroot="${IonArm64SysRoot:-}"

qemu=""
for candidate in qemu-aarch64-static qemu-aarch64; do
	if command -v "$candidate" > /dev/null; then qemu="$candidate"; break; fi
done
qemu_root="${sysroot:-/usr/aarch64-linux-gnu}"

now_ns() { date +%s%N; }

# Median and minimum, in milliseconds, of the numbers on stdin.
stats() { sort -n | awk '{ a[NR] = $1 } END { m = (NR % 2) ? a[(NR + 1) / 2] : (a[NR / 2] + a[NR / 2 + 1]) / 2; printf "%.1f %.1f\n", m, a[1] }'; }

table="| Target | Executable | Shipped (no .dbg) | Startup median | Startup min | Runs | Where |
|---|---|---|---|---|---|---|"
json="["
status=0

for target in $targets; do
	pub="$out/$target"
	rm -rf "$pub" "$pub-arkos"
	echo "== publishing $sample for $target"
	args=(-p:IonTarget="$target" -o "$pub" -p:TrimmerSingleWarn=false)
	if [ -n "$sysroot" ]; then args+=(-p:IonArm64SysRoot="$sysroot"); fi
	dotnet publish "$repo/$sample" "${args[@]}" 2>&1 | tee "$out/$target-publish.log"

	exe="$pub/$name"
	exe_bytes=$(stat -c %s "$exe")
	shipped_bytes=$(find "$pub" -type f ! -name '*.dbg' -printf '%s\n' | awk '{ s += $1 } END { print s }')
	exe_mb=$(awk -v b="$exe_bytes" 'BEGIN { printf "%.1f", b / 1048576 }')
	shipped_mb=$(awk -v b="$shipped_bytes" 'BEGIN { printf "%.1f", b / 1048576 }')

	run=()
	where="native"
	case "$target" in
		linux-x64) run=("$exe") ;;
		r36s|linux-arm64)
			if [ "$(uname -m)" = "aarch64" ]; then run=("$exe")
			elif [ -n "$qemu" ]; then run=("$qemu" -L "$qemu_root" "$exe"); where="$qemu (user mode, TCG)"
			fi ;;
		*) ;;
	esac

	median="n/a"; min="n/a"; measured=0
	if [ ${#run[@]} -gt 0 ]; then
		times="$out/$target-startup.txt"
		: > "$times"
		(cd "$pub" && "${run[@]}" --Ion:Headless=true --Ion:Run:Frames=1 > "$out/$target-run.log" 2>&1)
		for _ in $(seq "$runs"); do
			start=$(now_ns)
			(cd "$pub" && "${run[@]}" --Ion:Headless=true --Ion:Run:Frames=1 > /dev/null 2>&1)
			end=$(now_ns)
			echo $(( (end - start) / 1000 )) | awk '{ printf "%.1f\n", $1 / 1000 }' >> "$times"
		done
		read -r median min < <(stats < "$times")
		measured=$runs
		median="$median ms"; min="$min ms"
	else
		where="not run (no arm64 machine or qemu)"
	fi

	table+="
| $target | $exe_mb MB | $shipped_mb MB | $median | $min | $measured | $where |"
	json+="{\"target\":\"$target\",\"executableBytes\":$exe_bytes,\"shippedBytes\":$shipped_bytes,\"startupMedianMs\":\"${median% ms}\",\"startupMinMs\":\"${min% ms}\",\"runs\":$measured,\"where\":\"$where\"},"

	if [ "$target" != "r36s" ] && [ "$target" != "linux-arm64" ] && [ "$exe_bytes" -gt $((limit_mb * 1048576)) ]; then
		echo "::error::$name for $target is $exe_mb MB, over the $limit_mb MB desktop limit. Look at the ILC map (-p:IlcGenerateMapFile=true) for what grew."
		status=1
	fi
done

json="${json%,}]"
echo "$json" > "$out/results.json"
{
	echo "# $name publish sizes and startup ($(date -u +%Y-%m-%d), $(uname -m), $(dotnet --version))"
	echo
	echo "$table"
} > "$out/results.md"
cat "$out/results.md"
exit $status
