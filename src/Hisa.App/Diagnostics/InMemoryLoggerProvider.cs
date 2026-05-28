using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

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
        private static readonly Regex SensitiveTokenRegex = new(
            "(access_token|refresh_token|client_secret|authorization)\\s*[:=]\\s*([^\r\n\\s,;]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

            var message = RedactSensitiveValues(formatter(state, exception));
            var sourceTag = GetSourceTag(_categoryName, message);
            _store.Add(new AppLogEntry(
                DateTimeOffset.UtcNow,
                logLevel,
                sourceTag,
                _categoryName,
                message,
                exception is null ? null : RedactSensitiveValues(exception.ToString())));
        }

        private static string GetSourceTag(string categoryName, string message)
        {
            if (categoryName.Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains("SocketsHttpHandler", StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains("HttpClient", StringComparison.OrdinalIgnoreCase))
            {
                return "NET";
            }

            if (categoryName.Contains("Hisa.Esi", StringComparison.OrdinalIgnoreCase))
            {
                return "ESI";
            }

            if (message.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("request", StringComparison.OrdinalIgnoreCase) && message.Contains("response", StringComparison.OrdinalIgnoreCase))
            {
                return "NET";
            }

            return "APP";
        }

        private static string RedactSensitiveValues(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            var redacted = SensitiveTokenRegex.Replace(input, m => $"{m.Groups[1].Value}=***REDACTED***");
            redacted = redacted.Replace("Bearer ", "Bearer ***REDACTED***", StringComparison.OrdinalIgnoreCase);
            return redacted;
        }
    }
}
