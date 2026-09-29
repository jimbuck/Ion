#!/bin/bash
# @TITLE@ for ArkOS (R36S and other RK3326 handhelds), published by Ion with: dotnet publish -p:IonTarget=r36s
# Copy this script and the "@NAME@" folder, side by side, into the ports folder of the SD card (/roms/ports, or
# /roms2/ports on a two-card setup), then start it from the Ports menu. See README.md next to the ports folder.

GAMEDIR="$(cd "$(dirname "$0")/@NAME@" && pwd)"
cd "$GAMEDIR" || exit 1

# Use the system SDL2 (built with the KMSDRM video driver and the device's controls) instead of the generic one bundled
# with the game: Silk.NET loads libSDL2-2.0.so from the game folder first. exFAT cards have no symbolic links, so copy.
for sdl in /usr/lib/aarch64-linux-gnu/libSDL2-2.0.so.0 /usr/lib/libSDL2-2.0.so.0; do
  if [ -e "$sdl" ]; then
    ln -sf "$sdl" libSDL2-2.0.so 2>/dev/null || cp -f "$sdl" libSDL2-2.0.so
    break
  fi
done

# The r36s environment loads appsettings.r36s.json (OpenGL ES, SDL, fullscreen 640x480) over appsettings.json.
export DOTNET_ENVIRONMENT=r36s

chmod +x "./@EXE@" 2>/dev/null
"./@EXE@" "$@" > "$GAMEDIR/log.txt" 2>&1
status=$?
printf '\033c' > /dev/tty1 2>/dev/null
exit $status
# end of launcher
