#!/usr/bin/env bash
# Builds Box2D v3.1.0 as a static library for linux-x64 and linux-arm64 the way Box2D's own CMake does for deterministic
# math (-ffp-contract=off: no fused multiply-add contraction), with clang. Used by the Stage 5b decision benchmark to check
# whether a contraction-free build of the native library gives the same results on x64 and arm64 (the packaged arm64
# binary of Box2D.NET.Native.Release 3.1.0 contains 1,562 fused multiply-add instructions, the x64 one none).
#
# Usage: build.sh <box2d source checkout (tag v3.1.0)> <output directory>
# Output: <out>/linux-x64/libbox2d.a and <out>/linux-arm64/libbox2d.a
# The arm64 build needs the Ubuntu cross packages (libc6-dev-arm64-cross) for its headers.
set -euo pipefail

src="$1"
out="$2"

# The bindings call Box2D's inline helpers (b2MakeRot, b2Rot_GetAngle, ...) as exported functions, as the packaged
# natives provide them: compile the inline headers once more with external linkage.
mkdir -p "$out"
exports="$out/inline_exports.c"
cat > "$exports" <<'C'
#include "box2d/base.h"
#undef B2_INLINE
#define B2_INLINE __attribute__((visibility("default")))
#include "box2d/math_functions.h"
#include "box2d/id.h"
C

build() {
	local rid="$1"; shift
	local dir="$out/$rid"
	mkdir -p "$dir/obj"
	for file in "$src"/src/*.c "$exports"; do
		clang -c -O2 -std=gnu17 -fPIC -ffp-contract=off -DNDEBUG -I"$src/include" -I"$src/src" "$@" "$file" -o "$dir/obj/$(basename "$file" .c).o"
	done
	rm -f "$dir/libbox2d.a"
	llvm-ar rcs "$dir/libbox2d.a" "$dir"/obj/*.o
	echo "$dir/libbox2d.a"
}

build linux-x64 --target=x86_64-linux-gnu
build linux-arm64 --target=aarch64-linux-gnu --sysroot=/usr/aarch64-linux-gnu -I/usr/aarch64-linux-gnu/include
