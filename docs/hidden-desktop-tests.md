# Hidden-desktop test runs

WinForms tests create real windows, which appear on the screen and take focus
from whatever you are doing. `scripts/Test-Hidden.ps1` runs `dotnet test` on a
separate Win32 desktop instead, so nothing appears and focus stays put.

```powershell
# OpenNest.WinForms.Tests
.\scripts\Test-Hidden.ps1

# Any dotnet test arguments, passed through unchanged
.\scripts\Test-Hidden.ps1 OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj --no-build --filter FullyQualifiedName~DrawingLibrary

# Allow longer than the default 30 minutes
.\scripts\Test-Hidden.ps1 -HiddenTimeoutMinutes 60
```

Output streams back as usual and the exit code is that of `dotnet test`, or 124
after a timeout. It needs no feature, setting or administrator rights.

## How it behaves

- The desktop is created in the current window station, so forms get real
  window handles, message loops, layout and `DrawToBitmap`. Focus also works
  within the desktop: the focus-traversal tests pass unchanged.
- Nobody can see or dismiss a window there. A test stuck on a modal dialog stops
  at the timeout, and the error says so.
- The run lives in a job object, so stopping the script or reaching the timeout
  ends the whole process tree. MSBuild node reuse and the shared compiler are
  turned off for the run, so a build inside it can be slower; use `--no-build`
  after building normally.
- Whatever a test hands to the shell, such as opening a file through
  `UseShellExecute`, can be started by Explorer and appear on your desktop. The
  WinForms suite does not do this.

## Alternatives

- **Windows Sandbox**: isolates more, but a user must enable the optional
  feature, and each sandbox starts empty. A `.wsb` file would have to map the
  repository and an SDK folder and then start from cold every run.
- **CI or a remote runner**: suits full verification and needs no local desktop,
  but a push-and-wait cycle is too slow for iterating on a dialog, and a runner
  must have an interactive session for its windows to behave.
