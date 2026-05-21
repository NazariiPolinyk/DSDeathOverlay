# DSDeathOverlay

External, **read-only** in-game death counter for **Dark Souls: Remastered**.

A small WPF window that sits on top of the game, displays your current Death Num,
and never touches a single byte of the game's memory or files. No DLL injection,
no `WriteProcessMemory`, no `CreateRemoteThread`. Functionally equivalent to the
public projects [DSDeaths](https://github.com/Quidrex/DSDeaths) and
[DSDC](https://github.com/cisc0disco/DSDC).

## Safety

- The overlay opens DSR's process handle with **only** `PROCESS_VM_READ |
  PROCESS_QUERY_INFORMATION` (`0x0410`). It cannot write to the game even if it
  wanted to.
- It does **not** drop any files into the DSR install folder.
- It runs as a **separate process** at the same integrity level as your normal
  Steam launch (no admin required).
- The technique is the same one used by the death counters above and has been
  considered safe by their communities for years. Still: if From's anti-cheat
  ever changes, no one can promise you anything. **Use at your own risk.**

## Requirements

- Windows 10 or 11 (x64).
- .NET 9 runtime (only needed for a non-self-contained build; the `publish`
  command below produces a single self-contained `.exe`).
- DSR set to **Borderless Fullscreen** in Options -> Display Settings. A regular
  WPF overlay cannot draw on top of true exclusive fullscreen.

## Build

```powershell
dotnet build DSDeathOverlay.sln -c Release
```

Or, for a single-file self-contained binary you can copy anywhere:

```powershell
dotnet publish src/DSDeathOverlay/DSDeathOverlay.csproj -c Release `
  -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output is `src/DSDeathOverlay/bin/Release/net9.0-windows/win-x64/publish/DSDeathOverlay.exe`.

## Run

1. Start Dark Souls Remastered.
2. Set it to **Borderless Fullscreen**.
3. Launch `DSDeathOverlay.exe`. The counter appears at the top-left.

The overlay shows a status string while it's still looking for the game or
waiting for you to load a save:

- `Deaths: --   (waiting for DSR)` - game isn't running yet.
- `Deaths: --   (locating)` - found the game, scanning for the death counter address.
- `Deaths: --   (load a save)` - on the title screen / no character loaded.
- `Deaths: 42` - reading live values.

## Hotkeys

| Key | Action |
| --- | --- |
| `F8` | Toggle **edit mode**. The background tints purple and the window becomes draggable with the left mouse button. Click-through is restored when you press F8 again. |
| `F9` | Show/hide the overlay. |

Position and font size are saved to
`%LOCALAPPDATA%\DSDeathOverlay\settings.json` when the app exits.

## Diagnostics

If the counter never appears, check `deaths.log` next to `DSDeathOverlay.exe`. It
records: process opens, the AOB hit address, and any read failures.

## How it works

1. Find the `DarkSoulsRemastered.exe` process and open it read-only.
2. Pattern-scan the main module for the static-load instruction of `ChrClassBase`
   (`48 8B 05 ? ? ? ? 48 85 C0 ? ? F3 0F 58 80 AC 00 00 00`).
3. Resolve the `mov rax, [rip+disp32]` RIP-relative reference to get the absolute
   address of the static pointer slot.
4. Every 250 ms: dereference that slot to get the live `ChrClassBase` instance,
   then read the 4-byte int at `+0x98` (Death Num).

Source for the pattern + offsets: the public Cheat Engine table
[JohrnaJohrna/RemasterCETable](https://github.com/JohrnaJohrna/RemasterCETable/blob/master/DarkSoulsRemastered.CT)
(entries `"TrueDeath"`, `"True Death Num"`, `"Death Num"`).

## Layout

```
DarkSoulsRemasteredDeathCounter/
  DSDeathOverlay.sln
  README.md
  src/DSDeathOverlay/
    App.xaml(.cs)
    MainWindow.xaml(.cs)            # transparent, topmost, click-through window
    OverlayViewModel.cs             # INotifyPropertyChanged + DisplayText
    BoolToBrushConverter.cs
    app.manifest                    # asInvoker (no UAC), Per-Monitor DPI
    Memory/
      NativeMethods.cs              # P/Invoke (kernel32, psapi, user32)
      ProcessAccess.cs              # opens DSR read-only, enumerates modules, reads bytes
      PatternScanner.cs             # pure-data AOB+mask scanner (unit-tested)
      DeathReader.cs                # ChrClassBase resolve + +0x98 read
    Services/
      DeathPoller.cs                # 250ms loop; reconnects on game restart
    Settings/
      SettingsStore.cs              # JSON persistence under %LOCALAPPDATA%
    Logging/
      ILogger.cs
      FileLogger.cs                 # deaths.log next to the exe
  test/DSDeathOverlay.Tests/
    PatternScannerTests.cs          # 11 xUnit tests
```

## Credits

- Pattern + offsets: [JohrnaJohrna/RemasterCETable](https://github.com/JohrnaJohrna/RemasterCETable).
- Prior art: [Quidrex/DSDeaths](https://github.com/Quidrex/DSDeaths),
  [cisc0disco/DSDC](https://github.com/cisc0disco/DSDC).

## License

This project is provided as-is for personal use. Dark Souls: Remastered is
copyright FromSoftware/Bandai Namco.
