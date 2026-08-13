using Scada.Cli.Database;
using Scada.Deployment;

if (args.Length != 4 || !string.Equals(args[0], "database", StringComparison.OrdinalIgnoreCase) || !string.Equals(args[1], "migrate-offline", StringComparison.OrdinalIgnoreCase) || !string.Equals(args[2], "--deployment", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(args[3]))
{
    Console.Error.WriteLine("Usage: database migrate-offline --deployment <absolute path>");
    return 2;
}

try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Offline migration is supported only on Windows.");
    OfflineMigrationCommand.RequestOwnerMigration(DeploymentConfiguration.Load(args[3]));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
