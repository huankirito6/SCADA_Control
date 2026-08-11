using System.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace Scada.SecurityTests;

public sealed class DeploymentClosureTests
{
    [Fact]
    public void WebPublishClosureContainsNoDriverAssemblies()
    {
        string repositoryRoot = DeploymentClosure.FindRepositoryRoot();
        string webProject = Path.Combine(repositoryRoot, "src", "Scada.Web", "Scada.Web.csproj");
        if (!File.Exists(webProject))
        {
            throw new XunitException($"Expected Web project to exist before publish: {webProject}");
        }

        string publishDirectory = Path.Combine(
            Path.GetTempPath(),
            $"scada-web-publish-{Guid.NewGuid():N}");

        try
        {
            DeploymentClosure.PublishWeb(repositoryRoot, webProject, publishDirectory);
            DeploymentClosure.AssertNoDriverAssemblies(publishDirectory);
        }
        finally
        {
            if (Directory.Exists(publishDirectory))
            {
                Directory.Delete(publishDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void DriverDllClosureNegativeControlIsRejected()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"scada-forbidden-closure-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(fixtureDirectory);
            string forbiddenAssembly = Path.Combine(fixtureDirectory, "Scada.Drivers.ForbiddenFixture.dll");
            File.WriteAllBytes(forbiddenAssembly, [0x53, 0x43, 0x41, 0x44, 0x41]);

            XunitException exception = Assert.Throws<XunitException>(
                () => DeploymentClosure.AssertNoDriverAssemblies(fixtureDirectory));

            Assert.Contains("Scada.Drivers.ForbiddenFixture.dll", exception.Message, StringComparison.Ordinal);
            Assert.Contains("forbidden driver assemblies", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory))
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
        }
    }
}

internal static class DeploymentClosure
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(2);

    public static void PublishWeb(string repositoryRoot, string webProject, string publishDirectory)
    {
        CommandResult result = RunDotNet(
            repositoryRoot,
            "publish",
            webProject,
            "-c",
            "Release",
            "--output",
            publishDirectory,
            "--nologo",
            "-p:RestoreLockedMode=true");

        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"dotnet publish failed for Scada.Web.{Environment.NewLine}{FormatDiagnostics(result)}");
        }

        if (!Directory.Exists(publishDirectory))
        {
            throw new XunitException(
                $"dotnet publish reported success but did not create: {publishDirectory}{Environment.NewLine}" +
                FormatDiagnostics(result));
        }
    }

    public static void AssertNoDriverAssemblies(string publishDirectory)
    {
        if (!Directory.Exists(publishDirectory))
        {
            throw new XunitException($"Expected publish directory to exist: {publishDirectory}");
        }

        string[] forbiddenAssemblies = Directory
            .EnumerateFiles(publishDirectory, "Scada.Drivers.*.dll", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(publishDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (forbiddenAssemblies.Length > 0)
        {
            throw new XunitException(
                "Web publish closure contains forbidden driver assemblies:" + Environment.NewLine +
                string.Join(Environment.NewLine, forbiddenAssemblies));
        }
    }

    public static string FindRepositoryRoot()
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

    private static CommandResult RunDotNet(string workingDirectory, params string[] arguments)
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
        using CancellationTokenSource timeout = new(PublishTimeout);

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
                // The process exited between the timeout and kill attempt.
            }

            CommandResult timedOutResult = new(
                arguments,
                -1,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
            throw new XunitException(
                $"dotnet publish timed out after {PublishTimeout.TotalSeconds:0} seconds." +
                $"{Environment.NewLine}{FormatDiagnostics(timedOutResult)}");
        }

        return new CommandResult(
            arguments,
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
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
        => "dotnet " + string.Join(' ', arguments.Select(QuoteForDisplay));

    private static string QuoteForDisplay(string argument)
        => argument.Length > 0 && argument.All(character => !char.IsWhiteSpace(character) && character != '"')
            ? argument
            : $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed record CommandResult(
        IReadOnlyList<string> Arguments,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
