# Desktop auto-update

The Windows desktop app (`OpenNest.exe`) can self-update via
[Velopack](https://velopack.io): `OpenNest.UpdaterService` polls its update
source every 30 minutes, downloads a newer release in the background, and
only restarts to apply it after the user confirms (a clickable status-bar
item appears once a download finishes). It never force-restarts; an open,
unsaved nest is never discarded automatically.

## Where updates come from

By default an installed copy checks the public
[GitHub Releases](https://github.com/ajisaacs/OpenNest/releases) for full,
non-pre-release versions (`GithubSource`, unauthenticated: GitHub allows 60 API
requests per hour per IP address, ample for a 30-minute poll but shared by every
machine behind one address). Pre-releases are offered only as the development
build below.

To use a self-hosted feed instead, set **Tools > Options > Auto-update feed URL**
(`OpenNest.Properties.Settings.UpdateFeedUrl`, saved for the current Windows
user) to the feed's base URL (`http://host:port`) and restart. A self-hosted
feed URL is operator infrastructure and never belongs in source control; clearing
the field returns to GitHub Releases.

Velopack only applies updates to a Velopack installation (`UpdateManager.IsInstalled`):
a copy installed with `OpenNest-win-Setup.exe` or extracted from
`OpenNest-win-Portable.zip`. The plain `OpenNest.vX.Y.Z.win-x64.zip` and ordinary
build output contain no `Update.exe` and never auto-update; moving from one of
them to an updating copy is a one-time manual install.

## Release packages

`scripts/Publish-Windows.ps1` packs the same smoke-tested folder as the plain ZIP
with the `vpk` CLI pinned in `.config/dotnet-tools.json` (keep it equal to the
`Velopack` package version), into `velopack/`: `OpenNest-win-Setup.exe`,
`OpenNest-win-Portable.zip`, `OpenNest-X.Y.Z-full.nupkg`, `releases.win.json`
and `RELEASES`, with their hashes in `OpenNest.vX.Y.Z.velopack.sha256`. Installed
copies find an update through the `releases.win.json` and full package attached to
the newest full GitHub Release, so all five must be uploaded with it (see
[releasing](releasing.md#publish)). Packages are full only; delta packages would
need the previous release's packages beside the output before `vpk pack`.

## Development builds

`.github/workflows/dev-build.yml` runs after every master push whose
`Cross-platform tests` pass (that workflow includes the Windows desktop tests).
It packages that commit with `Publish-Windows.ps1` as `X.Y.Z-dev.N`, where
`X.Y.Z` is the minor after the newest `vX.Y.Z` tag and `N` the commit count, so
each build outranks the last and the next full release outranks them all. It then
replaces the single rolling `dev-build` pre-release (tag `dev-build`, never marked
Latest) with the same eight assets as a release. A run whose commit is no longer
master's head publishes nothing; the newer run does.

Installed copies take it only with **Tools > Options > Include development
builds** (`UpdateIncludeDevBuilds`, restart to apply); everyone else sees full
releases only. Velopack reads the ten newest GitHub releases, so keep a single
rolling pre-release: a pile of them could push the newest full release out of
that window. Turning the option off leaves a copy on its development build until
a higher full release appears. The workflow's publish job is the only automation
with release write access; it is limited to this pre-release.

## Hosting a feed

Any static file server that can serve a `vpk pack` output directory works.
`scripts/Deploy-OpenNest.ps1` builds a self-contained `win-x64` publish,
packs it with the pinned `vpk` CLI, and `scp`s the
result to a directory you provide:

```powershell
./scripts/Deploy-OpenNest.ps1 -FeedHost <host> -FeedUser <user> -RemoteDataDir <path>
```

Bump `<Version>` in `OpenNest/OpenNest.csproj` before each run. This is
independent of [the GitHub release process](releasing.md) and of
`Publish-Windows.ps1` — the latter always overrides `<Version>` via
`-p:Version`, so the two don't conflict. Machines on a self-hosted feed only see
versions pushed to that feed.
