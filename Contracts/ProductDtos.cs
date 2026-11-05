namespace Dpp.Contracts;

public sealed record ProductDto(
    string Gtin,
    string ModelName,
    string BatchNumber,
    decimal CottonPercent,
    decimal ElastanePercent,
    string ProductionLocation,
    decimal? CarbonKgCo2e,
    DateTimeOffset UpdatedAt);

public sealed record ProductSummaryDto(
    string Gtin,
    string ModelName,
    string BatchNumber,
    string ProductionLocation,
    decimal CottonPercent,
    decimal ElastanePercent,
    DateTimeOffset UpdatedAt);

public sealed record ImportRowError(int RowNumber, string Message);

public sealed record ImportResult(int TotalRows, int ImportedRows, int InvalidRows, IReadOnlyList<ImportRowError> Errors);
