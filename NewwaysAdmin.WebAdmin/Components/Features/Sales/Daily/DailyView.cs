using Microsoft.AspNetCore.Components;
using NewwaysAdmin.SharedModels.Sales;
using NewwaysAdmin.SharedModels.Config;
using System.Collections.Immutable;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.OrderProcessor;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.WebAdmin.Components.Features.Sales.Daily;

public partial class DailyView : ComponentBase
{
    [Parameter] public ProcessorConfig Config { get; set; } = null!;
    [Inject] private SalesDataProvider SalesProvider { get; set; } = null!;
    [Inject] private ILogger<DailyView> Logger { get; set; } = null!;
    [Inject] private IOManager IOManager { get; set; } = null!;

    private DailySalesData? _salesData;
    private DateTime _selectedDate = DateTime.Today;
    private Dictionary<string, Dictionary<string, int>> _platformTotals = new();
    private bool _isLoading;
    private ImmutableDictionary<string, List<(string SkuId, SkuConfig Config)>> _productGroups = null!;

    // New fields for enhanced display
    private int _standardOrderCount = 0;
    private int _unusualOrderCount = 0;
    private Dictionary<string, int> _standardOrderSkus = new();
    private List<UnusualOrder> _unusualOrders = new();

    // To store the latest scan result for display
    private ScanResult? _latestScan;

    // Courier tracking with carryover
    private Dictionary<string, CourierTrackingData> _courierTracking = new();

    // Class to represent unusual orders (multiple items or quantities)
    private class UnusualOrder
    {
        public string? OrderNumber { get; set; }
        public string Platform { get; set; } = string.Empty;
        public Dictionary<string, int> SkuCounts { get; set; } = new();
        public string? Courier { get; set; }
    }

    // Class to track courier data
    private class CourierTrackingData
    {
        public int TodayCount { get; set; }
        public int CarryoverCount { get; set; }
        public int TotalCount => TodayCount + CarryoverCount;
        public DateTime LastReset { get; set; }
    }

    protected override async Task OnInitializedAsync()
    {
        _productGroups = GroupSkusByProduct();
        await LoadSalesDataAsync();
        await LoadCourierTrackingAsync();
    }

    private async Task LoadSalesDataAsync()
    {
        try
        {
            _isLoading = true;
            Logger.LogInformation("Loading sales data for {Date}", _selectedDate);

            // Get storage for scan results
            var scanStorage = await IOManager.GetStorageAsync<ScanResult>("PdfProcessor_Scans");

            // List all scan identifiers
            var scanIds = await scanStorage.ListIdentifiersAsync();
            Logger.LogInformation("Found {Count} scan identifiers", scanIds.Count());

            // Process scans for the selected date
            List<ScanResult> todayScans = new();
            foreach (var id in scanIds)
            {
                try
                {
                    var scan = await scanStorage.LoadAsync(id);
                    if (scan != null && scan.ScanTime.Date == _selectedDate.Date)
                    {
                        Logger.LogInformation("Found scan for today: {Id} with {Count} SKUs",
                            id, scan.SkuCounts.Count);
                        todayScans.Add(scan);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error loading scan {Id}", id);
                }
            }

            // Sort scans by time, most recent first
            todayScans = todayScans.OrderByDescending(s => s.ScanTime).ToList();

            // Save the latest scan for display in the UI
            _latestScan = todayScans.FirstOrDefault();

            // No need to process sales data further - we'll use the scan data directly
            // The courier data is already in _latestScan.CourierCounts
            // Unusual orders are in _latestScan.UnusualOrders

            // Update courier tracking based on scan data
            if (_latestScan?.CourierCounts != null)
            {
                foreach (var courier in _latestScan.CourierCounts)
                {
                    if (!_courierTracking.ContainsKey(courier.Key))
                    {
                        _courierTracking[courier.Key] = new CourierTrackingData
                        {
                            TodayCount = courier.Value,
                            CarryoverCount = 0,
                            LastReset = DateTime.Now
                        };
                    }
                    else
                    {
                        _courierTracking[courier.Key].TodayCount = courier.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading sales data for {Date}", _selectedDate);
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }


    private async Task LoadCourierTrackingAsync()
    {
        // In a real implementation, this would load from persistent storage
        // For now, we'll set up some sample data
        _courierTracking = new Dictionary<string, CourierTrackingData>
        {
            { "Flash", new CourierTrackingData { TodayCount = 15, CarryoverCount = 12, LastReset = DateTime.Now.AddDays(-1) } },
            { "J&T", new CourierTrackingData { TodayCount = 20, CarryoverCount = 5, LastReset = DateTime.Now.AddDays(-1) } },
            { "Ninja", new CourierTrackingData { TodayCount = 7, CarryoverCount = 0, LastReset = DateTime.Now } }
        };

        await Task.CompletedTask; // Placeholder for actual async operation
    }

    private void ProcessSalesData()
    {
        if (_salesData == null || _salesData.Sales == null) return;

        _standardOrderSkus.Clear();
        _unusualOrders.Clear();
        _standardOrderCount = 0;
        _unusualOrderCount = 0;

        // Group sales by platform and SKU since SaleEntry doesn't have OrderNumber
        // We'll consider each entry as its own "order" for simplicity
        foreach (var sale in _salesData.Sales)
        {
            // Check if this is an unusual order (quantity > 1)
            bool isUnusual = sale.Quantity > 1;

            if (isUnusual)
            {
                // This is an unusual order (quantity > 1)
                var skuCounts = new Dictionary<string, int>
                {
                    { sale.Sku, sale.Quantity }
                };

                _unusualOrders.Add(new UnusualOrder
                {
                    // We don't have an order number, so use timestamp as a substitute identifier
                    OrderNumber = sale.Timestamp.ToString("yyyyMMdd-HHmmss"),
                    Platform = sale.Platform,
                    SkuCounts = skuCounts,
                    // There's no Courier property in SaleEntry
                    Courier = "Unknown"
                });

                _unusualOrderCount++;
            }
            else
            {
                // This is a standard order with quantity = 1
                string sku = sale.Sku;

                if (!_standardOrderSkus.ContainsKey(sku))
                    _standardOrderSkus[sku] = 0;

                _standardOrderSkus[sku]++;
                _standardOrderCount++;
            }
        }
    }

    private async Task OnDateChanged(DateTime newDate)
    {
        _selectedDate = newDate;
        await LoadSalesDataAsync();
        await LoadCourierTrackingAsync();
    }

    private ImmutableDictionary<string, List<(string, SkuConfig)>> GroupSkusByProduct()
    {
        if (Config?.Platforms == null) return ImmutableDictionary<string, List<(string, SkuConfig)>>.Empty;
        var groups = new Dictionary<string, List<(string, SkuConfig)>>();

        foreach (var platform in Config.Platforms)
        {
            foreach (var sku in platform.Value.Skus)
            {
                var product = sku.Value.ProductName;
                if (!groups.ContainsKey(product))
                {
                    groups[product] = new List<(string, SkuConfig)>();
                }
                groups[product].Add((sku.Key, sku.Value));
            }
        }

        return groups.ToImmutableDictionary();
    }

    private int GetQuantity(string platform, string sku)
    {
        if (_platformTotals.TryGetValue(platform, out var skus))
        {
            if (skus.TryGetValue(sku, out var quantity))
            {
                return quantity;
            }
        }
        return 0;
    }

    private int GetTotalForSku(string sku)
    {
        if (_latestScan?.SkuCounts == null)
            return 0;

        return _latestScan.SkuCounts.TryGetValue(sku, out var count) ? count : 0;
    }

    private int GetTotalOrders(string product)
    {
        if (_salesData?.Sales == null) return 0;

        return _salesData.Sales
            .Where(s => GetSkusForProduct(product).Any(p => p.SkuId == s.Sku))
            .Sum(s => s.Quantity);
    }

    private int GetTotalItemsSold(string product)
    {
        if (_latestScan?.SkuCounts == null)
            return 0;

        var total = 0;
        var skus = GetUniqueSkusForProduct(product);

        foreach (var sku in skus)
        {
            if (_latestScan.SkuCounts.TryGetValue(sku.SkuId, out var count))
            {
                total += count * sku.Config.PackSize;
            }
        }

        return total;
    }

    public IEnumerable<string> GetUniqueProducts() => _productGroups.Keys;

    public IEnumerable<(string SkuId, SkuConfig Config)> GetSkusForProduct(string product)
        => _productGroups.GetValueOrDefault(product) ?? Enumerable.Empty<(string, SkuConfig)>();

    public HashSet<(string SkuId, SkuConfig Config)> GetUniqueSkusForProduct(string product)
    {
        return GetSkusForProduct(product)
            .GroupBy(x => x.SkuId)
            .Select(g => g.First())
            .ToHashSet();
    }

    private int GetTotalOrders()
    {
        return _salesData?.Sales?.Count ?? 0;
    }

    private string CreateScanId()
    {
        if (_latestScan is not null)
        {
            return _latestScan.Id;
        }

        return "[No Data]";
    }

    private string GetPlatformName()
    {
        if (_latestScan is not null)
        {
            return _latestScan.Platform;
        }

        if (_salesData?.Sales != null && _salesData.Sales.Count > 0)
        {
            // Get most common platform
            return _salesData.Sales
                .GroupBy(s => s.Platform)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
        }

        return "UNKNOWN";
    }

    private void ResetCourierCount(string courier)
    {
        if (_courierTracking.TryGetValue(courier, out var data))
        {
            // Add today's count to carryover and reset today's count
            data.CarryoverCount += data.TodayCount;
            data.TodayCount = 0;
            data.LastReset = DateTime.Now;

            Logger.LogInformation("Reset courier count for {Courier}. New carryover: {Carryover}",
                courier, data.CarryoverCount);

            // In real implementation, save this data to persistent storage

            StateHasChanged();
        }
    }
}