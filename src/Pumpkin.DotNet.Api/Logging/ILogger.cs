namespace Pumpkin.DotNet.Api.Logging;

public interface ILogger
{
    void Log(LogLevel logLevel, string message);

    void Trace(string message) => Log(LogLevel.Trace, message);
    void Debug(string message) => Log(LogLevel.Debug, message);
    void Info(string message) => Log(LogLevel.Info, message);
    void Warn(string message) => Log(LogLevel.Warn, message);
    void Error(string message) => Log(LogLevel.Error, message);
}
