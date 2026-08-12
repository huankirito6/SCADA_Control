using Xunit;
using Xunit.Sdk;

namespace Scada.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void ExactProductProjectSetIsPresentAndInSolution()
        => Architecture.AssertExactProductProjectSet();

    [Fact]
    public void ExactAllowedProductDependencyGraphIsEnforced()
        => Architecture.AssertExactAllowedProductDependencyGraph();

    [Fact]
    public void EvaluatedDependencyGraphRejectsReferencesIntroducedByImports()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"scada-imported-reference-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(fixtureDirectory);
            string importedProps = Path.Combine(fixtureDirectory, "ForbiddenReference.props");
            File.WriteAllText(
                importedProps,
                """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.10" />
                  </ItemGroup>
                </Project>
                """);
            string fixtureProject = Path.Combine(fixtureDirectory, "Scada.Web.csproj");
            File.WriteAllText(
                fixtureProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="ForbiddenReference.props" />
                </Project>
                """);

            XunitException exception = Assert.Throws<XunitException>(
                () => Architecture.AssertProjectDependencyGraph(
                    fixtureProject,
                    "Scada.Web",
                    ["Scada.Application", "Scada.Contracts"],
                    []));

            Assert.Contains("Microsoft.Data.Sqlite", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory))
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void WebCannotReferenceDriversRuntimeOrSqlite()
        => Architecture.AssertNoReferences(
            "Scada.Web",
            "Scada.Runtime",
            "Scada.Drivers",
            "Scada.Infrastructure.Sqlite",
            "Microsoft.Data.Sqlite");

    [Fact]
    public void DomainHasNoProductDependencies()
        => Architecture.AssertOnlySystemReferences("Scada.Domain");

    [Fact]
    public void ApplicationDependsOnlyOnDomainAndSceneContracts()
        => Architecture.AssertNoReferences(
            "Scada.Application",
            "Google.Protobuf",
            "Grpc");

    [Fact]
    public void RuntimeCannotReferenceWeb()
        => Architecture.AssertNoReferences("Scada.Runtime", "Scada.Web");

    [Fact]
    public void OnlySqliteInfrastructureReferencesMicrosoftDataSqlite()
        => Architecture.AssertOnlySqliteInfrastructureReferencesMicrosoftDataSqlite();
}
