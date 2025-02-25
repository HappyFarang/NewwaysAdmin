using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Binary;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.SharedModels.Sales
{
    [MessagePackObject]
    public class DailySalesData
    {
        [Key(0)]
        public List<SaleEntry> Sales { get; set; } = new();

        public string GetFormula(string platform, string sku)
        {
            var quantities = Sales
                .Where(s => s.Platform == platform && s.Sku == sku)
                .Select(s => s.Quantity.ToString());

            return string.Join("+", quantities);
        }

        public int GetTotal(string platform, string sku)
        {
            return Sales
                .Where(s => s.Platform == platform && s.Sku == sku)
                .Sum(s => s.Quantity);
        }

        public Dictionary<string, Dictionary<string, int>> GetPlatformTotals()
        {
            return Sales
                .GroupBy(s => s.Platform)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(s => s.Sku)
                          .ToDictionary(
                              sg => sg.Key,
                              sg => sg.Sum(s => s.Quantity)
                          )
                );
        }
    }

    [MessagePackObject]
    public class SaleEntry
    {
        [Key(0)]
        public required string Platform { get; set; }

        [Key(1)]
        public required string Sku { get; set; }

        [Key(2)]
        public int Quantity { get; set; }

        [Key(3)]
        public DateTime Timestamp { get; set; }
    }

    public class SalesDataProvider
    {
        private readonly IDataStorage<DailySalesData> _storage;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SalesDataProvider(EnhancedStorageFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _storage = factory.GetStorage<DailySalesData>("Sales")
                ?? throw new InvalidOperationException("Failed to create storage");
        }

        private static string GetIdentifier(DateTime date) => date.ToString("yyyy-MM-dd");

        public async Task<DailySalesData> GetDailySalesAsync(DateTime date)
        {
            var identifier = GetIdentifier(date);

            if (await _storage.ExistsAsync(identifier))
            {
                return await _storage.LoadAsync(identifier);
            }

            return new DailySalesData();
        }

        public async Task AddSalesAsync(string platform, Dictionary<string, int> skuCounts)
        {
            ArgumentNullException.ThrowIfNull(platform);
            ArgumentNullException.ThrowIfNull(skuCounts);

            var identifier = GetIdentifier(DateTime.Now.Date);

            try
            {
                await _semaphore.WaitAsync();

                var dailyData = await GetDailySalesAsync(DateTime.Now.Date);

                foreach (var (sku, count) in skuCounts)
                {
                    if (string.IsNullOrEmpty(sku)) continue;

                    dailyData.Sales.Add(new SaleEntry
                    {
                        Platform = platform,
                        Sku = sku,
                        Quantity = count,
                        Timestamp = DateTime.Now
                    });
                }

                await _storage.SaveAsync(identifier, dailyData);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<(DateTime Date, DailySalesData Data)>> GetSalesDataAsync(
            DateTime startDate,
            DateTime endDate)
        {
            var result = new List<(DateTime Date, DailySalesData Data)>();
            var current = startDate.Date;
            endDate = endDate.Date;

            while (current <= endDate)
            {
                var identifier = GetIdentifier(current);
                if (await _storage.ExistsAsync(identifier))
                {
                    var data = await _storage.LoadAsync(identifier);
                    if (data != null)
                    {
                        result.Add((Date: current, Data: data));
                    }
                }
                current = current.AddDays(1);
            }

            return result;
        }

        public async Task<List<DateTime>> GetAvailableDatesAsync()
        {
            var files = await _storage.ListIdentifiersAsync();
            var dates = new List<DateTime>();

            foreach (var file in files)
            {
                if (DateTime.TryParseExact(file, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime date))
                {
                    dates.Add(date);
                }
            }

            return dates.OrderBy(d => d).ToList();
        }
    }
}