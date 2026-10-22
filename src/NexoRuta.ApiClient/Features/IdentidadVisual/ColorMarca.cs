namespace NexoRuta.ApiClient.Features.IdentidadVisual;

public static class ColorMarca
{
    public static string? AcentoSobreBlanco(string? color)
        => TieneFormatoValido(color) && Contraste(color!, "#FFFFFF") >= 3 ? color : null;

    public static bool TieneFormatoValido(string? color)
        => color is { Length: 7 } && color[0] == '#'
            && color.AsSpan(1).ToString().All(Uri.IsHexDigit);

    private static double Contraste(string first, string second)
    {
        var a = Luminancia(first);
        var b = Luminancia(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminancia(string color)
    {
        var red = Linear(Convert.ToInt32(color[1..3], 16) / 255d);
        var green = Linear(Convert.ToInt32(color[3..5], 16) / 255d);
        var blue = Linear(Convert.ToInt32(color[5..7], 16) / 255d);
        return .2126 * red + .7152 * green + .0722 * blue;
    }

    private static double Linear(double value)
        => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
}
