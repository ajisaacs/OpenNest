# Desktop help

User guides live in [ajisaacs/opennest-help](https://github.com/ajisaacs/opennest-help):
Markdown pages plus annotated screenshots, built with MkDocs into offline HTML. This repo
holds only the code that shows that HTML and the pin of which help release ships.

## In the app

- **Help > OpenNest Help** (F1 in the main window) opens the home topic;
  **Help > Your First Nest** opens the tutorial.
- A dialog opts in to context help with `HelpTopics.Attach(this, HelpTopics.<Topic>)`;
  F1 anywhere in it opens that topic. Auto Nest and the CAD Converter do this today.
- `HelpForm` is a single WebView2 window on its own STA thread, so modal dialogs never
  disable it. It serves the help folder from the private virtual host
  `https://help.opennest.local/`; off-site links open in the default browser. WebView2
  data goes to `%LOCALAPPDATA%\OpenNest\WebView2`.
- The help folder is `Help\` beside `OpenNest.exe`. Set `OPENNEST_HELP_DIR` to a local
  opennest-help `site\` build to preview unreleased pages. Without either, the menu
  explains that help is not installed.
- A Windows `dotnet build` of `OpenNest.csproj` downloads the pinned release once into
  `obj\help\` (again when `scripts/help.json` changes) and copies it to the build output's
  `Help\`. A failed download only warns. CI (`CI=true`) skips it; pass
  `-p:InstallHelp=false` to skip it offline. It never reaches `dotnet publish` output.

## Topic IDs

`HelpTopics` constants are IDs, not file names. The help's `topics.json` maps each ID
to a page or `page.html#anchor`, and its build fails if a mapped page is missing. An
unknown ID falls back to the home page. Add the ID to `topics.json` (in the help repo)
before using a new constant here, and never rename an existing ID.

## Packaging

`scripts/help.json` pins one opennest-help release (tag and SHA-256 of its
`opennest-help.zip`). `scripts/Install-Help.ps1` downloads it, refuses a hash mismatch
and expands it into the package's `Help\` folder; both `Publish-Windows.ps1` and
`Deploy-OpenNest.ps1` call it, and `build-info.json` records the help version. Pass
`-HelpDirectory <site folder>` to either script to package a local build instead.

To ship new help: tag the help repo `vX.Y.Z`, wait for its release, then update `tag`
and `sha256` here (the release attaches `opennest-help.zip.sha256`).

When a UI change alters a documented screen or workflow, update or recapture the
affected opennest-help pages (its README covers capture and markup).
