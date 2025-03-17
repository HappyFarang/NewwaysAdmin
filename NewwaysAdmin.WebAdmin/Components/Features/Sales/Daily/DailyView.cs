using Microsoft.AspNetCore.Components;
using NewwaysAdmin.SharedModels.Sales;
using NewwaysAdmin.SharedModels.Config;
using System.Collections.Immutable;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NewwaysAdmin.WebAdmin.Components.Features.Sales.Daily;

public partial class DailyView : ComponentBase
{
    [Parameter] public ProcessorConfig Config { get; set; } = null!;
    [Inject] private SalesDataProvider SalesProvider { get; set; } = null!;
    [Inject] private ILogger<DailyView> Logger { get; set; } = null!;

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
            _salesData = await SalesProvider.GetDailySalesAsync(_selectedDate);

            if (_salesData.Sales.Count == 0)
            {
                _platformTotals = new Dictionary<string, Dictionary<string, int>>();
            }
            else
            {
                _platformTotals = _salesData.GetPlatformTotals();
            }

            ProcessSalesData();
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
        if (_salesData == null) return;

        _standardOrderSkus.Clear();
        _unusualOrders.Clear();
        _standardOrderCount = 0;
        _unusualOrderCount = 0;

        // Group sales by order number to identify orders with multiple items
        var orderGroups = _salesData.Sales
            .GroupBy(s => new { s.Platform, s.OrderNumber })
            .Select(g => new
            {
                Platform = g.Key.Platform,
                OrderNumber = g.Key.OrderNumber,
                Items = g.ToList(),
                TotalQuantity = g.Sum(s => s.Quantity)
            })
            .ToList();

        foreach (var order in orderGroups)
        {
            // Check if this is an unusual order:
            // 1. More than one item type in the order, or
            // 2. Any item has quantity > 1
            bool isUnusual = order.Items.Count > 1 || order.Items.Any(i => i.Quantity > 1);

            if (isUnusual)
            {
                // This is an unusual order
                var skuCounts = order.Items
                    .GroupBy(i => i.Sku)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

                _unusualOrders.Add(new UnusualOrder
                {
                    OrderNumber = order.OrderNumber,
                    Platform = order.Platform,
                    SkuCounts = skuCounts,
                    Courier = order.Items.FirstOrDefault()?.Courier
                });

                _unusualOrderCount++;
            }
            else
            {
                // This is a standard order with exactly one item with quantity 1
                string sku = order.Items.First().Sku;

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
        var total = 0;
        foreach (var platform in _platformTotals.Values)
        {
            if (platform.TryGetValue(sku, out var quantity))
            {
                total += quantity;
            }
        }
        return total;
    }

    private int GetSampleQuantity(string platform, string product) => 0; // Implementation pending

    private int GetTotalSamples(string product) => 0; // Implementation pending

    private int GetTotalOrders(string product)
    {
        if (_salesData?.Sales == null) return 0;

        return _salesData.Sales
            .Where(s => GetSkusForProduct(product).Any(p => p.SkuId == s.Sku))
            .Sum(s => s.Quantity);
    }

    private int GetTotalItemsSold(string product)
    {
        var total = 0;
        var skus = GetUniqueSkusForProduct(product);

        foreach (var sku in skus)
        {
            var quantity = GetTotalForSku(sku.SkuId);
            total += quantity * sku.Config.PackSize;
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
        return _salesData?.Sales.Count ?? 0;
    }

    private string CreateScanId()
    {
        if (_salesData == null || _salesData.Sales.Count == 0)
            return "[No Data]";

        string date = _selectedDate.ToString("yyyyMMdd");
        string time = DateTime.Now.ToString("HHmmss");
        string platform = GetPlatformName();

        return $"[{date}][{time}][{platform}]";
    }

    private string GetPlatformName()
    {
        if (_salesData == null || _salesData.Sales.Count == 0)
            return "UNKNOWN";

        // Get most common platform
        return _salesData.Sales
            .GroupBy(s => s.Platform)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
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