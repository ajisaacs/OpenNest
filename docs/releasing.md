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
   runner. It builds the solution, runs all six test projects in Release and
   the main test project in Debug, then packages and smoke-tests the desktop app.
   A test that is silent for five minutes is killed and named in the log, with a
   mini dump in the uploaded `windows-test-results` artifact.
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

## Server image (separate from Windows candidates)

`Server image` validates relevant PRs and `master` pushes without registry login,
package-write permissions, or registry upload. It builds/tests with .NET 8, records pinned
SDK/runtime base digests, builds a single-platform `linux/amd64` image without cache
or attestations (`--provenance=false --sbom=false`), and runs the real-client
container persistence/backup/restore smoke. A Windows `v*` tag build alone never
publishes a server image.

Publication requires owner-approved release intent: a **published GitHub Release**
or an explicit `Server image` dispatch **from `master`**, naming an existing strict
`vX.Y.Z` tag (no prerelease, leading zero, or extra suffix). The tag must resolve to
a full commit already on `master`. For a published Release, that peeled commit
must also equal the event's full `GITHUB_SHA`; a retargeted tag is refused. Manual
dispatch intentionally resolves the approved existing tag, not the workflow's
`master` SHA. The job checks out that exact source, reruns all
server/image gates, and pushes the *same smoked image* to
`ghcr.io/ajisaacs/opennest-server:X.Y.Z` and `:sha-<full-commit>` only. Both tags must
be absent; retries after even a partial upload stop instead of overwriting. There
are no `latest`, major, or minor aliases. Do not dispatch just to test publication.

Publication requires an **existing owner-verifiable private package**, linked to
`ajisaacs/OpenNest`, with repository Actions access (normally inherited from the
link). The job uses only its scoped `GITHUB_TOKEN`. All package metadata 404s are
refused: GitHub can mask an inaccessible private package as `Not Found`. Opaque
registry token success, an empty tag list, or a registry 404 proves neither package
absence nor privacy and cannot override the metadata check. Explicit reduced
scope and failed registry read access also stop the job. The job rechecks private
association after upload.

First-package initialization is a separately owner-authorized prerequisite
outside this workflow. Until the private package, repository link, and Actions
access can be verified, publication remains blocked while read-only validation
can pass. There
is no automatic bootstrap, extra PAT, absence assertion, or bypass setting. A
local credential's Packages API 403 proves neither absence nor privacy. No
workflow changes visibility. Public availability needs separate owner approval
and a later anonymous-pull check; it is not approved here.

The local gate reads `docker image save` to hash the actual config and verify each
layer against its ordered uncompressed rootfs digest. It rejects unexpected
indexes/attestations. Smoke and tagging use the inspected immutable Docker ID,
not a mutable local tag, and readback must match that pre-smoke identity. Success
requires exact readback of both manifest digests, `linux/amd64`, OCI labels, config
bytes, and the full layer/rootfs chain identifying that same smoked image.
A **separate clean job** pulls the returned manifest digest, independently checks
its saved config/layers, and repeats the real-client smoke using tools checked out
from that source commit. Only its pass verifies the image.

Artifacts retain TRX, explicit logs, and safe provenance. Docker's store-dependent
`Id`, the config blob digest, and the registry manifest digest are separate values:
classic Docker may use the config digest as `Id`, while containerd may use a
manifest or index digest. Disabling attestations does not make `Id` a config
digest. Temporary image-save archives are deleted, never uploaded. Artifacts
never include smoke state JSON, databases, nest archives, Docker auth configs, or
credentials.
A failed publication/verification is not a release acceptance; inspect its logs
and any partial tags with the owner before choosing a new approved version.

For a local read-only check of the release guards:

```sh
python3 -m unittest discover -s scripts -p test_server_image_release.py -v
```

Deployment is deliberately separate; use the verified digest and the
[private pull/Compose procedure](nest-storage.md#deploying-with-compose). No
publication, release creation, deployment, or visibility change is implied by
adding or validating this workflow.
