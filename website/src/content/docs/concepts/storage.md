---
title: Storage
description: Read and write game content, assets, settings and saves with IPersistentStorage, and configure where each folder lives with Ion:Storage.
sidebar:
  order: 9
---

`IPersistentStorage` is Ion's small file-system abstraction. It gives every game four well-known folders, resolves them
the same way on every platform, and is what the asset loaders read through. It is registered by
`IonApplication.CreateBuilder`, so every game has it without calling any `AddX`.

```csharp
using Ion;

public sealed class SettingsSystem(IPersistentStorage storage)
{
    [Init]
    public void Load(GameTime dt)
    {
        if (File.Exists(storage.User.GetPath("settings.json")))
        {
            using var stream = storage.User.Read("settings.json");
            // ... deserialize
        }
    }

    public void Save(string json) => storage.User.Write(json, "settings.json");
}
```

## The four folders

| Property | Default location | Use |
|---|---|---|
| `Game` | The executable's folder (`AppContext.BaseDirectory`) | Read-only content shipped with the game. |
| `Assets` | `Assets` under `Game` | Textures, fonts, sounds, models. The asset loaders read from here. |
| `User` | A per-game folder under the user's local application data, named after `Ion:Title` | Per-user data: settings, profiles. |
| `Saves` | `Saves` under `User` | Save games. |

On Windows the user folder is under `%LOCALAPPDATA%`; on Linux and macOS it is under the platform's local application
data folder (on Linux, typically `~/.local/share`). The folder name is `Ion:Title` (default `Ion`) with the characters
that are invalid on any supported OS (`< > : " / \ | ? *` and control characters) replaced by `_`, so the name is the
same everywhere: a title of `Block:Breaker` gives a folder named `Block_Breaker`.

If the environment has no local application data folder (some containers and service accounts), the user folder falls
back to `UserData/<title>` under the executable's folder.

:::caution[Set a title]
Two games that keep the default title `Ion` share the same user folder. Set `Ion:Title` in `appsettings.json` (the
templates set it to the game's name).
:::

## Configuring the folders

Override any folder in the `Ion:Storage` section (`StorageConfig`):

| Key | Default | Resolved against |
|---|---|---|
| `Ion:Storage:GamePath` | The executable's folder | The executable's folder, when relative |
| `Ion:Storage:AssetsPath` | `Assets` under the game folder | The game folder, when relative |
| `Ion:Storage:UserPath` | The per-game local application data folder | The executable's folder, when relative |

`Saves` always follows `User`.

```json title="appsettings.json"
{
  "Ion": {
    "Title": "Arena",
    "Storage": {
      "AssetsPath": "Content",
      "UserPath": "portable-data"
    }
  }
}
```

A portable build that keeps its data next to the executable sets `UserPath` to a relative folder, as above. The Breakout
mobile heads point `GamePath` at the folder where the app unpacked its content:

```csharp
var builder = IonApplication.CreateBuilder([$"--Ion:Storage:GamePath={contentRoot}", "--Ion:Title=Ion Breakout"]);
```

## IPersistentStorageProvider

Each folder is an `IPersistentStorageProvider`. Every method takes the path as `params string[]` segments, combined with
`Path.Combine`, so you never build separators by hand:

| Method | Behavior |
|---|---|
| `GetPath(params string[] path)` | The full path of a file or folder. Does not touch the disk. |
| `Read(params string[] path)` | Opens the file for reading and returns a `Stream`. Throws a `FileNotFoundException` that names the full path, and points out a file whose name differs only in casing. |
| `Write(string text, params string[] path)` | Writes text, creating parent folders, replacing the file. |
| `Write(byte[] bytes, params string[] path)` | Writes bytes, creating parent folders, replacing the file. |
| `OpenWrite(params string[] path)` | Opens the file for writing (created or truncated) and returns a `BinaryWriter`. Creates parent folders. |
| `Append(string text, params string[] path)` | Appends text, creating the file and parent folders if needed. |
| `List(params string[] path)` | The files and folders in a folder. |
| `CreateDirectory(params string[] path)` | Creates a folder (and its parents). |
| `DeleteFile(params string[] path)` | Deletes a file. |
| `DeleteDirectory(params string[] path)` | Deletes an empty folder. |

Folders are created on first write, never eagerly, so a game that never saves leaves no empty folders behind.

```csharp
public sealed class SaveSystem(IPersistentStorage storage)
{
    public void Save(int slot, byte[] data) => storage.Saves.Write(data, $"slot{slot}.sav");

    public byte[]? Load(int slot)
    {
        var path = storage.Saves.GetPath($"slot{slot}.sav");
        if (!File.Exists(path)) return null;
        using var stream = storage.Saves.Read($"slot{slot}.sav");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void SaveBinary(int score, float time)
    {
        using var writer = storage.Saves.OpenWrite("best.bin");
        writer.Write(score);
        writer.Write(time);
    }

    public IEnumerable<string> Slots() =>
        Directory.Exists(storage.Saves.GetPath()) ? storage.Saves.List() : [];
}
```

:::note[Synchronous by design]
Storage calls block, like every other step in Ion (steps cannot be `async`). Small settings and save files are fast
enough to read and write in a step. For large files, do the work in `Init`, or start it on a background thread from a
step and check for the result in later frames.
:::

## Case sensitivity

Linux and macOS file systems are case-sensitive. A game developed on Windows that loads `Bonk.wav` from a file named
`bonk.wav` fails elsewhere. `Read` detects this and says so in the exception:

```text
File 'Bonk.wav' was not found at '/opt/game/Assets/Bonk.wav'. A file with different casing exists: 'bonk.wav'.
File names are case-sensitive on Linux and macOS.
```

## Assets go through storage

The asset loaders (textures, fonts, sounds, glTF models, cube maps) read files through `storage.Assets`, and asset hot
reload watches that folder. So `Ion:Storage:AssetsPath` moves where every asset is loaded from, and a test can point it
at a temporary folder. Load assets through `IAssetManager` rather than reading them yourself; see
[Assets](/Ion/rendering/assets/).

## Testing with storage

Replace the service in a test to keep saves out of the real user folder, or point the folders at a temporary directory
through configuration:

```csharp
var temp = Directory.CreateTempSubdirectory().FullName;
using var run = IonTestHost.RunEntryPoint<Program>(60, host => host
    .WithConfiguration("Ion:Storage:UserPath", temp));
```

## See also

- [Services and configuration](/Ion/concepts/services-and-configuration/): configuration sources and the `Ion` sections.
- [Assets](/Ion/rendering/assets/): loading and hot reload.
- [Publishing](/Ion/platforms/publishing/): what is copied next to the executable.
- [Configuration reference](/Ion/reference/configuration/).
