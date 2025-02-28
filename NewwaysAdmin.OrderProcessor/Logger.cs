using Microsoft.Extensions.Logging.Abstractions;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
// now on github
public class OrderProcessorLogger
{
    private readonly IOManager _ioManager;
    private readonly string _logIdentifier;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _logLock = new(1, 1);
    private IDataStorage<List<LogEntry>>? _storage;

    public OrderProcessorLogger(IOManager ioManager, string logIdentifier)
    {
        _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
        _logIdentifier = logIdentifier ?? throw new ArgumentNullException(nameof(logIdentifier));
    }

    private async Task EnsureStorageInitializedAsync()
    {
        if (_storage != null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_storage == null)
            {
                _storage = await _ioManager.GetStorageAsync<List<LogEntry>>("Logs");
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task LogAsync(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            await EnsureStorageInitializedAsync();

            await _logLock.WaitAsync();
            try
            {
                var logs = await LoadLogsAsync();
                logs.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message
                });

                if (_storage != null)
                {
                    await _storage.SaveAsync(_logIdentifier, logs);
                }
            }
            finally
            {
                _logLock.Release();
            }
        }
        catch (Exception ex)
        {
            // If we can't log to storage, at least try console
            Console.WriteLine($"Failed to log message: {ex.Message}");
            Console.WriteLine($"Original message: {message}");
        }
    }

    private async Task<List<LogEntry>> LoadLogsAsync()
    {
        if (_storage == null)
            return new List<LogEntry>();

        try
        {
            if (await _storage.ExistsAsync(_logIdentifier))
            {
                var logs = await _storage.LoadAsync(_logIdentifier);
                return logs ?? new List<LogEntry>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading logs: {ex.Message}");
        }

        return new List<LogEntry>();
    }

    // Sync version for compatibility
    public void Log(string message)
    {
        LogAsync(message).GetAwaiter().GetResult();
    }
}