# Stalker GAMMA Linux GUI — Design

Date: 2026-07-22 · Status: approved

## Goal

An all-in-one Linux (Arch/CachyOS) desktop app to download, install, update, and play
STALKER G.A.M.M.A.: full installer feature parity with FaithBeam/stalker-gamma-cli, plus
Steam integration (non-Steam shortcut, Proton selection, protontricks prerequisites),
shipped as a single self-contained AppImage.

## Decisions

| Topic | Decision |
|-------|----------|
| Scope | Full CLI feature parity + Steam integration |
| Stack | C# / Avalonia 11, CommunityToolkit.Mvvm, System.Reactive for progress throttling |
| Engine | Vendored `Stalker.Gamma` + `LibCurlImpersonate` sources (GPL-3.0) from upstream commit `3869580` — full ownership, no upstream dependency (see `VENDORED.md`) |
| Config | Shares the CLI's `~/.config/stalker-gamma/settings.json` format, staying CLI-interchangeable |
| Distribution | AppImage only (no AUR); external binaries (7zz, libcurl-impersonate.so, cacert.pem) bundled inside |
| Steam integration | Jackify-style: binary-VDF shortcut in `userdata/<id>/config/shortcuts.vdf` (random negative appid), CompatToolMapping raw-text edit in `config/config.vdf`, prefix via `proton run wineboot -u` + `STEAM_COMPAT_DATA_PATH`, prerequisites via `protontricks --no-bwrap <appid> -q d3dcompiler_47 d3dx10 d3dx11_43 d3dx9 dx8vb quartz vcrun2022` + `win10` |
| Proton choice | Newest installed proton-cachyos* → newest GE-Proton* → proton_experimental; user-overridable |
| Out of scope | AUR, Flatpak Steam/protontricks, Windows/macOS, cloudscraper python server |

## Architecture

Single Avalonia window: sidebar (Install · Updates · Mods · MO2 Profiles · Settings ·
Steam Setup), content region, status bar with overall progress + Cancel, collapsible log pane.

- Engine consumed via its DI entry point `RegisterCoreGammaServices()`; each operation runs
  in a fresh DI scope, one at a time (`OperationRunner`), because `StalkerGammaSettings` and
  `GammaProgress` are process-wide singletons.
- Progress: subscribe `IGammaProgress.ProgressChanged` / `DebugProgressChanged`, throttle with
  Rx `.Sample(100ms)`, marshal to `Dispatcher.UIThread`.
- Cancellation: `CancellationTokenSource` per operation, honored down to native curl.
- Errors: engine throws typed exceptions; surfaced in status bar + error dialog with detail.
- CLI-only logic vendored into the GUI: settings/profiles, MO2 modlist/profile utilities,
  check-anomaly verification, binary path fixup, utilities-ready probe.
- Steam services (all GUI-side, new code): SteamLocator, CompatToolCatalog,
  ShortcutsVdfService, ConfigVdfService, SteamProcessService, ProtonPrefixService,
  ProtontricksService.

The full implementation plan (API signatures, flow order, verification) lives in the
project plan file and mirrors this design.
