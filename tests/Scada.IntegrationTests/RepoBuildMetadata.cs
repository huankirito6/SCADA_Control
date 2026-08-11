using System.Xml.Linq;
using Xunit;
using Xunit.Sdk;

namespace Scada.IntegrationTests;

internal static class RepoBuildMetadata
{
    private const string BuildMetadataFileName = "Directory.Build.props";

    public static void AssertTargetFramework(string expected)
    {
        Assert.Equal(expected, ReadProperty("TargetFramework"));
    }

    public static void AssertProperty(string propertyName, string expected)
    {
        Assert.Equal(expected, ReadProperty(propertyName));
    }

    public static void AssertWarningsAsErrors()
    {
        AssertProperty("TreatWarningsAsErrors", "true");
    }

    private static string? ReadProperty(string propertyName)
    {
        XDocument document = LoadBuildMetadata();
        return document
            .Descendants(propertyName)
            .Select(element => element.Value.Trim())
            .FirstOrDefault();
    }

    private static XDocument LoadBuildMetadata()
    {
        string metadataPath = Path.Combine(FindRepositoryRoot(), BuildMetadataFileName);
        Assert.True(
            File.Exists(metadataPath),
            $"Expected repository build metadata file to exist: {metadataPath}");

        return XDocument.Load(metadataPath);
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
}
