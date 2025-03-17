using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.WebAdmin.Services.Sales
{
    public class CourierTrackingData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int TodayCount { get; set; }
        public int CarryoverCount { get; set; }
        public int TotalCount => TodayCount + CarryoverCount;
        public DateTime LastReset { get; set; }
    }

    public class DailyCourierTracking
    {
        public DateTime Date { get; set; }
        public List<CourierTrackingData> Couriers { get; set; } = new();
    }

    public interface ICourierTrackingService
    {
        Task<List<CourierTrackingData>> GetCourierDataAsync(DateTime date);
        Task ResetCourierAsync(string courierId, DateTime date);
        Task UpdateCourierCountAsync(string courierId, int additionalCount, DateTime date);
        Task<Dictionary<string, CourierTrackingData>> GetCourierDictionaryAsync(DateTime date);
    }

    public class CourierTrackingService : ICourierTrackingService
    {
        private readonly ILogger<CourierTrackingService> _logger;
        private readonly IOManager _ioManager;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<DailyCourierTracking>? _storage;

        public CourierTrackingService(ILogger<CourierTrackingService> logger, IOManager ioManager)
        {
            _logger = logger;
            _ioManager = ioManager;
        }

        private async Task InitializeStorageAsync()
        {
            if (_storage != null) return;

            await _initLock.WaitAsync();
            try
            {
                if (_storage == null)
                {
                    _storage = await _ioManager.GetStorageAsync<DailyCourierTracking>("CourierTracking");
                    _logger.LogInformation("Initialized courier tracking storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize courier tracking storage");
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        // Get courier data for a specific date
        public async Task<List<CourierTrackingData>> GetCourierDataAsync(DateTime date)
        {
            try
            {
                await InitializeStorageAsync();

                var dateKey = date.ToString("yyyy-MM-dd");

                if (await _storage!.ExistsAsync(dateKey))
                {
                    var dailyData = await _storage.LoadAsync(dateKey);
                    return dailyData.Couriers;
                }

                // Check if we need to carry over from previous day
                var yesterday = date.AddDays(-1);
                var yesterdayKey = yesterday.ToString("yyyy-MM-dd");

                if (await _storage!.ExistsAsync(yesterdayKey))
                {
                    var yesterdayData = await _storage.LoadAsync(yesterdayKey);

                    // Create new data with carryover
                    var newData = new List<CourierTrackingData>();

                    foreach (var courier in yesterdayData.Couriers)
                    {
                        newData.Add(new CourierTrackingData
                        {
                            Id = courier.Id,
                            Name = courier.Name,
                            TodayCount = 0,
                            CarryoverCount = courier.TotalCount, // Yesterday's total becomes today's carryover
                            LastReset = DateTime.Now
                        });
                    }

                    // Save this new data
                    await SaveCourierDataAsync(date, newData);
                    return newData;
                }

                // If no data for yesterday, create default couriers
                var defaultCouriers = CreateDefaultCouriers();
                await SaveCourierDataAsync(date, defaultCouriers);
                return defaultCouriers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting courier data for {Date}", date);
                return new List<CourierTrackingData>();
            }
        }

        public async Task<Dictionary<string, CourierTrackingData>> GetCourierDictionaryAsync(DateTime date)
        {
            var courierList = await GetCourierDataAsync(date);
            var courierDict = new Dictionary<string, CourierTrackingData>();

            foreach (var courier in courierList)
            {
                courierDict[courier.Name] = courier;
            }

            return courierDict;
        }

        // Reset a courier's count (move today's count to carryover)
        public async Task ResetCourierAsync(string courierId, DateTime date)
        {
            try
            {
                await InitializeStorageAsync();

                var dateKey = date.ToString("yyyy-MM-dd");

                if (await _storage!.ExistsAsync(dateKey))
                {
                    var dailyData = await _storage.LoadAsync(dateKey);
                    var courier = dailyData.Couriers.Find(c => c.Id == courierId);

                    if (courier != null)
                    {
                        // Add today's count to carryover
                        courier.CarryoverCount += courier.TodayCount;
                        courier.TodayCount = 0;
                        courier.LastReset = DateTime.Now;

                        await SaveCourierDataAsync(date, dailyData.Couriers);

                        _logger.LogInformation("Reset courier {CourierId} count. New carryover: {Carryover}",
                            courierId, courier.CarryoverCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting courier {CourierId} on {Date}", courierId, date);
                throw;
            }
        }

        // Update a courier's count
        public async Task UpdateCourierCountAsync(string courierId, int additionalCount, DateTime date)
        {
            if (additionalCount == 0) return;

            try
            {
                await InitializeStorageAsync();

                var dateKey = date.ToString("yyyy-MM-dd");
                List<CourierTrackingData> couriers;

                if (await _storage!.ExistsAsync(dateKey))
                {
                    var dailyData = await _storage.LoadAsync(dateKey);
                    couriers = dailyData.Couriers;

                    var courier = couriers.Find(c => c.Id == courierId);
                    if (courier != null)
                    {
                        courier.TodayCount += additionalCount;
                        _logger.LogInformation("Updated courier {CourierId} count by {Count}. New total: {Total}",
                            courierId, additionalCount, courier.TodayCount);
                    }
                    else
                    {
                        // Add new courier
                        var newCourier = CreateCourier(courierId);
                        newCourier.TodayCount = additionalCount;
                        couriers.Add(newCourier);

                        _logger.LogInformation("Added new courier {CourierId} with count {Count}",
                            courierId, additionalCount);
                    }
                }
                else
                {
                    // Create new tracking for today
                    couriers = CreateDefaultCouriers();
                    var courier = couriers.Find(c => c.Id == courierId);

                    if (courier != null)
                    {
                        courier.TodayCount = additionalCount;
                    }
                    else
                    {
                        var newCourier = CreateCourier(courierId);
                        newCourier.TodayCount = additionalCount;
                        couriers.Add(newCourier);
                    }

                    _logger.LogInformation("Created new tracking for {Date} with courier {CourierId} count {Count}",
                        date, courierId, additionalCount);
                }

                await SaveCourierDataAsync(date, couriers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating courier {CourierId} count by {Count} on {Date}",
                    courierId, additionalCount, date);
                throw;
            }
        }

        // Private helper methods
        private async Task SaveCourierDataAsync(DateTime date, List<CourierTrackingData> couriers)
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var dailyData = new DailyCourierTracking
            {
                Date = date,
                Couriers = couriers
            };

            await _storage!.SaveAsync(dateKey, dailyData);
        }

        private List<CourierTrackingData> CreateDefaultCouriers()
        {
            return new List<CourierTrackingData>
            {
                CreateCourier("flash"),
                CreateCourier("jnt"),
                CreateCourier("ninja")
            };
        }

        private CourierTrackingData CreateCourier(string id)
        {
            var name = id switch
            {
                "flash" => "Flash",
                "jnt" => "J&T",
                "ninja" => "Ninja",
                _ => id // Use ID as name if no mapping exists
            };

            return new CourierTrackingData
            {
                Id = id,
                Name = name,
                TodayCount = 0,
                CarryoverCount = 0,
                LastReset = DateTime.Now
            };
        }
    }
}