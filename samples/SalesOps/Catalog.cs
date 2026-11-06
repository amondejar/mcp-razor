using System.Collections.Concurrent;

namespace SalesOps;

public sealed record Product(string Sku, string Name, string Category, decimal ListPrice, decimal Cost);

/// <summary>The shop's catalog. Current prices change when the user applies a price change set.</summary>
public sealed class Catalog
{
    public static readonly IReadOnlyList<string> Categories = ["Outdoor", "Kitchen", "Garden", "Electronics", "Toys"];

    private static readonly Product[] Seed =
    [
        new("OUT-101", "Trail backpack 30 L", "Outdoor", 89.99m, 41.20m),
        new("OUT-102", "Two-person tent", "Outdoor", 179.00m, 96.50m),
        new("OUT-103", "Sleeping bag -5 °C", "Outdoor", 119.00m, 58.00m),
        new("OUT-104", "Trekking poles (pair)", "Outdoor", 49.90m, 31.80m),
        new("OUT-105", "Headlamp 400 lm", "Outdoor", 34.99m, 15.10m),
        new("OUT-106", "Insulated bottle 1 L", "Outdoor", 27.50m, 19.90m),
        new("OUT-107", "Camping stove", "Outdoor", 64.00m, 30.40m),
        new("OUT-108", "Rain jacket", "Outdoor", 139.00m, 62.00m),

        new("KIT-201", "Chef's knife 20 cm", "Kitchen", 69.00m, 28.50m),
        new("KIT-202", "Cast iron skillet", "Kitchen", 54.90m, 24.10m),
        new("KIT-203", "Espresso grinder", "Kitchen", 149.00m, 88.00m),
        new("KIT-204", "Bamboo cutting board", "Kitchen", 24.99m, 9.80m),
        new("KIT-205", "Stand mixer", "Kitchen", 329.00m, 201.00m),
        new("KIT-206", "Glass storage set", "Kitchen", 39.90m, 33.40m),
        new("KIT-207", "Digital kitchen scale", "Kitchen", 22.50m, 9.10m),
        new("KIT-208", "Dutch oven 5 L", "Kitchen", 99.00m, 47.00m),

        new("GAR-301", "Cordless hedge trimmer", "Garden", 129.00m, 74.00m),
        new("GAR-302", "Expandable hose 30 m", "Garden", 39.99m, 17.20m),
        new("GAR-303", "Raised garden bed", "Garden", 84.00m, 52.30m),
        new("GAR-304", "Pruning shears", "Garden", 29.90m, 11.60m),
        new("GAR-305", "Solar path lights (6)", "Garden", 44.50m, 38.90m),
        new("GAR-306", "Compost bin 300 L", "Garden", 59.00m, 31.00m),
        new("GAR-307", "Garden kneeler", "Garden", 32.00m, 14.40m),
        new("GAR-308", "Drip irrigation kit", "Garden", 74.90m, 36.50m),

        new("ELE-401", "Noise-cancelling headphones", "Electronics", 249.00m, 151.00m),
        new("ELE-402", "Portable speaker", "Electronics", 79.00m, 44.30m),
        new("ELE-403", "Smart plug (4-pack)", "Electronics", 39.99m, 31.50m),
        new("ELE-404", "USB-C charger 65 W", "Electronics", 44.00m, 19.80m),
        new("ELE-405", "E-reader", "Electronics", 139.00m, 97.00m),
        new("ELE-406", "Webcam 4K", "Electronics", 119.00m, 64.00m),
        new("ELE-407", "Mechanical keyboard", "Electronics", 99.00m, 52.00m),
        new("ELE-408", "Power bank 20,000 mAh", "Electronics", 49.00m, 23.70m),

        new("TOY-501", "Wooden train set", "Toys", 59.00m, 26.40m),
        new("TOY-502", "Building blocks 1,000 pcs", "Toys", 69.99m, 38.00m),
        new("TOY-503", "Plush bear", "Toys", 24.00m, 9.60m),
        new("TOY-504", "Board game: Harbor", "Toys", 39.00m, 21.50m),
        new("TOY-505", "Remote-control car", "Toys", 54.90m, 41.80m),
        new("TOY-506", "Art supplies kit", "Toys", 29.99m, 12.20m),
        new("TOY-507", "Puzzle 1,500 pcs", "Toys", 21.90m, 8.70m),
        new("TOY-508", "Kids' telescope", "Toys", 89.00m, 49.90m),
    ];

    private readonly ConcurrentDictionary<string, decimal> _prices = new(Seed.ToDictionary(p => p.Sku, p => p.ListPrice));

    public IReadOnlyList<Product> Products => Seed;

    public Product? Find(string sku) => Seed.FirstOrDefault(p => p.Sku == sku);

    public decimal CurrentPrice(string sku) => _prices[sku];

    internal void SetPrice(string sku, decimal price) => _prices[sku] = price;

    public static string? NormalizeCategory(string? category) =>
        Categories.FirstOrDefault(c => string.Equals(c, category?.Trim(), StringComparison.OrdinalIgnoreCase));
}
