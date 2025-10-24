// File: NewwaysAdmin.SignalR.Universal/Services/ConnectionManager.cs
using NewwaysAdmin.SignalR.Universal.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace NewwaysAdmin.SignalR.Universal.Services
{
    /// <summary>
    /// Manages active connections and app registrations
    /// Thread-safe for concurrent access from multiple SignalR connections
    /// </summary>
    public class ConnectionManager
    {
        private readonly ILogger<ConnectionManager> _logger;
        private readonly ConcurrentDictionary<string, AppConnection> _connections = new();
        private readonly ConcurrentDictionary<string, List<string>> _appConnections = new();
        private readonly object _lockObject = new();

        public ConnectionManager(ILogger<ConnectionManager> logger)
        {
            _logger = logger;
        }

        // ===== CONNECTION MANAGEMENT =====

        public void AddConnection(AppConnection connection)
        {
            _connections.TryAdd(connection.ConnectionId, connection);

            lock (_lockObject)
            {
                if (!_appConnections.ContainsKey(connection.AppName))
                {
                    _appConnections[connection.AppName] = new List<string>();
                }
                _appConnections[connection.AppName].Add(connection.ConnectionId);
            }

            _logger.LogInformation("Added connection: {ConnectionId} for app {AppName} v{AppVersion}",
                connection.ConnectionId, connection.AppName, connection.AppVersion);
        }

        public void RemoveConnection(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                lock (_lockObject)
                {
                    if (_appConnections.ContainsKey(connection.AppName))
                    {
                        _appConnections[connection.AppName].Remove(connectionId);
                        if (_appConnections[connection.AppName].Count == 0)
                        {
                            _appConnections.TryRemove(connection.AppName, out _);
                        }
                    }
                }

                _logger.LogInformation("Removed connection: {ConnectionId} for app {AppName}",
                    connectionId, connection.AppName);
            }
        }

        public AppConnection? GetConnection(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        public void UpdateHeartbeat(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.LastHeartbeat = DateTime.UtcNow;
                connection.Status = ConnectionStatus.Connected;
            }
        }

        // ===== APP-SPECIFIC QUERIES =====

        public List<AppConnection> GetConnectionsForApp(string appName)
        {
            var connectionIds = new List<string>();

            lock (_lockObject)
            {
                if (_appConnections.ContainsKey(appName))
                {
                    connectionIds.AddRange(_appConnections[appName]);
                }
            }

            return connectionIds
                .Select(id => _connections.TryGetValue(id, out var conn) ? conn : null)
                .Where(conn => conn != null)
                .Cast<AppConnection>()
                .ToList();
        }

        public List<string> GetConnectionIdsForApp(string appName)
        {
            lock (_lockObject)
            {
                return _appConnections.ContainsKey(appName)
                    ? new List<string>(_appConnections[appName])
                    : new List<string>();
            }
        }

        public List<AppConnection> GetConnectionsForUser(string userId)
        {
            return _connections.Values
                .Where(conn => conn.UserId == userId)
                .ToList();
        }

        // ===== STATISTICS & MONITORING =====

        public Dictionary<string, int> GetAppConnectionCounts()
        {
            lock (_lockObject)
            {
                return _appConnections.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Count
                );
            }
        }

        public List<AppConnection> GetAllConnections()
        {
            return _connections.Values.ToList();
        }

        public int GetActiveConnectionCount()
        {
            return _connections.Count;
        }

        public int GetTotalConnectionCount()
        {
            return _connections.Count;
        }

        public List<string> GetConnectedApps()
        {
            lock (_lockObject)
            {
                return _appConnections.Keys.ToList();
            }
        }

        public List<AppConnection> GetStaleConnections(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.UtcNow.Subtract(maxAge);
            return _connections.Values
                .Where(conn => conn.LastHeartbeat < cutoffTime)
                .ToList();
        }

        // ===== CLEANUP & MAINTENANCE =====

        public int CleanupStaleConnections(TimeSpan maxAge)
        {
            var staleConnections = GetStaleConnections(maxAge);
            var cleanedCount = 0;

            foreach (var staleConnection in staleConnections)
            {
                RemoveConnection(staleConnection.ConnectionId);
                cleanedCount++;
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} stale connections", cleanedCount);
            }

            return cleanedCount;
        }

        public void UpdateConnectionStatus(string connectionId, ConnectionStatus status)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.Status = status;
                _logger.LogDebug("Updated connection {ConnectionId} status to {Status}",
                    connectionId, status);
            }
        }
    }
}