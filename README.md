# DSDeathOverlay

External, **read-only** in-game death counter for the FromSoftware Souls games.

A small WPF window that sits on top of the game, displays your current death
count and your per-boss deaths, and never touches a single byte of the game's
memory or files. No DLL injection, no `WriteProcessMemory`, no
`CreateRemoteThread`. Same technique as the public projects
[DSDeaths](https://github.com/Quidrex/DSDeaths) and
[DSDC](https://github.com/cisc0disco/DSDC).

## Supported games

| Tag  | Game                                       | Process name             | How it's located                       |
| ---- | ------------------------------------------ | ------------------------ | -------------------------------------- |
| DSR  | Dark Souls: Remastered                     | `DarkSoulsRemastered.exe`| **AOB scan** (patch-resilient)         |
| DS2  | Dark Souls II: Scholar of the First Sin    | `DarkSoulsII.exe`        | Pointer chain (current patch)          |
| DS3  | Dark Souls III                             | `DarkSoulsIII.exe`       | Pointer chain (current patch)          |
| SEK  | Sekiro: Shadows Die Twice                  | `sekiro.exe`             | Pointer chain (current patch)          |

The overlay auto-detects whichever supported game is running and switches the
on-screen tag accordingly (e.g. `DS3 - Deaths: 42`). DSR uses an array-of-bytes
scan that survives small patches; the others use module-relative pointer chains
ported from DSDeaths and will need their offsets refreshed if From ships an
update — see [Updating offsets](#updating-offsets-when-a-patch-breaks-things).

### Elden Ring is intentionally excluded

Elden Ring uses **Easy Anti-Cheat (EAC)**, which actively blocks
`ReadProcessMemory` from outside processes. Making the overlay work would
require disabling EAC (and forfeiting online play); EAC is meaningfully
riskier than the Steam-side detection the older games have. If you want this,
use [DSDeaths](https://github.com/Quidrex/DSDeaths) directly and follow its
warnings.

## Safety

- The overlay opens the game's process handle with **only** `PROCESS_VM_READ |
  PROCESS_QUERY_INFORMATION` (`0x0410`). It cannot write to the game even if it
  wanted to.
- It does **not** drop any files into the game install folder.
- It runs as a **separate process** at the same integrity level as your normal
  Steam launch (no admin required).
- No DLL injection, no `CreateRemoteThread`, no `WriteProcessMemory`.
- The technique is the same one used by the death counters above and has been
  considered safe by their communities for years. Still: **use at your own
  risk.** Anti-cheat behaviour can change without notice.

## Download

Pre-built Windows builds are published on
**[GitHub Releases](https://github.com/NazariiPolinyk/DSDeathOverlay/releases)**.

1. Open the latest release.
2. Download `DSDeathOverlay-v*-win-x64.zip` from the latest release (or run the
   **Release** workflow manually and download the zip from that run’s
   **Artifacts** if no release exists yet).
3. Extract the zip anywhere.
4. Run `DSDeathOverlay.exe` (keep `games.json` in the same folder if you want
   to edit offsets without rebuilding).

No .NET install is required — the release is a self-contained single-file exe.

> **Private repo:** only GitHub users with access to this repository can
> download releases. To share with friends, add them as collaborators or make
> the repository public.

### Publishing a new release (maintainers)

Push a version tag and GitHub Actions builds and uploads the zip automatically:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

You can also run the **Release** workflow manually from the repo’s **Actions**
tab; the zip appears under that run’s **Artifacts** (useful for testing before
tagging).

## Requirements

- Windows 10 or 11 (x64).
- .NET 9 runtime (only needed for a non-self-contained build; the `publish`
  command below produces a single self-contained `.exe`).
- The game set to **Borderless Fullscreen** (Options -> Display Settings).
  A WPF overlay cannot draw on top of true exclusive fullscreen.

## Build

```powershell
dotnet build DSDeathOverlay.sln -c Release
```

Or, for a single-file self-contained binary you can copy anywhere:

```powershell
dotnet publish src/DSDeathOverlay/DSDeathOverlay.csproj -c Release `
  -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

The output is `src/DSDeathOverlay/bin/Release/net9.0-windows/win-x64/publish/DSDeathOverlay.exe`.

## Run

1. Start any supported From game.
2. Set it to **Borderless Fullscreen**.
3. Launch `DSDeathOverlay.exe`. The counter appears at the top-left with the
   matching game tag.

The overlay shows a status string while it's still working things out:

- `Deaths: --   (waiting for game)` - no supported game is running.
- `DS3 - Deaths: --   (locating)` - found the game, performing one-time setup.
- `DS3 - Deaths: --   (load a save)` - on the title screen / no character loaded.
- `DS3 - Deaths: 42` - reading live values.

## Per-boss death tracking

The overlay can attribute individual deaths to specific bosses on top of the
total counter. The compact display becomes `DS3 - Deaths: 42 | Vordt: 3` when a
boss is selected, and pressing `F10` reveals an expanded list with every boss
in the game and how many times you've died to each.

Because none of the supported games store a "currently fighting which boss"
field in memory that's safe to read, the overlay uses **manual selection**:
before (or during) a fight, mark the active boss and any subsequent death is
credited to it. When you beat the boss, clear the selection. There's no
automatic detection in this release — manual mode is the same approach used
by the popular [HitCounterManager](https://github.com/topeterk/HitCounterManager)
tool, and it works correctly for every supported game from day one.

Workflow:

1. Press `F11` (or `Shift+F11` to cycle backwards) to step through the boss
   list for the active game until your boss shows next to the death count, or
   enter edit mode with `F8` and click the boss name in the expanded list.
2. Play. Every death that arrives while a boss is active increments that
   boss's counter.
3. When the boss dies, press `F11` until you reach "none" (or cycle past the
   end of the list), or pick a different boss.

Per-boss counts are saved to
`%LOCALAPPDATA%\DSDeathOverlay\boss-deaths.json` and survive restarts. The
boss roster comes from `bosses.json` next to the .exe (with an embedded
fallback baked into the binary, identical to how `games.json` works); edit
the file to add bosses, rename them, or — once community-supplied offsets are
available — enable automatic detection by switching a game's `detection.type`
from `manual` to `pointerChainFlag`. See the comment block at the top of
`bosses.json` for the schema.

## Hotkeys

| Key          | Action |
| ------------ | --- |
| `F8`         | Toggle **edit mode**. The background tints purple, the window becomes draggable with the left mouse button, and small `reset` and `x` buttons appear. Click-through is restored when you press F8 again. |
| `F9`         | Show/hide the overlay. |
| `F10`        | Show/hide the expanded per-boss death list. |
| `F11`        | Cycle the active boss forward (none → first → second → … → none). |
| `Shift+F8`   | Reset overlay position to the top-left default (`20, 20`). |
| `Shift+F9`   | Close DSDeathOverlay. |
| `Shift+F11`  | Cycle the active boss backward. |

You can also close the app from the on-screen `x` button when in edit mode,
or reset all per-boss counts for the current game with the `reset` button
next to it.

Position, font size, and panel state are saved to
`%LOCALAPPDATA%\DSDeathOverlay\settings.json` when the app exits.

## Updating offsets when a patch breaks things

If a game patch shifts the pointer chain and the overlay starts reading
garbage (or zero) for DS2 / DS3 / Sekiro, edit `games.json` next to
`DSDeathOverlay.exe`:

```json
{
  "games": [
    {
      "displayName": "Dark Souls III",
      "shortTag": "DS3",
      "processName": "DarkSoulsIII",
      "moduleName": "DarkSoulsIII.exe",
      "chainOffsets64": [ 74867384, 152 ]
    }
  ]
}
```

`chainOffsets64` is walked exactly the same way DSDeaths does it: start at
`module_base`, for each offset add it then dereference 8 bytes (4 for
`chainOffsets32`), interpret the low 32 bits of the last deref as the death
count. The shipped values are the ones from the current DSDeaths master at
the time this was written; check that project for newer numbers if they break.

DSR is unaffected: its AOB pattern keeps matching as long as the surrounding
instructions don't change. If even that breaks, update the `aobPattern`
field in the DSR entry.

If `games.json` is missing or malformed, the app falls back to the embedded
copy baked into the .exe so it always boots.

## Diagnostics

If the counter never appears, check `deaths.log` next to `DSDeathOverlay.exe`.
It records: process opens, the AOB hit address, pointer-chain bitness, the
first successful death-count read, and any read failures.

### Overlay stuck on `(load a save)` with a save actually loaded

This means the reader could not produce a usable value. `deaths.log` will say
which step failed, throttled to once every five seconds:

- `[DS2] chain read failed: hop 2: TryReadUInt64(0x...) failed (offset 0xD0)`
  — a pointer-chain hop dereferenced unreadable memory. The chain in
  `games.json` is out of date (game patched) or wrong for this build.
- `[DS2] chain read failed: hop 1: previous deref was null` — the game has
  not allocated the player struct yet; usually a few seconds of patience
  fixes it. If it persists, the chain is broken.
- `[DS2] chain produced negative value -267242410 (endpoint 0x...); rejecting
  as garbage` — the chain walked through readable memory but landed on a slot
  whose contents are not a 4-byte death-count int (almost always a
  pointer-aligned field). The offsets in `games.json` are stale for this game
  build; update them from DSDeaths and restart.
- `[DSR] AOB pattern NOT found in DarkSoulsRemastered.exe` — the pattern in
  `games.json` no longer matches the game's binary.

On the first successful read the log also prints
`[DS2] first read: 12345 (chain endpoint = 0x...)` so you can sanity-check
the number against the Majula gravestone (offline mode).

If a hop is broken, update the chain or AOB in `games.json` next to the .exe
and restart. The shipped chains track [DSDeaths](https://github.com/Quidrex/DSDeaths/blob/master/Program.cs)
master; if DSDeaths has newer values, paste them in and try again.

## How it works

```mermaid
flowchart LR
    Start["DeathPoller"] --> Load["GameProfileStore.Load"]
    Load --> Scan["ProcessAccess.TryOpenAnyKnown"]
    Scan --> Match["matching GameProfile"]
    Match --> Choose{"profile.UsesAob?"}
    Choose -- yes --> Aob["AobDeathReader (DSR)"]
    Choose -- no --> Chain["PointerChainDeathReader (DS2/DS3/SEK)"]
    Aob --> Read["TryReadDeathCount"]
    Chain --> Read
    Read --> VM["OverlayViewModel"]
    VM --> Window["WPF transparent overlay"]
```

1. Load `games.json` (external file beats embedded fallback).
2. Find any supported game's process; open it with read-only access; detect
   bitness via `IsWow64Process`.
3. Build the right reader for the matched game:
   - **AOB**: scan the main module for the cheat-engine-style pattern,
     resolve the RIP-relative `mov` to get a static pointer slot, dereference
     it on each tick, read the 4-byte death count at the configured offset.
   - **Pointer chain**: walk the module-relative offset list a la DSDeaths.
4. Render `Deaths: N` in a transparent topmost click-through WPF window.
   Re-assert `HWND_TOPMOST` once per second so the overlay survives
   borderless-fullscreen focus changes.

## Layout

```
DarkSoulsRemasteredDeathCounter/
  DSDeathOverlay.sln
  README.md
  src/DSDeathOverlay/
    App.xaml(.cs)
    MainWindow.xaml(.cs)              # transparent topmost click-through window
    OverlayViewModel.cs               # DisplayText with per-game tag
    BoolToBrushConverter.cs
    app.manifest                      # asInvoker (no UAC), Per-Monitor DPI
    games.json                        # game profiles (also embedded as fallback)
    Memory/
      NativeMethods.cs                # P/Invoke (kernel32, psapi, user32)
      IMemoryReader.cs                # abstraction for unit-testable reads
      ProcessAccess.cs                # OpenProcess + module enum (read-only)
      PatternScanner.cs               # pure AOB+mask scanner (unit-tested)
      GameProfile.cs                  # per-game record
      GameProfileStore.cs             # loads games.json (file beats embedded)
      IDeathReader.cs                 # interface for the two reader strategies
      AobDeathReader.cs               # DSR: pattern scan + RIP-relative
      PointerChainDeathReader.cs      # DS2/DS3/SEK: walk fixed offset chain
      BossCatalog.cs                  # bosses.json data model
      BossCatalogStore.cs             # loads bosses.json (file beats embedded)
      IBossContextReader.cs           # "which boss is active right now?"
      ManualBossContextReader.cs      # active boss set by hotkey / click
      PointerChainBossContextReader.cs# future-ready auto-detection strategy
    Services/
      DeathPoller.cs                  # 250ms loop; auto-reconnects between games
      BossDeathTracker.cs             # attributes deltas to the active boss
      BossDeathStore.cs               # JSON persistence for boss-deaths.json
    Settings/
      SettingsStore.cs                # JSON persistence under %LOCALAPPDATA%
    Logging/
      ILogger.cs
      FileLogger.cs                   # deaths.log next to the exe
    bosses.json                       # per-game boss rosters (also embedded)
  test/DSDeathOverlay.Tests/
    PatternScannerTests.cs            # AOB + mask scanner
    PointerChainWalkTests.cs          # DSDeaths-style walker (incl. 32-bit)
    GameProfileStoreTests.cs          # JSON parsing + embedded fallback
    BossCatalogStoreTests.cs          # bosses.json parsing + embedded fallback
    BossDeathTrackerTests.cs          # delta attribution / persistence / reset
    PointerChainBossContextReaderTests.cs
    FakeMemoryReader.cs               # in-memory IMemoryReader for tests
```

## Credits

- DSR pattern + offset: [JohrnaJohrna/RemasterCETable](https://github.com/JohrnaJohrna/RemasterCETable).
- DS2 / DS3 / Sekiro pointer chains: ported from
  [Quidrex/DSDeaths](https://github.com/Quidrex/DSDeaths) `Program.cs`.
- Prior art: [Quidrex/DSDeaths](https://github.com/Quidrex/DSDeaths),
  [cisc0disco/DSDC](https://github.com/cisc0disco/DSDC).

## License

This project is provided as-is for personal use. Dark Souls: Remastered,
Dark Souls II: SotFS, Dark Souls III, and Sekiro: Shadows Die Twice are
copyright FromSoftware / Bandai Namco / Activision respectively.
