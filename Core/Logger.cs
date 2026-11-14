namespace Bozo.Core;

public sealed class Logger
{
    private readonly string _path;
    private readonly object _gate = new();

    public Logger(string logDir)
    {
        Directory.CreateDirectory(logDir);
        _path = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
    }

    public event Action<string, LogLevel>? Line;

    public void Trace(string m) => Write(LogLevel.Trace, m);
    public void Info(string m)  => Write(LogLevel.Info, m);
    public void Warn(string m)  => Write(LogLevel.Warn, m);
    public void Error(string m) => Write(LogLevel.Error, m);

    private void Write(LogLevel level, string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Bozo] [{level.ToString().ToLower()}] {msg}";
        lock (_gate) File.AppendAllText(_path, line + Environment.NewLine);
        Line?.Invoke(line, level);
    }
}

public enum LogLevel { Trace, Info, Warn, Error }
