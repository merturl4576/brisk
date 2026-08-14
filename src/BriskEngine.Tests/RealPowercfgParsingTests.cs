using System;
using System.Linq;
using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

public class RealPowercfgParsingTests
{
    private const string EnglishList = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        """;

    private const string TurkishList = """
        Var Olan Güç Düzenleri (* Etkin)
        -----------------------------------
        Güç Düzeni GUID'i: 381b4222-f694-41f0-9685-ff5bb260df2e  (Dengeli) *
        Güç Düzeni GUID'i: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Yüksek performans)
        """;

    [Theory]
    [InlineData(EnglishList, "Balanced")]
    [InlineData(TurkishList, "Dengeli")]
    public void ParsesSchemes_AndActiveMarker(string output, string activeName)
    {
        var schemes = RealPowercfgProbe.ParseSchemes(output);
        Assert.Equal(2, schemes.Count);
        var active = schemes.Single(s => s.IsActive);
        Assert.Equal(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), active.Id);
        Assert.Equal(activeName, active.Name);
    }
}
