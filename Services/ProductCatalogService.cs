using Dpp.Contracts;
using Dpp.Data;
using Microsoft.EntityFrameworkCore;

namespace Dpp.Services;

public sealed class ProductCatalogService(DppDbContext dbContext)
{
    public async Task<IReadOnlyList<ProductSummaryDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Model.Name)
            .Select(product => new ProductSummaryDto(
                product.Gtin,
                product.Model.Name,
                product.Batch.BatchNumber,
                product.Batch.ProductionLocation,
                product.Batch.CottonPercent,
                product.Batch.ElastanePercent,
                product.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductDto?> GetProductAsync(string gtin, bool authorized, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Where(item => item.Gtin == gtin)
            .Select(item => new ProductDto(
                item.Gtin,
                item.Model.Name,
                item.Batch.BatchNumber,
                item.Batch.CottonPercent,
                item.Batch.ElastanePercent,
                item.Batch.ProductionLocation,
                authorized ? item.Batch.CarbonKgCo2e : null,
                item.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return product;
    }
}
