using Microsoft.Extensions.Logging;

namespace Hisa.App.Diagnostics;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly AppLogStore _store;

    public InMemoryLoggerProvider(AppLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, _store);

    public void Dispose()
    {
    }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly AppLogStore _store;

        public InMemoryLogger(string categoryName, AppLogStore store)
        {
            _categoryName = categoryName;
            _store = store;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var sourceTag = IsNetworkLog(_categoryName, message) ? "NET" : "APP";
            _store.Add(new AppLogEntry(
                DateTimeOffset.UtcNow,
                logLevel,
                sourceTag,
                _categoryName,
                message,
                exception?.ToString()));
        }

        private static bool IsNetworkLog(string categoryName, string message)
        {
            if (categoryName.Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains("SocketsHttpHandler", StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains("HttpClient", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return message.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("request", StringComparison.OrdinalIgnoreCase) && message.Contains("response", StringComparison.OrdinalIgnoreCase);
        }
    }
}
