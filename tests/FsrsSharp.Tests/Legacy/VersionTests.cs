using System.Reflection;
using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>version.test.ts</c>: <c>FSRSVersion</c> is the package version (here, <c>TsFsrsVersion</c> from
/// <c>Directory.Build.props</c>, which the test project embeds as assembly metadata) prefixed with <c>v</c>.</summary>
public class VersionTests
{
    [Fact]
    public void FsrsVersion_ShouldBeThePackageVersionPrefixedWithV()
    {
        var tsFsrsVersion = typeof(VersionTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "TsFsrsVersion").Value;

        Constants.FsrsVersion.Should().Be($"v{tsFsrsVersion}");
        Constants.FsrsVersion.Should().Be("v6.0.0");
    }
}
