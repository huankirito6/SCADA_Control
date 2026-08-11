using Xunit;

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
    public void ApplicationIsIndependentOfContractsAndProtobuf()
        => Architecture.AssertNoReferences(
            "Scada.Application",
            "Scada.Contracts",
            "Google.Protobuf",
            "Grpc");

    [Fact]
    public void RuntimeCannotReferenceWeb()
        => Architecture.AssertNoReferences("Scada.Runtime", "Scada.Web");

    [Fact]
    public void OnlySqliteInfrastructureReferencesMicrosoftDataSqlite()
        => Architecture.AssertOnlySqliteInfrastructureReferencesMicrosoftDataSqlite();
}
