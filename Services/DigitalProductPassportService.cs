using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dpp.Contracts;

namespace Dpp.Services;

public sealed class DigitalProductPassportService(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public string CreateHtml(ProductDto product)
    {
        var carbon = product.CarbonKgCo2e is null
            ? string.Empty
            : $"<div class=\"item\"><span>Karbon ayak izi</span><strong>{product.CarbonKgCo2e.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} kg CO₂e</strong></div>";
        var jsonLdUrl = $"/dpp/{Uri.EscapeDataString(product.Gtin)}.jsonld"
            + (product.CarbonKgCo2e is null ? string.Empty : "?rol=yetkili");

        return $$"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="description" content="{{HtmlEncoder.Default.Encode(product.ModelName)}} dijital ürün pasaportu">
              <title>{{HtmlEncoder.Default.Encode(product.ModelName)}} — Dijital Ürün Pasaportu</title>
              <style>
                body{margin:0;padding:40px 20px;background:#f4f7f5;color:#19352e;font:16px/1.6 system-ui,sans-serif}
                main{max-width:760px;margin:auto;padding:40px;background:#fff;border:1px solid #e4ebe7;border-radius:20px;box-shadow:0 12px 40px #173c2c0c}
                .eyebrow{color:#287450;font-weight:700;letter-spacing:.08em;text-transform:uppercase;font-size:12px}
                h1{margin:.5rem 0 0;font-size:clamp(28px,5vw,42px);line-height:1.15}
                .gtin{color:#65756e;font-family:ui-monospace,monospace}
                .grid{margin-top:32px;border-top:1px solid #e9eeeb}
                .item{display:flex;justify-content:space-between;gap:20px;padding:17px 0;border-bottom:1px solid #e9eeeb}
                .item span{color:#65756e}.item strong{text-align:right}
                a{color:#287450}
              </style>
            </head>
            <body>
              <main>
                <div class="eyebrow">Dijital Ürün Pasaportu</div>
                <h1>{{HtmlEncoder.Default.Encode(product.ModelName)}}</h1>
                <div class="gtin">GTIN {{HtmlEncoder.Default.Encode(product.Gtin)}}</div>
                <section class="grid" aria-label="Ürün bilgileri">
                  <div class="item"><span>Model</span><strong>{{HtmlEncoder.Default.Encode(product.ModelName)}}</strong></div>
                  <div class="item"><span>Üretim lotu</span><strong>{{HtmlEncoder.Default.Encode(product.BatchNumber)}}</strong></div>
                  <div class="item"><span>Üretim yeri</span><strong>{{HtmlEncoder.Default.Encode(product.ProductionLocation)}}</strong></div>
                  <div class="item"><span>Materyal</span><strong>{{product.CottonPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}}% pamuk · {{product.ElastanePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}}% elastan</strong></div>
                  {{(carbon)}}
                </section>
                <p><a href="{{HtmlEncoder.Default.Encode(jsonLdUrl)}}">JSON-LD verisini görüntüle</a></p>
              </main>
            </body>
            </html>
            """;
    }

    public string CreateJsonLd(ProductDto product)
    {
        var baseUrl = (configuration["PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var jsonLd = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["@id"] = $"{baseUrl}/dpp/{Uri.EscapeDataString(product.Gtin)}",
            ["name"] = product.ModelName,
            ["gtin14"] = product.Gtin,
            ["material"] = $"{FormatPercent(product.CottonPercent)}% pamuk, {FormatPercent(product.ElastanePercent)}% elastan",
            ["countryOfOrigin"] = new Dictionary<string, string> { ["@type"] = "Country", ["name"] = "Türkiye" },
            ["additionalProperty"] = new[]
            {
                new { @type = "PropertyValue", name = "Lot numarası", value = product.BatchNumber },
                new { @type = "PropertyValue", name = "Üretim yeri", value = product.ProductionLocation }
            }
        };

        if (product.CarbonKgCo2e is not null)
        {
            jsonLd["additionalProperty"] = new object[]
            {
                new { @type = "PropertyValue", name = "Lot numarası", value = product.BatchNumber },
                new { @type = "PropertyValue", name = "Üretim yeri", value = product.ProductionLocation },
                new { @type = "PropertyValue", name = "Karbon ayak izi", value = product.CarbonKgCo2e.Value, unitText = "kg CO2e" }
            };
        }

        return JsonSerializer.Serialize(jsonLd, JsonOptions);
    }

    private static string FormatPercent(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
