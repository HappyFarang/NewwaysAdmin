// File: NewwaysAdmin.Mobile/Services/BankSlip/ProcessedSlipsTracker.cs
// Tracks which bank slips have been uploaded to avoid duplicates

using System.Text.Json;
using Microsoft.Maui.Storage;

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    /// <summary>
    /// Tracks which bank slips have been uploaded to avoid duplicates
    /// </summary>
    public class ProcessedSlipsTracker
    {
        private readonly string _trackingFilePath;
        private HashSet<string> _processedFiles;

        public ProcessedSlipsTracker()
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "BankSlipSync");
            Directory.CreateDirectory(folder);
            _trackingFilePath = Path.Combine(folder, "processed_slips.json");
            _processedFiles = LoadProcessedFiles();
        }

        public bool IsAlreadyProcessed(string filePath)
        {
            var key = GetFileKey(filePath);
            return _processedFiles.Contains(key);
        }

        public void MarkAsProcessed(string filePath)
        {
            var key = GetFileKey(filePath);
            _processedFiles.Add(key);
            SaveProcessedFiles();
        }

        public int ProcessedCount => _processedFiles.Count;

        private string GetFileKey(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                return $"{fileName}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return filePath;
            }
        }

        private HashSet<string> LoadProcessedFiles()
        {
            try
            {
                if (File.Exists(_trackingFilePath))
                {
                    var json = File.ReadAllText(_trackingFilePath);
                    return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
                }
            }
            catch { }
            return new HashSet<string>();
        }

        private void SaveProcessedFiles()
        {
            try
            {
                var json = JsonSerializer.Serialize(_processedFiles);
                File.WriteAllText(_trackingFilePath, json);
            }
            catch { }
        }

        public void CleanOldEntries(int keepCount = 500)
        {
            if (_processedFiles.Count > keepCount)
            {
                _processedFiles = _processedFiles.TakeLast(keepCount).ToHashSet();
                SaveProcessedFiles();
            }
        }
    }
}