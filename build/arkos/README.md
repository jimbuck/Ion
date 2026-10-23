# @TITLE@ for the R36S (ArkOS)

Published by the Ion engine with `dotnet publish -p:IonTarget=r36s` (NativeAOT, linux-arm64, OpenGL ES, SDL, fullscreen
640x480).

## Installing

Copy the contents of the `ports` folder onto the SD card's ports folder, keeping the script and the folder side by side:

    /roms/ports/@NAME@.sh
    /roms/ports/@NAME@/@EXE@
    /roms/ports/@NAME@/appsettings.json
    /roms/ports/@NAME@/appsettings.r36s.json
    /roms/ports/@NAME@/Assets/...

On a two-card setup the ports folder is `/roms2/ports`. Then restart EmulationStation (or the device) and start the game
from the Ports menu.

## What the launcher does

- Uses the system SDL2 (KMSDRM video, the device's controls) in place of the bundled one.
- Sets `DOTNET_ENVIRONMENT=r36s`, so `appsettings.r36s.json` is loaded over `appsettings.json`.
- Writes the game's output to `@NAME@/log.txt`; read it first when the game does not start.

## Settings

`appsettings.r36s.json` holds the handheld defaults. Anything in it can be changed there, or on the command line in the
launcher script (for example `--Ion:Graphics:Gles:MaxFeatureLevel=Es30`).

## Requirements

ArkOS (or another arm64 Linux with glibc 2.27 or later, Mesa's Panfrost OpenGL ES 3.1 and SDL2). Nothing else is
needed: the game is a single native executable with its assets; no .NET runtime is installed on the device.
