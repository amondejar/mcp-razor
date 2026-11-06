using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace SalesOps;

/// <summary>What sales_dashboard returns: the filters the panel opens with and the headline numbers.</summary>
public sealed record SalesDashboardData(int Year, string Region, SalesKpis Kpis, IReadOnlyList<MonthTotal> Months);

[McpServerToolType]
public sealed class SalesTools(SalesService sales, PriceChangeService priceChanges)
{
    [McpServerTool(Name = "sales_dashboard", ReadOnly = true, UseStructuredContent = true)]
    [McpAppUi(ResourceUri = "ui://sales/dashboard")]
    [Description("Opens an interactive sales dashboard: revenue and orders by month, filters by region and year, and a breakdown of any month the user clicks. Also returns the headline numbers.")]
    public SalesDashboardData SalesDashboard(
        [Description("Year: 2025 or 2026. 2026 has data until October.")] int year = 2026,
        [Description("Region: All, North, South, East or West.")] string region = "All")
    {
        if (!SalesService.Years.Contains(year))
        {
            throw new McpException($"There is no sales data for {year}. Use 2025 or 2026.");
        }

        var normalized = SalesService.NormalizeRegion(region);
        if (normalized is null && !string.Equals(region?.Trim(), "All", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Unknown region '{region}'. Use All, North, South, East or West.");
        }

        return new SalesDashboardData(year, normalized ?? "All", sales.Kpis(year, normalized), sales.Monthly(year, normalized));
    }

    [McpServerTool(Name = "propose_price_changes", UseStructuredContent = true)]
    [McpAppUi(ResourceUri = "ui://pricing/review")]
    [Description("Proposes a price change for every product of a category and opens a review panel. Nothing changes until the user selects rows and applies them in the panel; the model cannot apply the change itself. Categories: Outdoor, Kitchen, Garden, Electronics, Toys.")]
    public PriceChangeSet ProposePriceChanges(
        [Description("Product category: Outdoor, Kitchen, Garden, Electronics or Toys.")] string category,
        [Description("Percentage change, between -50 and 50. For example 8 raises prices 8 %.")] decimal percent,
        [Description("Why the change is proposed, shown to the user.")] string reason)
    {
        try
        {
            return priceChanges.Propose(category, percent, reason);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }
}
