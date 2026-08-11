using System.Globalization;

if (args.Length != 1)
{
    Console.Error.WriteLine("Expected exactly one PID-file argument.");
    return 2;
}

await File.WriteAllTextAsync(
    args[0],
    Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
Console.Out.WriteLine("descendant-standard-output");
Console.Error.WriteLine("descendant-standard-error");
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;