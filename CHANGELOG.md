# Changelog

All notable changes to this project are documented here. The release workflow publishes the
matching version's section as the GitHub release notes, so keep the headings as `## vX.Y.Z`.

## v0.1.9

v0.1.8 was tagged but its release build failed on a CI checksum pin (not an app problem), so it was
never published; everything below ships here.

### Added
- **Engine updated to stalker-gamma-cli 1.40.0** (from 1.3x). What that brings:
  - **Much faster updates.** "Apply updates" only re-extracts the files that changed in the four
    GAMMA git repos instead of expanding all of them every time.
  - **Faster installs and reinstalls**: git repo extraction is multi-threaded.
  - **Resume a failed install or update.** A failed run leaves `resume.txt` in the GAMMA folder;
    the Install and Updates pages show an "Unfinished install/update found" banner with
    **Resume** (skips everything that already completed) and **Discard**.
  - **Correct progress for downloads with no known size.** Addons that used to report absurd
    percentages like 1,000,000% now show the megabytes downloaded, with an indeterminate bar.
  - **Anomaly mirror (experimental).** Tick "Anomaly mirror" on the Install page to fetch a
    repacked, faster-to-extract Anomaly archive first, falling back to ModDB.
  - **Cloudflare workaround (experimental).** Settings → "Cloudflare workaround" takes the address
    of an optional `stalker-gamma-cli-server` (needs Docker) for when ModDB keeps challenging you.
    This replaces the old bundled-Python approach, which no longer exists upstream.
  - Clearer network errors (a hint about DNS, firewall, TLS or QUIC instead of a bare curl code).
- Bundled curl-impersonate is now v2.2.3 and 7-Zip 26.03, matching upstream.

### Changed
- Settings no longer has a "Python API URL" field (the engine no longer uses it; the value is left
  in `settings.json` untouched so the CLI still reads the same file). The unused
  `ExperimentalModDbSettings` block is dropped from `settings.json` the next time you save.
- Nav icons are drawn vector icons that line up and scale, instead of mismatched text glyphs.
- Lists (MO2 profiles, Settings profile list) use the app's dark palette instead of Fluent's grey.
- Steam page is laid out as Requirements / Setup / Steps cards, and runs the requirement check
  automatically when you open it instead of showing an empty Proton picker.
- Empty tables say what they are for instead of showing a blank box.
- The window can be narrower (minimum 640 px) so it tiles cleanly in a tiling window manager.

### Fixed
- Switching the MO2 profile no longer corrupts `ModOrganizer.ini` when a profile name contains
  `$` (it was read as a regex substitution), and it now says so instead of reporting success when
  `ModOrganizer.ini` has no `selected_profile` line.
- "Verify Anomaly" shows progress while it hashes instead of sitting on "Checking…", and a
  truncated `checksums.md5` no longer aborts the whole check.
- Whether curl is required is decided after the active profile's settings are applied, so using the
  Cloudflare workaround server no longer demands libcurl.
- Git extraction rejects tree entries that would write outside the target folder.
- Steam setup hardening:
  - Steam is detected as running even when only the main client is up (updating, or its web
    helper crashed), and setup stops if it will not exit, instead of writing shortcuts that the
    dying client then overwrites.
  - If writing the shortcut fails after Steam was shut down, Steam is started again.
  - A stalled protontricks run is cleaned up and retried like a failed one, and a leftover wine
    process holding the output pipe can no longer hang the step.
  - Component verification matches whole names (`d3dx9` no longer passes on `d3dx9_43`).
  - Launch options single-quote the mount list, so `$`, quotes or backticks in a path stay literal.
  - Existing library artwork for the shortcut is no longer overwritten on a re-run.

## v0.1.7

### Added
- **Ultrawide UI fix.** One button on the Mods page un-stretches the in-game HUD: it switches on
  the widescreen fixes GAMMA already downloads but ships disabled — Dynamic Aspect Ratio Tweaks
  (HUD, 3D PDA, minimap, map markers) plus the 2D scope reticle patch matching your monitor.
  Pick 21:9, 32:9 or 16:10; undo it any time by unticking those mods.
- **Browse buttons on every directory field.** Anomaly, GAMMA and Cache get a native folder
  picker plus an Open button that reveals the folder in your file manager, and the offline
  install's two list files get file pickers.
- **Mod list filter** with "Enable shown" / "Disable shown" for bulk toggling — usable on a
  700-entry list.
- Duplicate an MO2 profile from the MO2 Profiles page.
- The log pane gained Copy, Clear and "Open log file", and now follows new output as it arrives.
- Settings links straight to the config folder and the log file, and flags unsaved edits.

### Fixed
- **The mod list no longer loses MO2's header comment** every time you tick a checkbox. The file
  now round-trips exactly, apart from the entries you actually changed.
- Renaming a profile or editing its paths updates the profile list and the Install page's target
  paths immediately, instead of showing stale values until a restart.
- The Settings page now shows a profile created by a one-click install instead of an empty list.
- Action buttons grey out while an operation is running, rather than failing with a confusing
  "Another operation is running".
- "Run Steam setup" stays disabled until the requirement check passes and the restart is
  acknowledged, and the page says what to press first instead of sitting blank.
- Settings warns about relative paths, which would otherwise scatter a 40 GB install into
  whatever directory the app happened to start from.
- MO2 Profiles keeps your selection after "Set as selected" or "Delete".
- The log file rotates at 8 MB instead of growing without bound across installs.
- The window has a minimum size and button rows wrap instead of clipping when it is narrow.

## v0.1.6

### Fixed
- Steam shortcut setup no longer overwrites your own customizations (play time, custom
  artwork, collection tags) when re-run — only the fields it manages are updated.
- The mod list preserves any non-standard MO2 entries verbatim and in order instead of
  rewriting them.
- Clicking an install button while one is already running no longer wipes the progress table.

### Security
- Build-time downloads (appimagetool, 7-Zip, libcurl-impersonate) are checksum-verified, and
  the CI workflow pins its GitHub Actions to exact commits.
- Archive extraction no longer descends into symlinked directories anywhere in the pipeline.

## v0.1.5

### Security
- **Verified self-updates.** The updater checks the downloaded AppImage against GitHub's
  published SHA-256 and refuses to install a mismatch — no more executing an unverified binary.
- **Safer mod extraction.** Archive permission-normalization no longer follows symlinks, so a
  malicious modpack can't touch files outside the install directory. External tools are invoked
  with properly escaped arguments.
- **Least-privilege CI.** The build workflow's token is read-only except on the release job.

### Fixed
- **Crash-safe writes.** Steam's config.vdf/shortcuts.vdf and settings.json are written
  atomically, so a power loss can't corrupt them and silently drop your Steam shortcuts. A
  truncated shortcuts file is detected instead of quietly rewritten.
- **The app no longer crashes** when a file operation fails (e.g. deleting a mod whose files are
  open in the running game) — it logs the error and carries on.
- **protontricks cleanup** targets the correct Wine prefix (no longer risks killing your other
  Wine apps) and can't hang on a lingering process.
- Fixes for Proton version detection, mount-path matching, and a leaked network client.

## v0.1.4

### Improved
- Operation activity (protontricks components, prefix creation, every setup step) shows live in
  the status bar — no need to open the log pane to see what's happening.

## v0.1.3

### Improved
- Live protontricks feedback: prerequisite installs stream winetricks progress into the log
  pane instead of going silent for minutes.
- Cancelling an operation now reports "cancelled" instead of a misleading FAILED error.

## v0.1.2

### Added
- One-click in-app updates: "Update & restart" downloads the new AppImage, swaps it in place,
  and relaunches.

### Fixed
- Duplicate taskbar icon on KDE: the desktop entry declares `StartupWMClass`, so a pinned
  launcher and the running window group into a single icon.

## v0.1.1

### Fixed
- Steam setup no longer fails at "Restart Steam": the app waits for Steam's full teardown before
  relaunching.

### Added
- Steam library artwork for the GAMMA shortcut: icon, capsules, hero banner, and logo.

## v0.1.0

First release — an all-in-one Linux app to install, update, and play S.T.A.L.K.E.R. G.A.M.M.A.

- One-click install: profile bootstrap, full ~40 GB install, and Steam setup in a single run.
- Steam integration: non-Steam shortcut, newest-Proton auto-select, prefix creation, and
  protontricks prerequisites.
- Self-contained AppImage built by GitHub Actions, with an in-app version check.
