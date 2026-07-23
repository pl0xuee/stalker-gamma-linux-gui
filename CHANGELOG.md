# Changelog

All notable changes to this project are documented here. The release workflow publishes the
matching version's section as the GitHub release notes, so keep the headings as `## vX.Y.Z`.

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
