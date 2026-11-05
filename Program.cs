using System.Diagnostics;
using Dpp.Data;
using Dpp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QRCoder;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var configuredConnectionString = builder.Configuration.GetConnectionString("Dpp")
    ?? throw new InvalidOperationException(
        "PostgreSQL connection is not configured. Set ConnectionStrings:Dpp in appsettings.json.");
await EnsureDppDatabaseExistsAsync(configuredConnectionString);

var connectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
{
    Database = "dpp"
}.ConnectionString;

builder.Services.AddDbContext<DppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddSingleton<DigitalProductPassportService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var address = app.Urls.FirstOrDefault(url => Uri.TryCreate(url, UriKind.Absolute, out _));
        if (address is null)
        {
            app.Logger.LogError("Swagger tarayıcısı açılamadı; HTTP dinleme adresi bulunamadı.");
            return;
        }

        Process.Start(new ProcessStartInfo($"{address.TrimEnd('/')}/swagger")
        {
            UseShellExecute = true
        });
    });
}

app.UseCors();

await InitializeDatabaseAsync(app);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapGet("/products", async (ProductCatalogService catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.GetProductsAsync(cancellationToken)));

api.MapGet("/products/{gtin}", async (
    string gtin,
    [FromQuery(Name = "rol")] string? role,
    ProductCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    var product = await catalog.GetProductAsync(gtin, role == "yetkili", cancellationToken);
    return product is null ? Results.NotFound(new { message = "Ürün bulunamadı." }) : Results.Ok(product);
});

api.MapPost("/imports/csv", async (
    IFormFile? file,
    CsvImportService importer,
    CancellationToken cancellationToken) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "İçe aktarılacak boş olmayan bir CSV dosyası seçin." });
    }

    if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { message = "Yalnızca .csv uzantılı dosyalar yüklenebilir." });
    }

    if (file.Length > 10 * 1024 * 1024)
    {
        return Results.BadRequest(new { message = "CSV dosyası 10 MB sınırını aşamaz." });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var result = await importer.ImportAsync(stream, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).DisableAntiforgery();

api.MapGet("/products/{gtin}/qr", async (
    string gtin,
    IConfiguration configuration,
    ProductCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    if (!GtinValidator.IsValidGtin14(gtin))
    {
        return Results.BadRequest(new { message = "Geçerli bir 14 haneli GTIN girin." });
    }

    if (await catalog.GetProductAsync(gtin, authorized: false, cancellationToken: cancellationToken) is null)
    {
        return Results.NotFound(new { message = "Ürün bulunamadı." });
    }

    var baseUrl = (configuration["PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
    using var generator = new QRCodeGenerator();
    using var qrData = generator.CreateQrCode($"{baseUrl}/dpp/{Uri.EscapeDataString(gtin)}", QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(qrData);
    return Results.File(png.GetGraphic(8), "image/png", $"{gtin}-qr.png");
});

app.MapGet("/dpp/{gtin}", async (
    string gtin,
    [FromQuery(Name = "rol")] string? role,
    ProductCatalogService catalog,
    DigitalProductPassportService passport,
    CancellationToken cancellationToken) =>
{
    var product = await catalog.GetProductAsync(gtin, role == "yetkili", cancellationToken);
    return product is null
        ? Results.NotFound("Ürün bulunamadı.")
        : Results.Content(passport.CreateHtml(product), "text/html; charset=utf-8");
});

app.MapGet("/dpp/{gtin}.jsonld", async (
    string gtin,
    [FromQuery(Name = "rol")] string? role,
    ProductCatalogService catalog,
    DigitalProductPassportService passport,
    CancellationToken cancellationToken) =>
{
    var product = await catalog.GetProductAsync(gtin, role == "yetkili", cancellationToken);
    return product is null
        ? Results.NotFound(new { message = "Ürün bulunamadı." })
        : Results.Content(passport.CreateJsonLd(product), "application/ld+json; charset=utf-8");
});

app.Run();

static async Task EnsureDppDatabaseExistsAsync(string configuredConnectionString)
{
    var applicationConnectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
    {
        Database = "dpp"
    }.ConnectionString;

    try
    {
        await using var applicationConnection = new NpgsqlConnection(applicationConnectionString);
        await applicationConnection.OpenAsync();
        return;
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidCatalogName)
    {
    }

    var maintenanceConnectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
    {
        Database = "postgres"
    }.ConnectionString;

    await using var connection = new NpgsqlConnection(maintenanceConnectionString);
    await connection.OpenAsync();

    await using var createDatabase = new NpgsqlCommand("CREATE DATABASE dpp", connection);
    try
    {
        await createDatabase.ExecuteNonQueryAsync();
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.DuplicateDatabase)
    {
        // Another application instance may create the database at the same time.
    }
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    if (await dbContext.Products.AnyAsync())
    {
        return;
    }

    var seedFile = Path.Combine(AppContext.BaseDirectory, "Data", "urunler_duzeltilmis_test.csv");
    if (!File.Exists(seedFile))
    {
        app.Logger.LogWarning("Örnek CSV bulunamadı; başlangıç verileri yüklenmedi: {SeedFile}", seedFile);
        return;
    }

    await using var stream = File.OpenRead(seedFile);
    var result = await scope.ServiceProvider.GetRequiredService<CsvImportService>()
        .ImportAsync(stream, CancellationToken.None);
    app.Logger.LogInformation(
        "Örnek CSV yüklendi: {Imported} başarılı, {Invalid} hatalı satır.",
        result.ImportedRows,
        result.InvalidRows);
}
