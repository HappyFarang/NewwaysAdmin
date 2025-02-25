using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.Config;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using System;
using System.Collections.Generic;

namespace NewwaysAdmin.WebAdmin.Models.Sales
{
    public class SalesData
    {
        public DateTime Date { get; set; }
        public Dictionary<string, Dictionary<string, int>> PlatformSales { get; set; } = new();
    }

    public class SalesSummary
    {
        public Dictionary<string, Dictionary<string, int>> SkusByPlatform { get; set; } = new();
        public Dictionary<string, int> TotalsByPlatform { get; set; } = new();
        public Dictionary<string, int> TotalsBySku { get; set; } = new();
        public int GrandTotal { get; set; }
        public int TotalBags { get; set; }  // Calculated using packSize
    }

    public class SalesDataProvider
    {
        private readonly IDataStorage<Dictionary<DateTime, Dictionary<string, Dictionary<string, int>>>> _storage;
        private readonly ProcessorConfig _config;

        public SalesDataProvider(StorageManager storageManager, ProcessorConfig config)
        {
            _storage = storageManager.GetStorage<Dictionary<DateTime, Dictionary<string, Dictionary<string, int>>>>("Sales");
            _config = config;
        }

        public async Task<List<SalesData>> GetSalesDataAsync(DateTime startDate, DateTime endDate)
        {
            var allData = await _storage.LoadAsync("sales_data") ?? new();

            return allData
                .Where(kvp => kvp.Key >= startDate && kvp.Key <= endDate)
                .Select(kvp => new SalesData
                {
                    Date = kvp.Key,
                    PlatformSales = kvp.Value
                })
                .OrderBy(d => d.Date)
                .ToList();
        }

        public async Task<SalesSummary> GetSalesSummaryAsync(List<SalesData> salesData)
        {
            return await Task.Run(() =>
            {
                var summary = new SalesSummary();
                foreach (var data in salesData)
                {
                    foreach (var (platform, skuData) in data.PlatformSales)
                    {
                        if (!summary.SkusByPlatform.ContainsKey(platform))
                        {
                            summary.SkusByPlatform[platform] = new Dictionary<string, int>();
                            summary.TotalsByPlatform[platform] = 0;
                        }

                        foreach (var (sku, quantity) in skuData)
                        {
                            // Update SKU totals by platform
                            if (!summary.SkusByPlatform[platform].ContainsKey(sku))
                                summary.SkusByPlatform[platform][sku] = 0;
                            summary.SkusByPlatform[platform][sku] += quantity;

                            // Update total by platform
                            summary.TotalsByPlatform[platform] += quantity;

                            // Update total by SKU
                            if (!summary.TotalsBySku.ContainsKey(sku))
                                summary.TotalsBySku[sku] = 0;
                            summary.TotalsBySku[sku] += quantity;

                            // Update grand total
                            summary.GrandTotal += quantity;

                            // Calculate total bags using packSize
                            var packSize = _config.Platforms.Values
                                .FirstOrDefault(p => p.Skus.ContainsKey(sku))
                                ?.Skus[sku].PackSize ?? 1;
                            summary.TotalBags += quantity * packSize;
                        }
                    }
                }

                return summary;
            });
        }
    }
}