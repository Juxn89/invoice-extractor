namespace InvoiceExtractor.Architecture.Tests;

using System.Reflection;

/// <summary>
/// Licences the build can change without anyone noticing. ClosedXML accepts SixLabors.Fonts up to 3.x; only
/// the 1.x line is Apache-2.0, the later ones are under the Six Labors Split License, which is not OSI.
/// Directory.Packages.props pins 1.0.1; this test fails if a dependency ever pulls a newer major.
/// </summary>
public sealed class LicenceGuards
{
	[Fact]
	public void SixLabors_Fonts_stays_on_the_Apache_licensed_1_x_line()
	{
		var fonts = Assembly.Load("SixLabors.Fonts").GetName();

		Assert.True(fonts.Version?.Major == 1, $"SixLabors.Fonts {fonts.Version} resolved; 2.x and later are not under an OSI licence.");
	}
}
