<#
.SYNOPSIS
    Runs dotnet test on a hidden Win32 desktop so test windows cannot appear
    on, or take focus from, the interactive desktop.

.DESCRIPTION
    Every argument except -HiddenTimeoutMinutes goes to dotnet test unchanged.
    With none, runs OpenNest.WinForms.Tests. Output streams back here and the
    script exits with the dotnet test exit code, or 124 after a timeout.
    See docs/hidden-desktop-tests.md.

.EXAMPLE
    .\scripts\Test-Hidden.ps1

.EXAMPLE
    .\scripts\Test-Hidden.ps1 OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj --no-build --filter FullyQualifiedName~DrawingLibrary
#>
# No param block: it would bind the first dotnet argument by position, and
# dotnet's short switches (-v, -e) by prefix. Everything is read from $args.

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class HiddenDesktopRun : IDisposable
{
    private const uint GenericAll = 0x10000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 1;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint WaitObject0 = 0;

    private IntPtr desktop;
    private IntPtr job;
    private IntPtr process;

    public HiddenDesktopRun(string commandLine, string workingDirectory, string logPath)
    {
        var desktopName = "OpenNestTests-" + Guid.NewGuid().ToString("N");
        try
        {
            desktop = CreateDesktopW(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
                throw new Win32Exception();

            // Closing the job ends the whole tree, however the script stops.
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                throw new Win32Exception();
            var limits = new JobExtendedLimits();
            limits.Basic.LimitFlags = JobObjectLimitKillOnJobClose;
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf(limits)))
                throw new Win32Exception();

            using (var log = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var logHandle = log.SafeFileHandle.DangerousGetHandle();
                if (!SetHandleInformation(logHandle, HandleFlagInherit, HandleFlagInherit))
                    throw new Win32Exception();

                var startup = new StartupInfo
                {
                    Size = Marshal.SizeOf(typeof(StartupInfo)),
                    Desktop = @"WinSta0\" + desktopName,
                    Flags = StartfUseStdHandles,
                    StdOutput = logHandle,
                    StdError = logHandle,
                };
                ProcessInformation info;
                if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, true,
                        CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment, IntPtr.Zero, workingDirectory,
                        ref startup, out info))
                    throw new Win32Exception();

                process = info.Process;
                try
                {
                    if (!AssignProcessToJobObject(job, process))
                        throw new Win32Exception();
                    if (ResumeThread(info.Thread) == uint.MaxValue)
                        throw new Win32Exception();
                }
                finally
                {
                    CloseHandle(info.Thread);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool WaitForExit(int milliseconds)
    {
        return WaitForSingleObject(process, (uint)milliseconds) == WaitObject0;
    }

    public int ExitCode
    {
        get
        {
            int code;
            if (!GetExitCodeProcess(process, out code))
                throw new Win32Exception();
            return code;
        }
    }

    public void Dispose()
    {
        if (job != IntPtr.Zero)
            CloseHandle(job);
        if (process != IntPtr.Zero)
            CloseHandle(process);
        if (desktop != IntPtr.Zero)
            CloseDesktop(desktop);
        job = process = desktop = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimits
    {
        public JobBasicLimits Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobExtendedLimits info, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
'@

function ConvertTo-CommandLineArgument([string]$Value) {
    if ($Value -and $Value -notmatch '[\s"]') {
        return $Value
    }

    # CommandLineToArgvW: backslashes double only when a quote follows them.
    $escaped = $Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1'
    return "`"$escaped`""
}

# PowerShell splits an unbound -p:Name=Value into "-p:" and "Name=Value".
$hiddenTimeoutMinutes = 30
$testArgs = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string]$args[$i]
    if ($arg -eq '-HiddenTimeoutMinutes' -and $i + 1 -lt $args.Count) {
        $i++
        $hiddenTimeoutMinutes = [int]$args[$i]
        continue
    }
    if ($arg -match '^-[^:]*:$' -and $i + 1 -lt $args.Count) {
        $i++
        $arg += [string]$args[$i]
    }
    $testArgs.Add($arg)
}

$repoRoot = Split-Path $PSScriptRoot -Parent
if ($testArgs.Count -eq 0) {
    $testArgs.Add((Join-Path $repoRoot 'OpenNest.WinForms.Tests\OpenNest.WinForms.Tests.csproj'))
}

$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$commandLine = (@($dotnet, 'test') + $testArgs | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join ' '
$logPath = Join-Path ([System.IO.Path]::GetTempPath()) "OpenNestTests-$([guid]::NewGuid().ToString('N')).log"

# Build servers would be killed with the job while other builds may reuse them.
$buildEnvironment = @{ MSBUILDDISABLENODEREUSE = '1'; UseSharedCompilation = 'false' }
$savedEnvironment = @{}
foreach ($name in $buildEnvironment.Keys) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name])
}

$run = $null
$reader = $null
$exitCode = 124
try {
    $run = [HiddenDesktopRun]::new($commandLine, (Get-Location -PSProvider FileSystem).ProviderPath, $logPath)
    $reader = [System.IO.StreamReader]::new(
        [System.IO.FileStream]::new($logPath, 'Open', 'Read', 'ReadWrite, Delete'),
        [System.Text.Encoding]::UTF8)

    $deadline = [DateTime]::UtcNow.AddMinutes($hiddenTimeoutMinutes)
    $pending = ''
    do {
        $exited = $run.WaitForExit(200)
        $pending += $reader.ReadToEnd()
        $lines = $pending -split '\r?\n'
        $pending = $lines[-1]
        if ($lines.Count -gt 1) {
            $lines[0..($lines.Count - 2)] | Write-Output
        }
    } until ($exited -or [DateTime]::UtcNow -ge $deadline)

    if ($pending) {
        Write-Output $pending
    }
    if ($exited) {
        $exitCode = $run.ExitCode
    }
    else {
        Write-Error "Stopped after $hiddenTimeoutMinutes minutes. A modal dialog nobody can dismiss is the usual cause." -ErrorAction Continue
    }
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    if ($reader) { $reader.Dispose() }
    if ($run) { $run.Dispose() }
    Remove-Item -LiteralPath $logPath -ErrorAction SilentlyContinue
}

exit $exitCode
