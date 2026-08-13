using Scada.Hosting.Database;

string[] processArguments = Environment.GetCommandLineArgs();
string serviceName = ReadOption(processArguments, "--service-name") ?? "WebScada";
ServiceHostRunner.RunWindowsService(serviceName, ServiceHostRunner.ReadProcessDeploymentPath(processArguments));

static string? ReadOption(string[] args, string name)
{
    int index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
