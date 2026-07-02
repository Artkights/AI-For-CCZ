using System.Diagnostics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace CCZModStudio;

internal sealed record ApplicationErrorReport(
    DateTimeOffset Timestamp,
    string Source,
    string Summary,
    string LogPath,
    string ExceptionType,
    string Message);

internal static class ApplicationErrorService
{
    private const int SummaryMaxLength = 180;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static int _isReporting;
    private static string _currentProjectPath = string.Empty;

    public static event Action<ApplicationErrorReport>? ErrorReported;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCZModStudio",
        "Logs");

    public static void SetCurrentProjectPath(string? projectPath)
        => _currentProjectPath = string.IsNullOrWhiteSpace(projectPath) ? string.Empty : projectPath;

    public static void RegisterWinFormsHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception
                            ?? new InvalidOperationException("Unhandled non-Exception object: " + Convert.ToString(e.ExceptionObject));
            Report(exception, e.IsTerminating ? "AppDomain terminating" : "AppDomain", notify: !e.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "TaskScheduler");
            e.SetObserved();
        };
    }

    public static ApplicationErrorReport Report(Exception exception, string source, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source.Trim();

        var timestamp = DateTimeOffset.Now;
        var logPath = string.Empty;
        if (Interlocked.Exchange(ref _isReporting, 1) == 0)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                logPath = Path.Combine(LogDirectory, $"cczmodstudio-{timestamp:yyyyMMdd}.log");
                var jsonlPath = Path.Combine(LogDirectory, "exceptions.jsonl");
                WriteTextLog(logPath, timestamp, source, exception);
                WriteJsonLine(jsonlPath, timestamp, source, exception, logPath);
            }
            catch (Exception logException)
            {
                Debug.WriteLine("CCZModStudio exception logging failed: " + logException);
                logPath = string.Empty;
            }
            finally
            {
                Interlocked.Exchange(ref _isReporting, 0);
            }
        }
        else
        {
            Debug.WriteLine("CCZModStudio nested exception while reporting: " + exception);
        }

        var report = new ApplicationErrorReport(
            timestamp,
            source,
            BuildSummary(exception),
            logPath,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message);

        if (notify)
        {
            try
            {
                ErrorReported?.Invoke(report);
            }
            catch (Exception notifyException)
            {
                Debug.WriteLine("CCZModStudio exception notification failed: " + notifyException);
            }
        }

        return report;
    }

    private static void WriteTextLog(string logPath, DateTimeOffset timestamp, string source, Exception exception)
    {
        var lines = new[]
        {
            "--------------------------------------------------------------------------------",
            $"Time: {timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}",
            $"Version: {GetApplicationVersion()}",
            $"Source: {source}",
            $"ProjectPath: {TryGetCurrentProjectPath()}",
            $"Thread: {Environment.CurrentManagedThreadId}",
            $"Exception: {exception.GetType().FullName}",
            $"Message: {exception.Message}",
            "Stack:",
            exception.ToString(),
            string.Empty
        };
        File.AppendAllText(logPath, string.Join(Environment.NewLine, lines));
    }

    private static void WriteJsonLine(string jsonlPath, DateTimeOffset timestamp, string source, Exception exception, string logPath)
    {
        var payload = new
        {
            timestamp = timestamp.ToString("O"),
            version = GetApplicationVersion(),
            source,
            projectPath = TryGetCurrentProjectPath(),
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            stack = exception.ToString(),
            logPath
        };
        File.AppendAllText(jsonlPath, JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine);
    }

    private static string BuildSummary(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = exception.GetType().Name;
        }

        message = string.Join(" ", message.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return message.Length <= SummaryMaxLength ? message : message[..SummaryMaxLength] + "...";
    }

    private static string GetApplicationVersion()
    {
        try
        {
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                   ?? Application.ProductVersion
                   ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string TryGetCurrentProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            return _currentProjectPath;
        }

        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return string.Empty;
        }
    }
}
