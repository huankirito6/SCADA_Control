using NetArchTest.Rules;
using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;
using System.Text.Json;
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
        "Scada.Hosting",
        "Scada.Runtime",
        "Scada.Web",
    ];

    private static readonly Dictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Scada.Application"] = ["Scada.Contracts", "Scada.Domain"],
            ["Scada.Cli"] = ["Scada.Application", "Scada.Contracts"],
            ["Scada.Contracts"] = [],
            ["Scada.Domain"] = [],
            ["Scada.Drivers.Abstractions"] = ["Scada.Domain"],
            ["Scada.Drivers.ModbusRtu"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.ModbusTcp"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.OpcUa"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Drivers.Simulator"] = ["Scada.Drivers.Abstractions"],
            ["Scada.Infrastructure.Sqlite"] = ["Scada.Application", "Scada.Domain"],
            ["Scada.Hosting"] = ["Scada.Infrastructure.Sqlite"],
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
            ["Scada.Web"] = ["Scada.Hosting"],
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
            AddProjectDependencyViolations(
                violations,
                productProjects[projectName],
                projectName,
                AllowedProjectReferences[projectName],
                AllowedPackageReferences[projectName]);
        }

        if (violations.Count > 0)
        {
            throw new XunitException(
                "Product dependency graph does not match the exact allowed graph." + Environment.NewLine +
                string.Join(Environment.NewLine, violations));
        }
    }

    internal static void AssertProjectDependencyGraph(
        string projectPath,
        string projectName,
        string[] expectedProjectReferences,
        string[] expectedPackageReferences)
    {
        List<string> violations = [];
        AddProjectDependencyViolations(
            violations,
            projectPath,
            projectName,
            expectedProjectReferences,
            expectedPackageReferences);
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

        EvaluatedReferences references = GetEvaluatedReferences(projectPath);
        string[] declaredViolations = references.ProjectReferences
            .Concat(references.PackageReferences)
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
        string[] packageReferences = GetEvaluatedReferences(productProjects[projectName]).PackageReferences;
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
            .Where(project => GetEvaluatedReferences(project.Value).PackageReferences
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

    private static void AddProjectDependencyViolations(
        List<string> violations,
        string projectPath,
        string projectName,
        IEnumerable<string> expectedProjectReferences,
        IEnumerable<string> expectedPackageReferences)
    {
        EvaluatedReferences references = GetEvaluatedReferences(projectPath);
        AddSetViolations(violations, projectName, "project references", expectedProjectReferences, references.ProjectReferences);
        AddSetViolations(violations, projectName, "package references", expectedPackageReferences, references.PackageReferences);
    }

    private static EvaluatedReferences GetEvaluatedReferences(string projectPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-getItem:ProjectReference,PackageReference");
        startInfo.ArgumentList.Add("--nologo");

        using Process process = Process.Start(startInfo)
            ?? throw new XunitException($"Failed to start MSBuild evaluation for '{projectPath}'.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new XunitException($"MSBuild evaluation timed out for '{projectPath}'.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new XunitException(
                $"MSBuild evaluation failed for '{projectPath}' with exit code {process.ExitCode}." +
                $"{Environment.NewLine}{error}{Environment.NewLine}{output}");
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement items = document.RootElement.GetProperty("Items");
        string[] projectReferences = items.GetProperty("ProjectReference")
            .EnumerateArray()
            .Select(item => Path.GetFileNameWithoutExtension(item.GetProperty("Identity").GetString())!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] packageReferences = items.GetProperty("PackageReference")
            .EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new EvaluatedReferences(projectReferences, packageReferences);
    }

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

    private sealed record EvaluatedReferences(string[] ProjectReferences, string[] PackageReferences);
}
