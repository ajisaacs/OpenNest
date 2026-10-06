# Automated Windows acceptance

The `windows-desktop` job in `.github/workflows/ci.yml` runs on GitHub-hosted
`windows-2022` for every pull request and `master` push. It executes the full
WinForms and FrontEnd test projects in Release, including shown-form STA tests
and Windows child-process lifetime tests. No production service, customer
fixture, physical GPU, or CNC controller is required.

Each test host has a five-minute hang watchdog with a mini dump; the job has a
20-minute limit. A failure in either suite fails the step, without suppressing
the other suite. This job is separate from the existing Linux `tests` aggregate;
require `windows-desktop` as well when assessing acceptance.

## Results and coverage contract

Download the `windows-desktop-test-results` artifact from the workflow run.
It contains both TRX files, any hang diagnostics, and:

- `windows-acceptance.json`: commit/tree, runner platform, workflow identifiers,
  TRX hashes, enumerated results, failures/skips and per-area test evidence.
- `windows-acceptance.md`: a readable summary, also shown in the Actions job
  summary, with remaining acceptance beside the automated result.

Artifacts are retained for 14 days. Save evidence needed for longer-lived task
verification before expiration. On pull requests the checked-out commit can be
GitHub's test merge commit; use the report's commit/tree and workflow link, not
just the branch name.

`scripts/windows-acceptance.json` is the committed coverage contract. It names
required test methods and the minimum number of executed cases for each
(including theory rows). The report fails if either TRX is absent/malformed,
empty or inconsistent; if a required method/case is missing, failed or skipped;
if any other test fails; or if a run was aborted. Skips outside the named contract
are retained explicitly, never counted as passes. Added cases are allowed;
removing or renaming required coverage requires an intentional manifest change.
The whole suites still run, not just this manifest's subset.

The areas are BOM import, Nest Info material preservation, saved-nest browsing,
nesting/commit policy, PDF export, the cutting dialog, automatic cutoffs,
overlap/posting consent, UI lifetime, import/export failures, and MCP processes.
The manifest is public test coverage only. Private tracker IDs, plans, customer
data, and acceptance notes stay out of the repository and CI artifacts.

The report is evidence, not automatic task closure. A passing area does not
establish absent implementation, real-service/two-PC behavior, DPI or physical
printing quality, packaged-build behavior, or machine safety. In particular,
fake-repository browser tests do not verify the HTTP/SQLite save/paging lifecycle.
Keep those remaining gates in the tracker; link exact test names and the workflow
run when updating an individual task.

## Repeat locally on Windows

Use a clean, committed checkout, .NET 8 and Python 3. Run from the repository root
in PowerShell. Always use a new results directory so stale TRX files cannot stand
in for a failed build. Do not reuse downloaded CI results as a new local run.

```powershell
$results = Join-Path 'TestResults' ([guid]::NewGuid().ToString())
$failed = @()
foreach ($project in @('OpenNest.WinForms.Tests', 'OpenNest.FrontEnd.Tests')) {
    dotnet test "$project/$project.csproj" -c Release --blame-hang-timeout 5m --blame-hang-dump-type mini --logger "trx;LogFileName=$project.trx" --results-directory $results
    if ($LASTEXITCODE -ne 0) { $failed += $project }
}
python scripts/windows_acceptance.py --results $results
if ($LASTEXITCODE -ne 0 -or $failed.Count -gt 0) { throw 'Windows acceptance failed; inspect results.' }
```

The CLI refuses Linux/macOS as Windows runtime evidence and refuses tracked
source edits. The source revision is provenance of the checkout running the
command, not a cryptographic attestation of arbitrary supplied TRX files; the
fresh-checkout CI run and the fresh-directory local procedure establish that
relationship. The JSON retains hashes so the saved files can be matched later.

## Maintain the gate

Run `python scripts/test_windows_acceptance.py -v` on any platform after changing
the parser or manifest. Its generated TRX fixtures test the reporter only, not
the application. Exercise a draft PR on Windows before integrating new form
cases. Inspect actual TRX failures and skip reasons; a Linux cross-build is not
a Windows pass. Keep visual/operator limitations alongside the named cases.
