using NetArchTest.Rules;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Xunit.Sdk;

namespace Scada.ArchitectureTests;

internal static class Architecture
{
    internal static readonly string[] ProductProjectNames =
    [
        "Scada.Application",
        "Scada.Cli",
        "Scada.Contracts",
        "Scada.Domain",
        "Scada.Drivers.Abstractions",
        "Scada.Drivers.ModbusRtu",
        "Scada.Drivers.ModbusTcp",
        "Scada.Drivers.OpcUa",
        "Scada.Drivers.Simulator",
        "Scada.Infrastructure.Sqlite",
        "Scada.Runtime",
        "Scada.Web",
    ];

    private static readonly Dictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Scada.Application"] = ["Scada.Domain"],
            ["Scada.Cli"] = ["Scada.Application", "Scada.Contracts"],
            ["Scada.Contracts"] = [],
            ["Scada.Domain"] = [],
            ["Scada.Drivers.Abstractions"] = ["Scada.Domain"],
            ["Scada.Drivers.ModbusRtu"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.ModbusTcp"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.OpcUa"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.Simulator"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Infrastructure.Sqlite"] = ["Scada.Application", "Scada.Domain"],
            ["Scada.Runtime"] =
            [
                "Scada.Application",
                "Scada.Contracts",
                "Scada.Drivers.Abstractions",
                "Scada.Drivers.ModbusRtu",
                "Scada.Drivers.ModbusTcp",
                "Scada.Drivers.OpcUa",
                "Scada.Drivers.Simulator",
                "Scada.Infrastructure.Sqlite",
            ],
            ["Scada.Web"] = ["Scada.Application", "Scada.Contracts"],
        };

    private static readonly Dictionary<string, string[]> AllowedPackageReferences =
        ProductProjectNames.ToDictionary(
            projectName => projectName,
            projectName => string.Equals(projectName, "Scada.Infrastructure.Sqlite", StringComparison.Ordinal)
                ? new[] { "Microsoft.Data.Sqlite" }
                : [],
            StringComparer.Ordinal);

    public static void AssertExactProductProjectSet()
    {
        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> productProjects = FindProductProjects(repositoryRoot);
        AssertSameSet(
            "Product project set does not match the required architecture graph.",
            ProductProjectNames,
            productProjects.Keys);

        string solutionPath = Path.Combine(repositoryRoot, "Scada.sln");
        if (!File.Exists(solutionPath))
        {
            throw new XunitException($"Expected solution file to exist: {solutionPath}");
        }

        string normalizedSolution = File.ReadAllText(solutionPath).Replace('\\', '/');
        string[] projectsMissingFromSolution = ProductProjectNames
            .Where(projectName => !normalizedSolution.Contains(
                $"src/{projectName}/{projectName}.csproj",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (projectsMissingFromSolution.Length > 0)
        {
            throw new XunitException(
                "Required product projects are missing from Scada.sln." + Environment.NewLine +
                $"Missing: {FormatList(projectsMissingFromSolution)}");
        }
    }

    public static void AssertExactAllowedProductDependencyGraph()
    {
        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> productProjects = FindProductProjects(repositoryRoot);
        AssertSameSet(
            "Product project set does not match the required architecture graph.",
            ProductProjectNames,
            productProjects.Keys);

        List<string> violations = [];
        foreach (string projectName in ProductProjectNames)
        {
            XDocument project = LoadProject(productProjects[projectName]);
            string[] actualProjectReferences = GetReferences(project, "ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference)!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            AddSetViolations(
                violations,
                projectName,
                "project references",
                AllowedProjectReferences[projectName],
                actualProjectReferences);

            string[] actualPackageReferences = GetReferences(project, "PackageReference")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            AddSetViolations(
                violations,
                projectName,
                "package references",
                AllowedPackageReferences[projectName],
                actualPackageReferences);
        }

        if (violations.Count > 0)
        {
            throw new XunitException(
                "Product dependency graph does not match the exact allowed graph." + Environment.NewLine +
                string.Join(Environment.NewLine, violations));
        }
    }

    public static void AssertNoReferences(string projectName, params string[] forbiddenPrefixes)
    {
        if (!ProductProjectNames.Contains(projectName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown product project '{projectName}'.", nameof(projectName));
        }

        if (forbiddenPrefixes.Length == 0 || forbiddenPrefixes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty forbidden prefix is required.", nameof(forbiddenPrefixes));
        }

        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> productProjects = FindProductProjects(repositoryRoot);
        if (!productProjects.TryGetValue(projectName, out string? projectPath))
        {
            throw new XunitException($"Expected product project '{projectName}' to exist under src.");
        }

        Assembly assembly = LoadProductAssembly(projectName);
        TestResult netArchResult = Types
            .InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenPrefixes)
            .GetResult();

        string[] compiledViolations = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => forbiddenPrefixes.Any(prefix => MatchesPrefix(name, prefix)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        XDocument project = LoadProject(projectPath);
        string[] declaredViolations = GetReferences(project, "ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference)!)
            .Concat(GetReferences(project, "PackageReference"))
            .Where(reference => forbiddenPrefixes.Any(prefix => MatchesPrefix(reference, prefix)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        if (!netArchResult.IsSuccessful || compiledViolations.Length > 0 || declaredViolations.Length > 0)
        {
            throw new XunitException(
                $"Project '{projectName}' has forbidden dependencies." + Environment.NewLine +
                $"Forbidden prefixes: {FormatList(forbiddenPrefixes)}{Environment.NewLine}" +
                $"NetArchTest successful: {netArchResult.IsSuccessful}{Environment.NewLine}" +
                $"Compiled references: {FormatList(compiledViolations)}{Environment.NewLine}" +
                $"Declared references: {FormatList(declaredViolations)}");
        }
    }

    public static void AssertOnlySystemReferences(string projectName)
    {
        string[] otherProductProjects = ProductProjectNames
            .Where(candidate => !string.Equals(candidate, projectName, StringComparison.Ordinal))
            .ToArray();
        AssertNoReferences(projectName, otherProductProjects);

        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> productProjects = FindProductProjects(repositoryRoot);
        XDocument project = LoadProject(productProjects[projectName]);
        string[] packageReferences = GetReferences(project, "PackageReference").ToArray();
        if (packageReferences.Length > 0)
        {
            throw new XunitException(
                $"Project '{projectName}' must have no package dependencies, but found: " +
                FormatList(packageReferences));
        }
    }

    public static void AssertOnlySqliteInfrastructureReferencesMicrosoftDataSqlite()
    {
        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> productProjects = FindProductProjects(repositoryRoot);
        string[] actualProjects = productProjects
            .Where(project => GetReferences(LoadProject(project.Value), "PackageReference")
                .Contains("Microsoft.Data.Sqlite", StringComparer.Ordinal))
            .Select(project => project.Key)
            .OrderBy(projectName => projectName, StringComparer.Ordinal)
            .ToArray();

        AssertSameSet(
            "Microsoft.Data.Sqlite package ownership is not exclusive to Scada.Infrastructure.Sqlite.",
            ["Scada.Infrastructure.Sqlite"],
            actualProjects);
    }

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string gitMarker = Path.Combine(directory.FullName, ".git");
            if (File.Exists(gitMarker) || Directory.Exists(gitMarker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new XunitException("Could not locate the repository root from the test output directory.");
    }

    private static Dictionary<string, string> FindProductProjects(string repositoryRoot)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        return Directory.Exists(sourceRoot)
            ? Directory
                .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path),
                    path => path,
                    StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Assembly LoadProductAssembly(string projectName)
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{projectName}.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new XunitException(
                $"Expected compiled product assembly '{projectName}' in the architecture test output: {assemblyPath}");
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private static XDocument LoadProject(string projectPath)
    {
        try
        {
            return XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new XunitException($"Could not read MSBuild project '{projectPath}': {exception.Message}");
        }
    }

    private static IEnumerable<string> GetReferences(XDocument project, string itemName)
        => project
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, itemName, StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!);

    private static bool MatchesPrefix(string candidate, string prefix)
        => string.Equals(candidate, prefix, StringComparison.Ordinal) ||
           candidate.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static void AddSetViolations(
        List<string> violations,
        string projectName,
        string referenceKind,
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        string[] missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] forbidden = actual.Except(expected, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || forbidden.Length > 0)
        {
            violations.Add(
                $"{projectName} {referenceKind}: missing [{FormatList(missing)}]; forbidden [{FormatList(forbidden)}]");
        }
    }

    private static void AssertSameSet(
        string message,
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        string[] missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] unexpected = actual.Except(expected, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unexpected.Length > 0)
        {
            throw new XunitException(
                message + Environment.NewLine +
                $"Missing: {FormatList(missing)}{Environment.NewLine}" +
                $"Unexpected: {FormatList(unexpected)}");
        }
    }

    private static string FormatList(IEnumerable<string> values)
    {
        string formatted = string.Join(", ", values);
        return formatted.Length == 0 ? "<none>" : formatted;
    }
}
