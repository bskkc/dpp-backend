using CsvHelper;
using CsvHelper.Configuration;
using Dpp.Contracts;
using Dpp.Data;
using Dpp.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Dpp.Services;

public sealed class CsvImportService(DppDbContext dbContext, ILogger<CsvImportService> logger)
{
    private static readonly string[] RequiredHeaders =
    [
        "gtin",
        "model_adi",
        "batch_no",
        "elyaf_pamuk_yuzde",
        "elyaf_elastan_yuzde",
        "uretim_yeri",
        "karbon_kg_co2e"
    ];

    public async Task<ImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var malformedRecord = false;
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = _ => malformedRecord = true
        });

        if (!await csv.ReadAsync())
        {
            throw new InvalidDataException("CSV dosyası boş; başlık satırı bulunamadı.");
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        if (malformedRecord)
        {
            throw new InvalidDataException("CSV başlık satırının biçimi geçersiz.");
        }

        var missingHeaders = RequiredHeaders
            .Where(required => !headers.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingHeaders.Length > 0)
        {
            throw new InvalidDataException($"CSV başlıkları eksik: {string.Join(", ", missingHeaders)}.");
        }

        var duplicateHeaders = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateHeaders.Length > 0)
        {
            throw new InvalidDataException($"CSV başlıkları birden fazla kez kullanılmış: {string.Join(", ", duplicateHeaders)}.");
        }

        var headerIndexes = headers
            .Select((header, index) => (Header: header, Index: index))
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var total = 0;
        var errors = new List<ImportRowError>();
        var seenGtins = new HashSet<string>(StringComparer.Ordinal);

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            var rowNumber = csv.Parser.Row;
            if (malformedRecord)
            {
                errors.Add(new ImportRowError(rowNumber, "CSV satırının biçimi geçersiz; ayraçları ve tırnak işaretlerini kontrol edin."));
                malformedRecord = false;
                continue;
            }

            var record = new ImportRecord(
                GetField(csv, headerIndexes, "gtin"),
                GetField(csv, headerIndexes, "model_adi"),
                GetField(csv, headerIndexes, "batch_no"),
                GetField(csv, headerIndexes, "elyaf_pamuk_yuzde"),
                GetField(csv, headerIndexes, "elyaf_elastan_yuzde"),
                GetField(csv, headerIndexes, "uretim_yeri"),
                GetField(csv, headerIndexes, "karbon_kg_co2e"));

            var validationErrors = Validate(record);
            if (validationErrors.Count == 0 && !seenGtins.Add(record.Gtin))
            {
                validationErrors.Add("GTIN aynı CSV dosyasında birden fazla kez kullanılmış.");
            }

            if (validationErrors.Count > 0)
            {
                errors.Add(new ImportRowError(rowNumber, string.Join(" ", validationErrors)));
                continue;
            }

            await SaveRecordAsync(record, cancellationToken);
            imported++;
        }

        logger.LogInformation("CSV aktarımı tamamlandı. {Imported} başarılı, {Invalid} hatalı satır.", imported, errors.Count);
        return new ImportResult(total, imported, errors.Count, errors);
    }

    private static List<string> Validate(ImportRecord record)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(record.Gtin))
        {
            errors.Add("GTIN boş olamaz.");
        }
        else if (!GtinValidator.IsValidGtin14(record.Gtin))
        {
            errors.Add("GTIN, GS1 kontrol basamağı geçerli 14 haneli sayısal bir değer olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(record.ModelName))
        {
            errors.Add("Model adı boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(record.BatchNumber))
        {
            errors.Add("Lot numarası boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(record.ProductionLocation))
        {
            errors.Add("Üretim yeri boş olamaz.");
        }

        if (!GtinValidator.TryParseDecimal(record.CottonPercent, out var cotton))
        {
            errors.Add("Pamuk yüzdesi sayısal olmalıdır.");
        }
        else if (cotton is < 0 or > 100)
        {
            errors.Add("Pamuk yüzdesi 0–100 aralığında olmalıdır.");
        }

        if (!GtinValidator.TryParseDecimal(record.ElastanePercent, out var elastane))
        {
            errors.Add("Elastan yüzdesi sayısal olmalıdır.");
        }
        else if (elastane is < 0 or > 100)
        {
            errors.Add("Elastan yüzdesi 0–100 aralığında olmalıdır.");
        }

        if (GtinValidator.TryParseDecimal(record.CottonPercent, out cotton)
            && GtinValidator.TryParseDecimal(record.ElastanePercent, out elastane)
            && cotton + elastane != 100)
        {
            errors.Add("Pamuk ve elastan yüzdelerinin toplamı %100 olmalıdır.");
        }

        if (!GtinValidator.TryParseDecimal(record.CarbonKgCo2e, out var carbon))
        {
            errors.Add("Karbon değeri sayısal olmalıdır.");
        }
        else if (carbon < 0)
        {
            errors.Add("Karbon değeri negatif olamaz.");
        }

        return errors;
    }

    private static string GetField(CsvReader csv, IReadOnlyDictionary<string, int> headerIndexes, string header)
    {
        return csv.TryGetField(headerIndexes[header], out string? value) ? value?.Trim() ?? string.Empty : string.Empty;
    }

    private async Task SaveRecordAsync(ImportRecord record, CancellationToken cancellationToken)
    {
        var model = dbContext.ProductModels.Local
            .SingleOrDefault(item => item.Name == record.ModelName)
            ?? await dbContext.ProductModels
            .SingleOrDefaultAsync(item => item.Name == record.ModelName, cancellationToken);
        if (model is null)
        {
            model = new ProductModel { Name = record.ModelName };
            dbContext.ProductModels.Add(model);
        }

        var batch = dbContext.ProductionBatches.Local
            .SingleOrDefault(item => item.BatchNumber == record.BatchNumber)
            ?? await dbContext.ProductionBatches
            .SingleOrDefaultAsync(item => item.BatchNumber == record.BatchNumber, cancellationToken);
        if (batch is null)
        {
            batch = new ProductionBatch { BatchNumber = record.BatchNumber };
            dbContext.ProductionBatches.Add(batch);
        }

        GtinValidator.TryParseDecimal(record.CottonPercent, out var cotton);
        GtinValidator.TryParseDecimal(record.ElastanePercent, out var elastane);
        GtinValidator.TryParseDecimal(record.CarbonKgCo2e, out var carbon);
        batch.CottonPercent = cotton;
        batch.ElastanePercent = elastane;
        batch.ProductionLocation = record.ProductionLocation;
        batch.CarbonKgCo2e = carbon;

        var product = await dbContext.Products
            .SingleOrDefaultAsync(item => item.Gtin == record.Gtin, cancellationToken);
        if (product is null)
        {
            product = new Product { Gtin = record.Gtin };
            dbContext.Products.Add(product);
        }

        product.Model = model;
        product.Batch = batch;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record ImportRecord(
        string Gtin,
        string ModelName,
        string BatchNumber,
        string CottonPercent,
        string ElastanePercent,
        string ProductionLocation,
        string CarbonKgCo2e);
}
