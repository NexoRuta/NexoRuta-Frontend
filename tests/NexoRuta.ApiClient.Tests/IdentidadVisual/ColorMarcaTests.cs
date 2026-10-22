using NexoRuta.ApiClient.Features.IdentidadVisual;

namespace NexoRuta.ApiClient.Tests.IdentidadVisual;

public sealed class ColorMarcaTests
{
    [Fact]
    public void Uses_only_a_valid_accent_with_three_to_one_contrast_against_white()
    {
        Assert.Equal("#123456", ColorMarca.AcentoSobreBlanco("#123456"));
        Assert.Null(ColorMarca.AcentoSobreBlanco("#FFFF00"));
        Assert.Null(ColorMarca.AcentoSobreBlanco("url(javascript:alert(1))"));
    }
}
