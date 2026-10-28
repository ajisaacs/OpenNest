namespace OpenNest.WinForms.Tests;

public class UpdaterServiceTests
{
    [Fact]
    public async Task ManualCheckOutsideAnInstallationReportsNotInstalled()
    {
        // Test hosts never start through VelopackApp, so there is no locator.
        using var updater = new UpdaterService(feedUrl: null, includeDevBuilds: false);

        Assert.False(updater.CanUpdate);
        Assert.Equal(UpdaterService.CheckResult.NotInstalled, await updater.CheckNowAsync());
        Assert.False(updater.HasPendingUpdate);
    }

    [Fact]
    public void VersionOutsideAnInstallationIsTheAssemblyVersionWithoutBuildMetadata()
    {
        using var updater = new UpdaterService(feedUrl: null, includeDevBuilds: false);

        Assert.Equal(UpdaterService.DisplayVersion(), updater.CurrentVersion);
        Assert.DoesNotContain("+", updater.CurrentVersion);
        Assert.StartsWith("0.", updater.CurrentVersion);
    }
}
