namespace Dpp.Domain;

public sealed class Product
{
    public string Gtin { get; set; } = string.Empty;
    public int ProductModelId { get; set; }
    public ProductModel Model { get; set; } = null!;
    public int ProductionBatchId { get; set; }
    public ProductionBatch Batch { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProductModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class ProductionBatch
{
    public int Id { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal CottonPercent { get; set; }
    public decimal ElastanePercent { get; set; }
    public string ProductionLocation { get; set; } = string.Empty;
    public decimal CarbonKgCo2e { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
