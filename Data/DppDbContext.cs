using Dpp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dpp.Data;

public sealed class DppDbContext(DbContextOptions<DppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductModel> ProductModels => Set<ProductModel>();
    public DbSet<ProductionBatch> ProductionBatches => Set<ProductionBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(product => product.Gtin);
            entity.Property(product => product.Gtin).HasMaxLength(14).IsRequired();
            entity.Property(product => product.UpdatedAt).IsRequired();
            entity.HasOne(product => product.Model)
                .WithMany(model => model.Products)
                .HasForeignKey(product => product.ProductModelId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(product => product.Batch)
                .WithMany(batch => batch.Products)
                .HasForeignKey(product => product.ProductionBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductModel>(entity =>
        {
            entity.ToTable("product_models");
            entity.HasKey(model => model.Id);
            entity.Property(model => model.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(model => model.Name).IsUnique();
        });

        modelBuilder.Entity<ProductionBatch>(entity =>
        {
            entity.ToTable("production_batches");
            entity.HasKey(batch => batch.Id);
            entity.Property(batch => batch.BatchNumber).HasMaxLength(100).IsRequired();
            entity.Property(batch => batch.ProductionLocation).HasMaxLength(200).IsRequired();
            entity.Property(batch => batch.CottonPercent).HasPrecision(5, 2);
            entity.Property(batch => batch.ElastanePercent).HasPrecision(5, 2);
            entity.Property(batch => batch.CarbonKgCo2e).HasPrecision(12, 3);
            entity.HasIndex(batch => batch.BatchNumber).IsUnique();
        });
    }
}
