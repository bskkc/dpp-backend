using System.Globalization;

namespace Dpp.Services;

public static class GtinValidator
{
    public static bool IsValidGtin14(string? value)
    {
        if (value is null || value.Length != 14 || value.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        var sum = 0;
        var weight = 3;
        for (var index = value.Length - 2; index >= 0; index--)
        {
            sum += (value[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        var checkDigit = (10 - sum % 10) % 10;
        return checkDigit == value[^1] - '0';
    }

    public static bool TryParseDecimal(string? value, out decimal result)
    {
        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result);
    }
}
