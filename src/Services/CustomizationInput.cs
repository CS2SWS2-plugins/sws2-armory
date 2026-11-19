using System.Globalization;
using System.Text;

namespace Sws2Armory.Services;

public static class CustomizationInput
{
    public static bool TryWear(string input, out float value) =>
        float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && float.IsFinite(value) && value is >= 0 and <= 1;

    public static bool TrySeed(string input, out int value) => TryInteger(input, 1000, out value);
    public static bool TryCounter(string input, out int value) => TryInteger(input, 999999, out value);
    public static bool TryInteger(string input, int maximum, out int value) =>
        int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= maximum;
    public static bool ValidName(string input) => input.Length <= 64 && Encoding.UTF8.GetByteCount(input) <= 160 && !input.Any(char.IsControl);
}
