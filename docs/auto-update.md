# Desktop auto-update

The Windows desktop app (`OpenNest.exe`) can self-update via
[Velopack](https://velopack.io): `OpenNest.UpdaterService` polls a static
file feed every 30 minutes, downloads a newer release in the background, and
only restarts to apply it after the user confirms (a clickable status-bar
item appears once a download finishes). It never force-restarts; an open,
unsaved nest is never discarded automatically.

## Configuring the feed

The feed URL is a per-machine setting
(`OpenNest.Properties.Settings.UpdateFeedUrl`), set from **File > Options >
Auto-update feed URL**. It is blank by default, which disables update checks
entirely — this is intentional: the feed URL is operator infrastructure, not
something that belongs in source control, so no default ships in the repo.
Enter a feed's base URL (`http://host:port`) and restart the app to pick it
up.

Velopack only applies updates to an installed copy (`UpdateManager.IsInstalled`);
running from a build output directory never attempts an update.

## Hosting a feed

Any static file server that can serve a `vpk pack` output directory works.
`scripts/Deploy-OpenNest.ps1` builds a self-contained `win-x64` publish,
packs it with the `vpk` CLI (`dotnet tool install -g vpk`), and `scp`s the
result to a directory you provide:

```powershell
./scripts/Deploy-OpenNest.ps1 -FeedHost <host> -FeedUser <user> -RemoteDataDir <path>
```

Bump `<Version>` in `OpenNest/OpenNest.csproj` before each run. This is
independent of [the GitHub release process](releasing.md) and of
`Publish-Windows.ps1` — the latter always overrides `<Version>` via
`-p:Version`, so the two don't conflict.
