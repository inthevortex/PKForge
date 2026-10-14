# PKForge — development guide

Everything needed to build, test, and extend PKForge. Start here, then
`docs/ARCHITECTURE.md` (layering), `docs/BANK_MODEL.md` (vault storage),
`docs/UI_DESIGN.md` (art direction), `docs/PRIOR_ART.md` (prior-art study).

## What PKForge is

A GPLv3 **.NET MAUI** app for dual-screen gamepad handhelds (developed on the AYN Thor),
with two heads: **Android** (`net11.0-android`, MAUI) and the **Linux desktop**
(`net11.0`, the same MAUI pages hosted by Avalonia through `Avalonia.Controls.Maui`). A
Pokémon **save editor + persistent cross-generation Bank** on **PKHeX.Core** (submodule,
pinned at the revision in `external/PKHeX`) with the **Auto Legality Mod compiled
in-process** (`src/PKForge.AutoMod` recompiles the plugins' sources against our pinned
Core so versions cannot drift).

## Non-negotiable invariants

1. **PKHeX.Core comes from PKForge's fork** (`sofianeelhor/PKForge-PKHeX`, pinned in
   `external/PKHeX`). The fork carries only what PKHeX itself cannot: save formats and data
   for games it does not know (Luminescent Platinum, Pokémon Compass), one `pkforge/*`
   branch per change. Everything else stays in PKForge: all engine access goes through
   adapters in `PKForge.Engine`, and game-specific behaviour (Lumi forms, held items,
   legality) lives there too.
2. **Data safety is sacred** — every write is **validate → backup → atomic write**. No
   exceptions, even for bulk ops. Invalid candidate = no backup, no write (test-covered).
3. **Offline-first** — assets are bundled or cached at runtime; the app works with no network.
4. **Layering** — Domain (interfaces/DTOs) → Engine (PKHeX adapters) → Infrastructure →
   App. The App layer never touches PKHeX.Core directly.
5. **Never ship secrets** — `sgdb.key` (SteamGridDB) is gitignored and must be absent
   from every APK (`unzip -l <apk> | grep -c sgdb.key` must be 0) and every Linux package
   (`package.sh` refuses to build while the key is in the source tree or the payload).

## Build / test

The .NET 11 RC1 SDK and the `maui-android` workload set are pinned in `global.json`
(`rollForward: disable`), and CI installs the same versions from it. Bump both together.
Restore covers both target frameworks, so the workload is needed even for a desktop-only
build. The Android build targets API 37.

```bash
export DOTNET_CLI_HOME=$PWD/.dotnet-home DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
dotnet workload install maui-android
dotnet test tests/PKForge.Engine.Tests
dotnet test tests/PKForge.Domain.Tests
dotnet test tests/PKForge.App.Tests
dotnet build src/PKForge.App/PKForge.App.csproj -f net11.0-android
dotnet build src/PKForge.App/PKForge.App.csproj -f net11.0
# Release APK:
dotnet publish src/PKForge.App/PKForge.App.csproj -f net11.0-android -c Release -o dist/
# Diagnostic APK for on-device testing (adb install -r). It installs beside the
# production app as org.pkforge.app.debug ("PKForge Diagnostics"), embeds its
# assemblies so plain adb install works, and skips the update check.
dotnet publish src/PKForge.App/PKForge.App.csproj -f net11.0-android -c Release \
    -p:DiagnosticBuild=true -o dist-diagnostic/
adb install -r dist-diagnostic/org.pkforge.app.debug-Signed.apk
# Linux desktop head (X11 or XWayland):
dotnet run --project src/PKForge.App/PKForge.App.csproj -f net11.0 -- --windowed --size 1280x800
```

- `TreatWarningsAsErrors=true` everywhere — warnings fail the build. A raw `&` or `<`
  in a `///` comment breaks it. Obsolete APIs (CS0618) fail it too (see SkiaSharp 4 below).
- Platform code lives in `src/PKForge.App/Platforms/Android` and `Platforms/Desktop`; each
  folder compiles only for its own head, and desktop-only code elsewhere sits behind
  `#if PKFORGE_DESKTOP`.
- `PKForgeUpdateRepository` (default `sofianeelhor/PKForge`) is the `owner/repo` the update
  check reads GitHub releases from. Forks building their own releases pass
  `-p:PKForgeUpdateRepository=owner/repo`; `package.sh` and CI set it from the repository.
- The design system can be iterated off-device:
  `dotnet run --project tools/ChromePreview` renders every chrome component to PNG.
- CI (`.github/workflows/build.yml`) runs the tests, the APK and the Linux packages on
  every pull request and push to `main`, and publishes them for version tags.

## Linux desktop head

- **Launch options:** `--fullscreen` (the default), `--windowed`, `--size WxH` (physical
  pixels, implies windowed) and `--wayland` (logged and ignored: the build ships only
  Avalonia's X11 backend). Bad values become log warnings, never a failed launch.
- **Data:** `FileSystem.AppDataDirectory` is `~/.config/PKForge` (`$XDG_CONFIG_HOME`):
  bank, backups, logs, `scan-cache.json`, the sprite pack and preferences
  (`Settings/preferences.dat`). `FileSystem.CacheDirectory` is
  `~/.local/share/PKForge/Cache`. Both folders are named after the desktop
  `AssemblyName` (`PKForge`); renaming it moves every user's data.
- **Keyboard:** arrows = D-pad, Enter/Space = A, Escape/Backspace = B, X = X, Y/Tab = Y,
  Q/Page Up = L, E/Page Down = R, F1/Menu = Start, F2 = Select. In a text box only Enter
  (A) and Escape (B) reach the pad. `KeyboardSource.ResolveKey` is the map.
- **Gamepad:** SDL3 (`ppy.SDL3-CS`) reads any SDL gamepad, buttons by position (South = A,
  East = B, West = X, North = Y). Rumble is off by default; Settings → Misc →
  "Controller rumble" turns on a short buzz for each handled gamepad press.
- **UI size:** Settings → "UI size" (Compact, Standard, Large; Standard by default). The
  whole window is zoomed (`DesktopZoom`): pages lay out on a logical surface with the
  window's aspect ratio, at most 1920, 1280 or 960 units long, scaled to fill the window.
  When a resize or a size change alters the length of a mockup pixel, `DesktopRelayout`
  rebuilds the pages once the window settles and Home is idle.
- **Sprites:** the bundled pixel sprites always work. Once the sprite pack is downloaded
  (Settings → Misc), the boxes, party, summary and editor draw Pokémon HOME renders by
  default; Settings → "Pokémon art" switches back. Android always draws pixel sprites.
- **No second screen** (`NullSecondaryDisplayHost`). Update checks look for the
  `-linux-x64.tar.gz` release asset and open the release page instead of installing.

## Packaging

`tools/Packaging/package.sh [version] [owner/repo]` publishes a self-contained single-file
`linux-x64` build (version defaults to the csproj `ApplicationDisplayVersion`, repository
to `sofianeelhor/PKForge`) and writes to `artifacts/`:

- `pkforge-<version>-linux-x64.tar.gz` — app, icon, `pkforge.desktop` and `install.sh`
  (per-user install to `~/.local/share/pkforge-app`; `--uninstall` removes it).
- `.deb`, `.pkg.tar.zst` (`bsdtar` for the `.MTREE`, `zstd`) and `.rpm` (only when
  `rpmbuild` is installed) installing to `/usr/lib/pkforge`, with the runtime libraries
  (ICU, OpenSSL, fontconfig, X11, GL/EGL) as dependencies.

Pre-release versions such as `3.2.0-beta1` are spelled so each format sorts them before
the final release.

## Engine API facts (verified against the pin — trust these over the wiki)

- `SaveUtil.TryGetSaveFile(byte[], out SaveFile)`; `save.Write()`; `save.BoxCount`,
  `save.BoxSlotCount` (**per-box**), `GetBoxSlotAtIndex/SetBoxSlotAtIndex`,
  `save.Personal.GetFormEntry(...)`, `save.Inventory.Pouches`, `BlankPKM`, `Context`.
- **Blank save for a loose mon:** `BlankSaveFile.Get(GameVersion, trainerName, LanguageID)`;
  version via `entity.Context.GetSingleGameVersion()`. **CAUTION:** a freshly-built blank
  Gen3/4 save **cannot be serialized** (`SAV3.WriteSectors` throws) — the standalone
  `SaveEngineSession` ctor deliberately does not call `save.Write()`.
- `PKM` interface-gated fields: gate UI with `pk is ITeraType`, `IHyperTrain`, etc., and
  hide when absent. `ResetPartyStats()` already applies mints and Hyper Training.
- `GameInfo.GetStrings("en")` name tables; `GameInfo.GetLocationList(version, context, egg)`.
- ALM: `sav.GetLegalFromSet(set)`, `sav.Legalize(pk)`, `sav.GenerateLivingDex(sav.Personal)`.
- Events: `EncounterEvent.MGDB_G4..G9` embedded wondercards.
- Corpus for tests: `external/PKHeX/Tests/PKHeX.Core.Tests/` (Gen7 save + ~195 loose
  `.pk*` across every generation).

## Release procedure

1. Build clean (0 warnings), tests green.
2. `dotnet publish ... -c Release` → verify `unzip -l <apk> | grep -c sgdb.key` == 0.
3. Tag `vX.Y.Z` (csproj `ApplicationDisplayVersion` must match), push; CI attaches the APK
   and the Linux packages.
4. Verify the uploaded asset digest matches the local `shasum -a 256`.

## Roadmap

PKHeX's `PKM` has 60+ editable surfaces; PKForge exposes a growing subset. Next up:
"How do I get this?" encounter cards (`EncounterMovesetGenerator`), batch editor
(`EntityBatchEditor`), box ops (`BoxManipulator`), form editor.
Full tier list in the git history of this file; `docs/PRODUCT_MAP.md` tracks surfaces.

## Known gotchas

- **Gender on PID-based formats (Gen 3–5):** gender derives from the PID; `Gender = x`
  may not stick. Legalize resolves it.
- **AutoMod ImplicitUsings** must stay disabled in `src/PKForge.AutoMod`.
- **SkiaSharp 4** (the app and `PKForge.Chrome`): build paths with `SKPathBuilder`, pass
  `SKSamplingOptions` to `DrawBitmap`, and look glyphs up through `SKFont`; the older
  overloads are obsolete. Touch `Released` only fires if `Pressed` was `Handled`;
  `GetPixelSpan()` for pixel scans, never per-pixel `GetPixel`.
- **MAUI bootstrap:** `MainApplication : MauiApplication` is required or the app shows a
  blank screen. Landscape locked, immersive fullscreen.
- **Second screen:** the Thor's lower display is not presentation-category →
  `GetDisplays()[1]` fallback; page inflated via `ToPlatform(new MauiContext(...))`.
- **Gamepad:** `MainActivity.DispatchKeyEvent` (Android) and `KeyboardSource` /
  `SdlGamepadSource` (desktop) feed `PadInputPump` → `GamepadRouter` stack; pages implement
  `IPadHandler`, popups push themselves while open. `StatsPopup`/`TextPopup` need the
  touchscreen (numeric/text entry).
- **Desktop assets:** `MauiAsset` items become Avalonia resources through the
  `HookMauiAssetsToAvalonia` target in the csproj, so `OpenAppPackageFileAsync` reads the
  same logical names on both heads.
- **Desktop canvases** paint on Avalonia's render thread at the zoomed window's real pixel
  size (`ZoomedSKCanvasViewHandler`), and touch points are mapped back through
  `DesktopZoom`. Images evicted from the sprite caches are disposed only between paints
  (`PaintFence`), so never dispose a cached image directly.
- **SDL** init, quit and event pumping all run on the one thread `SdlRuntime` owns; go
  through it rather than calling `SDL_Init` elsewhere. SDL's video subsystem is never
  started: Avalonia owns the window.
