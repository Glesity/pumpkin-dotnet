using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Logging;

namespace Pumpkin.DotNet.Runtime.Logging;

internal sealed class PumpkinLogger : ILogger
{
    private string _name;

    private PumpkinLogger(string name)
    {
        _name = name;
    }

    public static PumpkinLogger CreateLogger(string name)
    {
        return new(name);
    }

    public static PumpkinLogger CreateLogger<T>()
    {
        return new(typeof(T).Name);
    }

    public void Log(LogLevel logLevel, string message)
    {
        var witLogLevel = logLevel switch
        {
            LogLevel.Trace => ILoggingImports.Level.TRACE,
            LogLevel.Debug => ILoggingImports.Level.DEBUG,
            LogLevel.Info => ILoggingImports.Level.INFO,
            LogLevel.Warn => ILoggingImports.Level.WARN,
            LogLevel.Error => ILoggingImports.Level.ERROR,
            _ => throw new NotImplementedException(),
        };

        ILoggingImports.Log(witLogLevel, AddName(message));
    }

    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warn, message);
    public void Error(string message) => Log(LogLevel.Error, message);

    private string AddName(string message)
    {
        return $"[{_name}] {message}";
    }
}
