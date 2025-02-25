using Microsoft.AspNetCore.Components;
using NewwaysAdmin.SharedModels.Sales;
using NewwaysAdmin.SharedModels.Config;
using System.Collections.Immutable;

namespace NewwaysAdmin.WebAdmin.Components.Features.Sales.Daily;

public partial class DailyView : ComponentBase
{
    [Parameter] public ProcessorConfig Config { get; set; } = null!;
    [Inject] private SalesDataProvider SalesProvider { get; set; } = null!;

    private DailySalesData? _salesData;
    private DateTime _selectedDate = DateTime.Today;
    private Dictionary<string, Dictionary<string, int>> _platformTotals = new();
    private bool _isLoading;
    private ImmutableDictionary<string, List<(string SkuId, SkuConfig Config)>> _productGroups = null!;

    protected override async Task OnInitializedAsync()
    {
        _productGroups = GroupSkusByProduct();
        await LoadSalesDataAsync();
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
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task OnDateChanged(DateTime newDate)
    {
        _selectedDate = newDate;
        await LoadSalesDataAsync();
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
    private int GetTotalReturns(string product)
    {
        // This will be implemented later when returns functionality is added
        // For now, return 0 as placeholder
        return 0;
    }

    private int GetReturnsForSku(string platform, string sku)
    {
        // This will be implemented later when returns functionality is added
        // For now, return 0 as placeholder
        return 0;
    }

    private int GetTotalReturnsForSku(string sku)
    {
        // This will be implemented later when returns functionality is added
        // For now, return 0 as placeholder
        return 0;
    }
}