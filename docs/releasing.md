# Windows releases

GitHub (`ajisaacs/OpenNest`) is the primary repository: push branches and tags
there. Gitea (`git.thecozycat.net/aj/OpenNest`) is a read-only backup that
pulls from GitHub every hour; it refuses pushes.

## Build a candidate

1. Choose a committed source revision; never include uncommitted work implicitly.
2. Push a `release/vX.Y.Z` branch containing the release workflow to GitHub.
   The pushing credential needs **Contents: read/write**, plus
   **Workflows: read/write** to introduce or update `.github/workflows` files.
   Do not change credentials or broaden permissions without the owner's approval.
3. The `Windows release build` workflow uses a GitHub-hosted `windows-2022`
   runner. It builds the solution, runs all four test projects in Release and
   the main test project in Debug, then packages and smoke-tests the desktop app.
   Optional local/proprietary fixture and opt-in measurement tests may skip;
   inspect the uploaded TRX files rather than treating skips as passes.
4. Download the `OpenNest-X.Y.Z-win-x64` artifact and verify its `.sha256`.
   It contains `OpenNest.vX.Y.Z.win-x64.zip`, with the .NET runtime, native
   dependencies, shipped configurations, all three post-processors, license,
   and `build-info.json` identifying the exact source commit. The nesting engines
   are built into `OpenNest.Engine.dll`; no external engine repository or plug-in
   DLL is packaged. `scripts/ReleaseSmoke` loads the packaged engine assembly and
   checks that every built-in engine instantiates from it, that the renamed
   `Opus55NestingEngine` selection resolves to Irregular, and that an unknown
   engine name is rejected.

The workflow has read-only repository permissions and does **not** publish
releases. Once present on the default branch, it can also be dispatched manually
with an `X.Y.Z` version. Running/dispatching via a PAT needs suitable Actions
permissions; public run metadata alone does not prove dispatch access.

For a local Windows build with PowerShell 7 and the .NET 8 SDK:

```powershell
./scripts/Publish-Windows.ps1 -Version X.Y.Z
```

Run the test suites separately before local packaging. The script refuses an
existing output directory; use a fresh `-OutputDirectory` rather than deleting
previous packages. CI also exercises this refusal and verifies the ZIP is unchanged.

## Publish

Review release notes, breaking API changes, and known limitations with the owner.
After the candidate passes, integrate the release tooling into the chosen branch,
create an annotated `vX.Y.Z` tag on the exact release commit and push it to
GitHub. Use the successful **tag build's** artifacts
for the GitHub Release; do not substitute packages built from another commit.
Verify the public asset names, sizes, download/checksum, and release status after
upload. Do not overwrite an existing release/tag or asset silently.

The automated desktop smoke test proves the extracted app opens its main window
and discovers posts. It does not replace interactive CAD/nesting acceptance,
physical CNC/serial verification, GPU execution, or real-model ONNX accuracy.
Packages are unsigned; code signing and a fuller packaged-post execution test
remain follow-up hardening.
