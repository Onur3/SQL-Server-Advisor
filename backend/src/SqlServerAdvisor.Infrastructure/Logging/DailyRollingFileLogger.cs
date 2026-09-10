using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SqlServerAdvisor.Infrastructure.Logging;

public sealed class DailyRollingFileLoggerOptions
{
    public bool Enabled { get; set; } = true;
    public string DirectoryPath { get; set; } = string.Empty;
    public string FileNamePrefix { get; set; } = "sqladvisor";
    public int RetainedDays { get; set; } = 14;
}

public static class DailyRollingFileLoggerExtensions
{
    public static ILoggingBuilder AddSqlAdvisorFileLogging(
        this ILoggingBuilder logging,
        IConfiguration configuration,
        string defaultPrefix)
    {
        var options = new DailyRollingFileLoggerOptions();
        configuration.GetSection("Logging:File").Bind(options);

        if (string.IsNullOrWhiteSpace(options.DirectoryPath))
        {
            var keyPath = configuration["Security:DataProtectionKeyPath"];
            options.DirectoryPath = !string.IsNullOrWhiteSpace(keyPath)
                ? Path.Combine(keyPath, "Logs")
                : Path.Combine(AppContext.BaseDirectory, "Logs");
        }

        if (string.IsNullOrWhiteSpace(options.FileNamePrefix) ||
            options.FileNamePrefix.Equals("sqladvisor", StringComparison.OrdinalIgnoreCase))
        {
            options.FileNamePrefix = defaultPrefix;
        }

        if (options.Enabled)
            logging.AddProvider(new DailyRollingFileLoggerProvider(options));

        return logging;
    }
}

public sealed class DailyRollingFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly DailyRollingFileLoggerOptions options;
    private readonly ConcurrentDictionary<string, DailyRollingFileLogger> loggers = new(StringComparer.Ordinal);
    private readonly object writeLock = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();
    private DateOnly currentDate;
    private StreamWriter? writer;
    private bool disposed;

    public DailyRollingFileLoggerProvider(DailyRollingFileLoggerOptions options)
    {
        this.options = options;
        this.options.RetainedDays = Math.Clamp(this.options.RetainedDays, 1, 365);
        CleanupOldFiles();
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, category => new DailyRollingFileLogger(this, category));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        this.scopeProvider = scopeProvider;

    internal IDisposable? PushScope<TState>(TState state) where TState : notnull =>
        scopeProvider.Push(state);

    internal void Write<TState>(
        LogLevel logLevel,
        string category,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (disposed) return;

        var timestamp = DateTimeOffset.Now;
        var message = formatter(state, exception);
        var scopes = new List<string>();
        scopeProvider.ForEachScope((scope, list) =>
        {
            if (scope is not null) list.Add(scope.ToString() ?? string.Empty);
        }, scopes);

        lock (writeLock)
        {
            if (disposed || !TryEnsureWriter(timestamp)) return;

            try
            {
                writer!.Write(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                writer.Write(" [");
                writer.Write(ToLevelCode(logLevel));
                writer.Write("] ");
                writer.Write(category);
                if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
                {
                    writer.Write(" [EventId=");
                    writer.Write(eventId.ToString());
                    writer.Write(']');
                }

                if (scopes.Count > 0)
                {
                    writer.Write(" [Scope=");
                    writer.Write(string.Join(" => ", scopes));
                    writer.Write(']');
                }

                writer.Write(" - ");
                writer.WriteLine(message);
                if (exception is not null)
                    writer.WriteLine(exception.ToString());
            }
            catch
            {
                CloseWriter();
            }
        }
    }

    private bool TryEnsureWriter(DateTimeOffset timestamp)
    {
        var date = DateOnly.FromDateTime(timestamp.LocalDateTime);
        if (writer is not null && currentDate == date) return true;

        CloseWriter();
        try
        {
            Directory.CreateDirectory(options.DirectoryPath);
            var path = Path.Combine(
                options.DirectoryPath,
                $"{SanitizePrefix(options.FileNamePrefix)}-{date:yyyyMMdd}.log");
            var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            writer = new StreamWriter(stream) { AutoFlush = true };
            currentDate = date;
            CleanupOldFiles();
            return true;
        }
        catch
        {
            CloseWriter();
            return false;
        }
    }

    private void CloseWriter()
    {
        try { writer?.Dispose(); }
        catch { }
        writer = null;
    }

    private void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(options.DirectoryPath)) return;
            var cutoff = DateTime.Now.Date.AddDays(-options.RetainedDays);
            var pattern = $"{SanitizePrefix(options.FileNamePrefix)}-*.log";
            foreach (var file in Directory.EnumerateFiles(options.DirectoryPath, pattern))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best effort only: stale log cleanup must never affect the application.
                }
            }
        }
        catch
        {
            // Best-effort retention cleanup only.
        }
    }

    private static string SanitizePrefix(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "sqladvisor" : cleaned;
    }

    private static string ToLevelCode(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "NON"
    };

    public void Dispose()
    {
        lock (writeLock)
        {
            if (disposed) return;
            disposed = true;
            CloseWriter();
        }
        loggers.Clear();
    }
}

internal sealed class DailyRollingFileLogger(
    DailyRollingFileLoggerProvider provider,
    string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        provider.PushScope(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        provider.Write(logLevel, category, eventId, state, exception, formatter);
    }
}
