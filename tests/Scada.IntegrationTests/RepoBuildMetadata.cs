using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace Scada.IntegrationTests;

internal static class RepoBuildMetadata
{
    private const string SolutionFileName = "Scada.sln";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] NewLineSeparators = ["\r\n", "\n"];

    public static void AssertSolutionProjectsHaveProperties(
        string configuration,
        params (string PropertyName, string ExpectedValue)[] expectedProperties)
    {
        string repositoryRoot = FindRepositoryRoot();
        string solutionPath = Path.Combine(repositoryRoot, SolutionFileName);

        if (!File.Exists(solutionPath))
        {
            throw new XunitException($"Expected solution file to exist: {solutionPath}");
        }

        string[] projectPaths = ListSolutionProjects(repositoryRoot, solutionPath);
        foreach (string projectPath in projectPaths)
        {
            AssertProjectHasProperties(projectPath, configuration, expectedProperties);
        }
    }

    public static void AssertProjectHasProperties(
        string projectPath,
        string configuration,
        params (string PropertyName, string ExpectedValue)[] expectedProperties)
    {
        ValidateExpectedProperties(configuration, expectedProperties);

        string repositoryRoot = FindRepositoryRoot();
        string fullProjectPath = ResolveProjectPath(repositoryRoot, projectPath);
        if (!File.Exists(fullProjectPath))
        {
            throw new XunitException($"Expected MSBuild project to exist: {fullProjectPath}");
        }

        string propertyNames = string.Join(',', expectedProperties.Select(property => property.PropertyName));
        CommandResult result = RunDotNet(
            repositoryRoot,
            "msbuild",
            fullProjectPath,
            "-nologo",
            $"-property:Configuration={configuration}",
            $"-getProperty:{propertyNames}");

        EnsureSuccess(result, $"evaluate MSBuild properties for '{fullProjectPath}'");
        Dictionary<string, string> evaluatedProperties =
            ParseEvaluatedProperties(result, fullProjectPath);

        foreach ((string propertyName, string expectedValue) in expectedProperties)
        {
            if (!evaluatedProperties.TryGetValue(propertyName, out string? actualValue))
            {
                throw new XunitException(
                    $"Project '{fullProjectPath}' did not return the effective '{propertyName}' property " +
                    $"for configuration '{configuration}'.{Environment.NewLine}{FormatDiagnostics(result)}");
            }

            if (!string.Equals(expectedValue, actualValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new XunitException(
                    $"Project '{fullProjectPath}' effective {configuration} property '{propertyName}' " +
                    $"expected '{expectedValue}' but evaluated to '{actualValue}'.{Environment.NewLine}" +
                    $"Command: {FormatCommand(result.Arguments)}");
            }
        }
    }

    private static string[] ListSolutionProjects(string repositoryRoot, string solutionPath)
    {
        CommandResult result = RunDotNet(repositoryRoot, "sln", solutionPath, "list");
        EnsureSuccess(result, $"enumerate projects in '{solutionPath}'");

        string[] projectPaths = result.StandardOutput
            .Split(NewLineSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Trim('"'))
            .Where(line => string.Equals(Path.GetExtension(line), ".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(line => ResolveProjectPath(repositoryRoot, line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                StringComparer.Ordinal)
            .ToArray();

        if (projectPaths.Length == 0)
        {
            throw new XunitException(
                $"Solution '{solutionPath}' did not enumerate any .csproj files.{Environment.NewLine}" +
                FormatDiagnostics(result));
        }

        string? missingProject = projectPaths.FirstOrDefault(path => !File.Exists(path));
        if (missingProject is not null)
        {
            throw new XunitException(
                $"Solution '{solutionPath}' enumerated a project that does not exist: {missingProject}");
        }

        return projectPaths;
    }

    private static Dictionary<string, string> ParseEvaluatedProperties(
        CommandResult result,
        string projectPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            if (!document.RootElement.TryGetProperty("Properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("MSBuild JSON output did not contain a Properties object.");
            }

            return properties
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.GetString() ?? property.Value.ToString(),
                    StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new XunitException(
                $"Could not parse evaluated MSBuild properties for '{projectPath}': {exception.Message}" +
                $"{Environment.NewLine}{FormatDiagnostics(result)}");
        }
    }

    private static void ValidateExpectedProperties(
        string configuration,
        (string PropertyName, string ExpectedValue)[] expectedProperties)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new ArgumentException("An MSBuild configuration is required.", nameof(configuration));
        }

        if (expectedProperties.Length == 0)
        {
            throw new ArgumentException("At least one expected MSBuild property is required.", nameof(expectedProperties));
        }

        string? invalidPropertyName = expectedProperties
            .Select(property => property.PropertyName)
            .FirstOrDefault(string.IsNullOrWhiteSpace);
        if (invalidPropertyName is not null)
        {
            throw new ArgumentException("MSBuild property names cannot be empty.", nameof(expectedProperties));
        }

        string? duplicatePropertyName = expectedProperties
            .GroupBy(property => property.PropertyName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicatePropertyName is not null)
        {
            throw new ArgumentException(
                $"MSBuild property '{duplicatePropertyName}' was requested more than once.",
                nameof(expectedProperties));
        }
    }

    private static string ResolveProjectPath(string repositoryRoot, string projectPath)
    {
        string normalizedPath = projectPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(
            Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : Path.Combine(repositoryRoot, normalizedPath));
    }

    private static CommandResult RunDotNet(
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new XunitException($"Failed to start command: {FormatCommand(arguments)}");
            }
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw new XunitException(
                $"Failed to start command '{FormatCommand(arguments)}': {exception.Message}");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(CommandTimeout);

        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // The process exited between the timeout and the kill attempt.
            }

            string capturedOutput = standardOutput.GetAwaiter().GetResult();
            string capturedError = standardError.GetAwaiter().GetResult();
            CommandResult timedOutResult = new(arguments, -1, capturedOutput, capturedError);
            throw new XunitException(
                $"Command timed out after {CommandTimeout.TotalSeconds:0} seconds.{Environment.NewLine}" +
                FormatDiagnostics(timedOutResult));
        }

        return new CommandResult(
            arguments,
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private static void EnsureSuccess(CommandResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"dotnet failed to {operation}.{Environment.NewLine}{FormatDiagnostics(result)}");
        }
    }

    private static string FormatDiagnostics(CommandResult result)
    {
        string standardOutput = string.IsNullOrWhiteSpace(result.StandardOutput)
            ? "<empty>"
            : result.StandardOutput.TrimEnd();
        string standardError = string.IsNullOrWhiteSpace(result.StandardError)
            ? "<empty>"
            : result.StandardError.TrimEnd();

        return $"Command: {FormatCommand(result.Arguments)}{Environment.NewLine}" +
               $"Exit code: {result.ExitCode}{Environment.NewLine}" +
               $"Standard output:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
               $"Standard error:{Environment.NewLine}{standardError}";
    }

    private static string FormatCommand(IEnumerable<string> arguments)
    {
        return "dotnet " + string.Join(' ', arguments.Select(QuoteForDisplay));
    }

    private static string QuoteForDisplay(string argument)
    {
        return argument.Length > 0 && argument.All(character => !char.IsWhiteSpace(character) && character != '"')
            ? argument
            : $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string FindRepositoryRoot()
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

    private sealed record CommandResult(
        IReadOnlyList<string> Arguments,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
