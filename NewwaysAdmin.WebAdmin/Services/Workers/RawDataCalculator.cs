// File: NewwaysAdmin.WebAdmin/Services/Workers/RawDataCalculator.cs
// Purpose: Load attendance files and extract the 4 time values (bone simple)

using System.Text.Json;
using NewwaysAdmin.WorkerAttendance.Models;
using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    /// <summary>
    /// Simple service to load attendance files and extract raw time data
    /// Just file loading + time extraction, nothing else
    /// </summary>
    public class RawDataCalculator
    {
        private readonly ILogger<RawDataCalculator> _logger;
        private readonly string _attendanceFolderPath;

        public RawDataCalculator(ILogger<RawDataCalculator> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Get the WorkerAttendance folder path from configuration
            _attendanceFolderPath = configuration["WorkerAttendance:FolderPath"] ?? "Data/WorkerAttendance";
        }

        /// <summary>
        /// Load attendance file and extract the 4 time values
        /// Returns empty times if file doesn't exist or is invalid
        /// </summary>
        public async Task<RawTimeData> LoadRawTimesAsync(string attendanceFileName)
        {
            try
            {
                var filePath = Path.Combine(_attendanceFolderPath, attendanceFileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogDebug("Attendance file not found: {FileName}", attendanceFileName);
                    return CreateEmptyRawTimeData(attendanceFileName);
                }

                var jsonContent = await File.ReadAllTextAsync(filePath);
                var cycle = JsonSerializer.Deserialize<DailyWorkCycle>(jsonContent);

                if (cycle == null || !cycle.Records.Any())
                {
                    _logger.LogDebug("Empty attendance file: {FileName}", attendanceFileName);
                    return CreateEmptyRawTimeData(attendanceFileName);
                }

                // Extract the 4 times from the cycle
                var rawTimes = new RawTimeData
                {
                    AttendanceFileName = attendanceFileName
                };

                // Normal shift times
                var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
                if (normalRecords.Any())
                {
                    rawTimes.SignIn = normalRecords
                        .Where(r => r.Type == AttendanceType.CheckIn)
                        .FirstOrDefault()?.Timestamp;

                    rawTimes.SignOut = normalRecords
                        .Where(r => r.Type == AttendanceType.CheckOut)
                        .LastOrDefault()?.Timestamp;
                }

                // OT shift times
                var otRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.OT).ToList();
                if (otRecords.Any())
                {
                    rawTimes.OTSignIn = otRecords
                        .Where(r => r.Type == AttendanceType.CheckIn)
                        .FirstOrDefault()?.Timestamp;

                    rawTimes.OTSignOut = otRecords
                        .Where(r => r.Type == AttendanceType.CheckOut)
                        .LastOrDefault()?.Timestamp;
                }

                return rawTimes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading raw times for {FileName}", attendanceFileName);
                return CreateEmptyRawTimeData(attendanceFileName);
            }
        }

        /// <summary>
        /// Create empty raw time data for missing/invalid files
        /// </summary>
        private RawTimeData CreateEmptyRawTimeData(string attendanceFileName)
        {
            return new RawTimeData
            {
                AttendanceFileName = attendanceFileName,
                SignIn = null,
                SignOut = null,
                OTSignIn = null,
                OTSignOut = null
            };
        }
    }
}