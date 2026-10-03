using System.Reflection;
using SixLabors.Fonts;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// Stimulsoft 2023.1.x was compiled against SixLabors.Fonts 1.0.0-beta19 and calls
/// <c>TextMeasurer.Measure(string, TextOptions)</c>, which Fonts 1.0.0 no longer has. ClosedXML 0.102.1+
/// requires Fonts 1.0.0, so once it was upgraded every report that measures text failed inside
/// <c>report.Render()</c> with MissingMethodException (customer statement PDF). Upgrading ClosedXML or
/// Stimulsoft must keep this method resolvable.
/// </summary>
public class ReportFontsCompatibilityTests
{
    [Fact]
    public void Stimulsoft_text_measuring_method_is_available()
    {
        var measure = typeof(TextMeasurer).GetMethod("Measure", BindingFlags.Public | BindingFlags.Static,
                                                     new[] { typeof(string), typeof(TextOptions) });

        Assert.NotNull(measure);
        Assert.Equal(typeof(FontRectangle), measure!.ReturnType);
    }
}
