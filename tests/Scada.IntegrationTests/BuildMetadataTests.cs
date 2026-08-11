using Xunit;

namespace Scada.IntegrationTests;

public sealed class BuildMetadataTests
{
    [Fact]
    public void EveryProductAssemblyTargetsNet10AndHasDeterministicBuild()
    {
        RepoBuildMetadata.AssertTargetFramework("net10.0");
        RepoBuildMetadata.AssertProperty("Deterministic", "true");
        RepoBuildMetadata.AssertWarningsAsErrors();
    }
}
