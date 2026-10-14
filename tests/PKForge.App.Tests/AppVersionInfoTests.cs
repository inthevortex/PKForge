using System.Reflection;
using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class AppVersionInfoTests
{
    [Fact]
    public void DisplayVersionDropsTheCommitSuffix()
    {
        var info = AppVersionInfo.Create("3.1.0+2068c92d1f", new Version(3, 1, 0, 0), "310");

        Assert.Equal("3.1.0", info.VersionString);
        Assert.Equal(new Version(3, 1, 0), info.Version);
        Assert.Equal("310", info.BuildString);
    }

    [Fact]
    public void PrereleaseKeepsItsLabelButVersionIsNumeric()
    {
        var info = AppVersionInfo.Create("3.2.0-rc.1+abc", new Version(3, 2, 0, 0), "320");

        Assert.Equal("3.2.0-rc.1", info.VersionString);
        Assert.Equal(new Version(3, 2, 0), info.Version);
    }

    [Fact]
    public void MissingInformationalVersionFallsBackToAssemblyVersion()
    {
        var info = AppVersionInfo.Create(null, new Version(4, 0, 1, 0), null);

        Assert.Equal("4.0.1", info.VersionString);
        Assert.Equal(new Version(4, 0, 1), info.Version);
        Assert.Equal("", info.BuildString);
    }

    [Fact]
    public void NothingStampedYieldsZeroVersion()
    {
        var info = AppVersionInfo.Create("", null, " ");

        Assert.Equal("0.0.0", info.VersionString);
        Assert.Equal(new Version(0, 0, 0), info.Version);
        Assert.Equal("", info.BuildString);
    }

    [Fact]
    public void FromAssemblyReadsTheStampedAttributes()
    {
        var assembly = typeof(AppVersionInfoTests).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;

        var info = AppVersionInfo.FromAssembly(assembly);

        Assert.Equal(informational.Split('+')[0], info.VersionString);
        Assert.DoesNotContain('+', info.VersionString);
        Assert.Equal(fileVersion, info.BuildString);
    }
}
