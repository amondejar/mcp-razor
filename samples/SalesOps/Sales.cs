namespace SalesOps;

public sealed record MonthTotal(int Month, decimal Revenue, int Orders, int Units, bool HasData);

public sealed record SalesKpis(decimal Revenue, int Orders, decimal AverageOrder, decimal? RevenueChange, decimal? OrdersChange);

public sealed record ProductSales(string Sku, string Name, string Category, int Units, decimal Revenue);

public sealed record RegionSales(string Region, decimal Revenue, int Orders);

/// <summary>
/// Two years of sales by month, region and product. The numbers are generated, but always the same:
/// seasonality by category, a regional mix, growth in 2026 and a little noise.
/// </summary>
public sealed class SalesService
{
    public static readonly IReadOnlyList<string> Regions = ["North", "South", "East", "West"];
    public static readonly IReadOnlyList<int> Years = [2025, 2026];

    /// <summary>The last month with data in the current year.</summary>
    public const int LastMonthOf2026 = 10;

    private const decimal UnitsPerOrder = 1.6m;

    private static readonly Dictionary<string, double[]> Seasonality = new()
    {
        ["Outdoor"] = [0.55, 0.6, 0.8, 1.05, 1.3, 1.55, 1.7, 1.5, 1.1, 0.8, 0.7, 0.95],
        ["Kitchen"] = [0.95, 0.85, 0.9, 0.9, 0.95, 0.9, 0.85, 0.9, 1.0, 1.05, 1.35, 1.7],
        ["Garden"] = [0.3, 0.45, 1.0, 1.6, 1.85, 1.6, 1.3, 1.05, 0.8, 0.55, 0.35, 0.3],
        ["Electronics"] = [0.9, 0.8, 0.85, 0.85, 0.9, 0.9, 0.95, 1.0, 1.0, 1.05, 1.6, 2.0],
        ["Toys"] = [0.6, 0.6, 0.65, 0.7, 0.7, 0.75, 0.8, 0.8, 0.85, 1.0, 1.8, 2.6],
    };

    private static readonly Dictionary<string, double> RegionShare = new()
    {
        ["North"] = 0.31, ["South"] = 0.24, ["East"] = 0.27, ["West"] = 0.18,
    };

    // [year index][month 1..12][region][product] → units
    private readonly int[][][][] _units;
    private readonly IReadOnlyList<Product> _products;

    public SalesService(Catalog catalog)
    {
        _products = catalog.Products;
        _units = new int[Years.Count][][][];
        for (var y = 0; y < Years.Count; y++)
        {
            _units[y] = new int[13][][];
            for (var month = 1; month <= 12; month++)
            {
                _units[y][month] = new int[Regions.Count][];
                for (var r = 0; r < Regions.Count; r++)
                {
                    _units[y][month][r] = new int[_products.Count];
                    if (!HasData(Years[y], month))
                    {
                        continue;
                    }

                    for (var p = 0; p < _products.Count; p++)
                    {
                        _units[y][month][r][p] = Units(y, month, r, p);
                    }
                }
            }
        }
    }

    public static bool HasData(int year, int month) => year < 2026 || month <= LastMonthOf2026;

    public IReadOnlyList<MonthTotal> Monthly(int year, string? region)
    {
        var y = YearIndex(year);
        return Enumerable.Range(1, 12).Select(month =>
        {
            var (revenue, units) = Sum(y, month, region, category: null);
            return new MonthTotal(month, revenue, Orders(units), units, HasData(year, month));
        }).ToList();
    }

    public SalesKpis Kpis(int year, string? region)
    {
        var months = Enumerable.Range(1, 12).Where(m => HasData(year, m)).ToList();
        var current = Totals(year, region, months);

        // Same months of the previous year, so a partial year compares fairly.
        decimal? revenueChange = null, ordersChange = null;
        if (Years.Contains(year - 1))
        {
            var previous = Totals(year - 1, region, months);
            revenueChange = Change(current.Revenue, previous.Revenue);
            ordersChange = Change(current.Orders, previous.Orders);
        }

        var average = current.Orders == 0 ? 0 : Math.Round(current.Revenue / current.Orders, 2);
        return new SalesKpis(current.Revenue, current.Orders, average, revenueChange, ordersChange);
    }

    public IReadOnlyList<ProductSales> TopProducts(int year, int month, string? region, int count)
    {
        var y = YearIndex(year);
        return _products
            .Select((product, p) =>
            {
                var units = RegionIndexes(region).Sum(r => _units[y][month][r][p]);
                return new ProductSales(product.Sku, product.Name, product.Category, units, units * product.ListPrice);
            })
            .Where(sales => sales.Units > 0)
            .OrderByDescending(sales => sales.Revenue)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<RegionSales> ByRegion(int year, int month)
    {
        var y = YearIndex(year);
        return Regions.Select(region =>
        {
            var (revenue, units) = Sum(y, month, region, category: null);
            return new RegionSales(region, revenue, Orders(units));
        }).ToList();
    }

    /// <summary>Average monthly units of a product over the last three months with data.</summary>
    public int AverageMonthlyUnits(string sku)
    {
        var p = _products.ToList().FindIndex(product => product.Sku == sku);
        if (p < 0)
        {
            return 0;
        }

        var y = YearIndex(2026);
        var months = Enumerable.Range(LastMonthOf2026 - 2, 3);
        return (int)Math.Round(months.Average(month => Enumerable.Range(0, Regions.Count).Sum(r => _units[y][month][r][p])));
    }

    public static string? NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) || region.Trim().Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : Regions.FirstOrDefault(r => r.Equals(region.Trim(), StringComparison.OrdinalIgnoreCase));

    private (decimal Revenue, int Orders) Totals(int year, string? region, IEnumerable<int> months)
    {
        var y = YearIndex(year);
        decimal revenue = 0;
        var units = 0;
        foreach (var month in months)
        {
            var (monthRevenue, monthUnits) = Sum(y, month, region, category: null);
            revenue += monthRevenue;
            units += monthUnits;
        }

        return (revenue, Orders(units));
    }

    private (decimal Revenue, int Units) Sum(int y, int month, string? region, string? category)
    {
        decimal revenue = 0;
        var units = 0;
        foreach (var r in RegionIndexes(region))
        {
            for (var p = 0; p < _products.Count; p++)
            {
                if (category is not null && _products[p].Category != category)
                {
                    continue;
                }

                var u = _units[y][month][r][p];
                units += u;
                revenue += u * _products[p].ListPrice;
            }
        }

        return (revenue, units);
    }

    private static IEnumerable<int> RegionIndexes(string? region) =>
        region is null ? Enumerable.Range(0, Regions.Count) : [Regions.ToList().IndexOf(region)];

    private static int YearIndex(int year)
    {
        var index = Years.ToList().IndexOf(year);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(year), $"No sales data for {year}.");
    }

    private static int Orders(int units) => (int)Math.Round(units / UnitsPerOrder);

    private static decimal? Change(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / previous * 100, 1);

    private int Units(int y, int month, int r, int p)
    {
        var product = _products[p];
        var basePerMonth = 1400.0 / (double)product.ListPrice + 6;       // cheaper products sell more
        var season = Seasonality[product.Category][month - 1];
        var growth = y == 0 ? 1.0 : 1.12 + (product.Category == "Garden" ? -0.18 : 0); // 2026: +12 %, garden down
        var dip = y == 1 && month == 6 && Regions[r] == "South" ? 0.62 : 1.0;           // a heatwave in the south
        var noise = 0.85 + Noise(y, month, r, p) * 0.3;
        return (int)Math.Round(basePerMonth * season * growth * dip * noise * RegionShare[Regions[r]] * 4);
    }

    // Deterministic noise in [0, 1): string.GetHashCode is randomized per process, so mix integers instead.
    private static double Noise(int y, int month, int r, int p)
    {
        var x = (uint)(y * 7919 + month * 104729 + r * 1299709 + p * 15485863);
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x / (double)uint.MaxValue;
    }
}
