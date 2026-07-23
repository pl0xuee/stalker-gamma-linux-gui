# Vendored code

`src/Stalker.Gamma/` and `src/LibCurlImpersonate/` are vendored from
[FaithBeam/stalker-gamma-cli](https://github.com/FaithBeam/stalker-gamma-cli) (GPL-3.0).

- Source commit: `3869580b33c6928578c7d512e620f3f54969b085` (2026-05-22)
- Vendored on: 2026-07-22
- Original namespaces and project names are kept so future diffs against upstream stay readable.

Local modifications are listed here as they happen:

| File | Change | Reason |
|------|--------|--------|
| (upstream `Directory.Build.props` not copied) | Replaced by this repo's root `Directory.Build.props` (net10.0, Nullable, ImplicitUsings; no `PublishAot`) | GUI app is published self-contained JIT, not AOT |
| `Utilities/DirUtils.cs` | `NormalizePermissions` skips directory symlinks (reparse points) | Security: a malicious mod archive could plant a symlink and chmod files outside the install tree |
| `Utilities/RunProcessUtility.cs` | Build the child arg list with `ProcessStartInfo.ArgumentList` instead of hand-quoting into `Arguments` | Security: archive/destination paths containing quotes were injectable into 7zz/unzip/tar |

To re-sync with upstream: clone upstream, diff `Stalker.Gamma/` and `LibCurlImpersonate/`
against `src/`, review, and update the commit hash above.
