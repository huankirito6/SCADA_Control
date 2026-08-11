using System.Text.Json;

namespace Scada.Runtime.Time;

public sealed class FileClockStateStore(string path) : IClockStateStore
{
    private readonly string _path = ValidatePath(path);

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path;
    }

    public ClockState? Load()
    {
        if (!File.Exists(_path)) return null;
        var state = JsonSerializer.Deserialize<ClockState>(File.ReadAllText(_path));
        return state ?? throw new InvalidDataException("Clock checkpoint cannot be null.");
    }

    public void Save(ClockState state)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(state));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}