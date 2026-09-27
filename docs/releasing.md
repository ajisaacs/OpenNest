# Windows releases

Git refs are owned by Gitea (`aj/OpenNest`) and push-mirrored to GitHub
(`ajisaacs/OpenNest`). Create release branches and tags on Gitea, not GitHub.

## Build a candidate

1. Choose a committed source revision; never include uncommitted work implicitly.
2. Push a `release/vX.Y.Z` branch containing the release workflow to Gitea and
   verify that the push mirror delivered the same commit to GitHub. The mirror's
   GitHub credential needs **Contents: read/write** and **Workflows: read/write**
   to introduce or update `.github/workflows` files. Do not change credentials
   or broaden permissions without the owner's approval.
3. The `Windows release build` workflow uses a GitHub-hosted `windows-2022`
   runner. It builds the solution, runs all four test projects in Release and
   the main test project in Debug, then packages and smoke-tests the desktop app.
   Optional local/proprietary fixture and opt-in measurement tests may skip;
   inspect the uploaded TRX files rather than treating skips as passes.
4. Download the `OpenNest-X.Y.Z-win-x64` artifact and verify its `.sha256`.
   It contains `OpenNest.vX.Y.Z.win-x64.zip`, with the .NET runtime, native
   dependencies, shipped configurations, all three post-processors, license,
   and `build-info.json` identifying the exact source commit. Gpt6Astra, Opus55,
   and Qwen38FlashNext are bundled in `Engines/` with their MIT license and source
   manifest. `scripts/external-engines.json` pins the external repository revision;
   no moving branch or prebuilt third-party DLL is used. The script tests and
   builds each engine against this host, then exercises actual packaged registry
   discovery and an intentionally missing-DLL failure case. Keep the explicit
   engine allowlist; never package the template, shared test kit, or test DLLs.

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
create an annotated `vX.Y.Z` tag on the exact release commit, push to Gitea, and
verify the same tag/commit on GitHub. Use the successful **tag build's** artifacts
for the GitHub Release; do not substitute packages built from another commit.
Verify the public asset names, sizes, download/checksum, and release status after
upload. Do not overwrite an existing release/tag or asset silently.

The automated desktop smoke test proves the extracted app opens its main window
and discovers posts. It does not replace interactive CAD/nesting acceptance,
physical CNC/serial verification, GPU execution, or real-model ONNX accuracy.
Packages are unsigned; code signing and a fuller packaged-post execution test
remain follow-up hardening.
