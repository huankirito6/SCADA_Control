using Xunit;
using Xunit.Sdk;

namespace Scada.IntegrationTests;

public sealed class BuildMetadataTests
{
    private const string ForbiddenOverrideFixture =
        "tests/Scada.IntegrationTests/Fixtures/ForbiddenProjectOverride/ForbiddenProjectOverride.csproj";

    [Fact]
    public void EveryProductAssemblyTargetsNet10AndHasDeterministicBuild()
    {
        RepoBuildMetadata.AssertSolutionProjectsHaveProperties(
            "Release",
            ("TargetFramework", "net10.0"),
            ("Deterministic", "true"),
            ("TreatWarningsAsErrors", "true"));
    }

    [Fact]
    public void ConditionalProjectLevelOverrideIsRejected()
    {
        XunitException exception = Assert.Throws<XunitException>(() =>
            RepoBuildMetadata.AssertProjectHasProperties(
                ForbiddenOverrideFixture,
                "Release",
                ("TargetFramework", "net10.0"),
                ("Deterministic", "true"),
                ("TreatWarningsAsErrors", "true")));

        Assert.Contains("ForbiddenProjectOverride.csproj", exception.Message);
        Assert.Contains("Deterministic", exception.Message);
        Assert.Contains("expected 'true' but evaluated to 'false'", exception.Message);
    }
}
