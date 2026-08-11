using System.Diagnostics;
using System.Globalization;

if (args.Length != 3 || (args[0] is not "parent" and not "child"))
{
    Console.Error.WriteLine("Expected: <parent|child> <parent-identity-file> <child-identity-file>.");
    return 2;
}

string role = args[0];
string parentIdentityFile = args[1];
string childIdentityFile = args[2];
await WriteIdentityAsync(role == "parent" ? parentIdentityFile : childIdentityFile);
Console.Out.WriteLine($"fixture-{role}-standard-output");
Console.Error.WriteLine($"fixture-{role}-standard-error");

if (role == "parent")
{
    ProcessStartInfo startInfo = new()
    {
        FileName = "dotnet",
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    startInfo.ArgumentList.Add("child");
    startInfo.ArgumentList.Add(parentIdentityFile);
    startInfo.ArgumentList.Add(childIdentityFile);

    using Process child = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start fixture child.");
    await child.WaitForExitAsync();
    return child.ExitCode;
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

static async Task WriteIdentityAsync(string path)
{
    using Process current = Process.GetCurrentProcess();
    string content = $"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                     $"{current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}";
    await File.WriteAllTextAsync(path, content);
}
