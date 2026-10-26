# Vendored code

`src/Stalker.Gamma/` and `src/LibCurlImpersonate/` are vendored from
[FaithBeam/stalker-gamma-cli](https://github.com/FaithBeam/stalker-gamma-cli) (GPL-3.0).

- Source commit: `3854a9319e88e5419664307415a32854cc98782b` (upstream release 1.40.0, 2026-10-25)
- Vendored on: 2026-10-26 (previously `3869580b33c6928578c7d512e620f3f54969b085`, 2026-05-22)
- Original namespaces and project names are kept so future diffs against upstream stay readable.

Local modifications are listed here as they happen:

| File | Change | Reason |
|------|--------|--------|
| (upstream `Directory.Build.props` not copied) | Replaced by this repo's root `Directory.Build.props` (net10.0, Nullable, ImplicitUsings; no `PublishAot`) | GUI app is published self-contained JIT, not AOT |
| `Utilities/DirUtils.cs` | `NormalizePermissions` skips directory symlinks (reparse points) | Security: a malicious mod archive could plant a symlink and chmod files outside the install tree |
| `GammaInstallerServices/GammaProgress.cs` | `Reset()` is public | The GUI is one long-lived process and re-baselines progress between operations; the CLI is one process per command |
| `OpenApi/GithubClient/kiota-lock.json` | `descriptionLocation` scrubbed to `api.github.com.yaml` | Upstream's lock file contains the author's local filesystem path |
| `Services/GitService.cs` | `ExtractBlobs` rejects tree entries that resolve outside the output directory | Security: blobs are written straight from the object database, bypassing libgit2's checkout path validation |
| `Utilities/RunProcessUtility.cs` | Build the child arg list with `ProcessStartInfo.ArgumentList` instead of hand-quoting into `Arguments` | Security: archive/destination paths containing quotes were injectable into 7zz/unzip/tar |

Only `src/Stalker.Gamma/` and `src/LibCurlImpersonate/` are vendored; the CLI project itself is not.
The GUI re-implements the CLI's command logic (`FullInstallCmd`, `UpdateCmds`, `ResumeUtility`,
`SetupUtilitiesService`, `UtilitiesReady`) on top of the engine, so after a re-sync check those files
in upstream's `stalker-gamma-cli/` for behavioural changes too. `scripts/setup-deps.sh` pins the same
curl-impersonate / 7-Zip versions as upstream's `build/linux/Makefile`.

To re-sync with upstream: clone upstream, diff `Stalker.Gamma/` and `LibCurlImpersonate/`
against `src/`, review, and update the commit hash above.
