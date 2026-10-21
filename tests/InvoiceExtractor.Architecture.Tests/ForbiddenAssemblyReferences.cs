namespace InvoiceExtractor.Architecture.Tests;

using System.Reflection;

/// <summary>
/// The inner layers carry no framework dependencies, checked on the references the compiler actually emitted.
/// Reflection, not ArchUnitNET: a namespace rule against an assembly that is not loaded resolves to zero types
/// and passes no matter what (found in talent-mcp by injecting EF Core into the domain).
/// </summary>
public sealed class ForbiddenAssemblyReferences
{
	private static readonly string[] Adapters =
	[
		"Microsoft.EntityFrameworkCore",
		"Npgsql",
		"Microsoft.AspNetCore",
		"UglyToad.PdfPig",
		"SkiaSharp",
		"ClosedXML",
		"Anthropic",
	];

	private static readonly string[] Hosting =
	[
		"Microsoft.Extensions.DependencyInjection",
		"Microsoft.Extensions.Hosting",
		"Microsoft.Extensions.Options",
		"Microsoft.Extensions.AI",
		"OpenTelemetry",
	];

	[Fact]
	public void Domain_references_no_framework_assembly() =>
		AssertNoReferencesStartingWith(
			Layers.Domain,
			[.. Adapters, .. Hosting],
			"the domain is plain C#: validation and tax rules run without a database, a model or a host.");

	[Fact]
	public void Application_references_only_ports() =>
		AssertNoReferencesStartingWith(
			Layers.Application,
			Adapters,
			"use cases are written against ports (IDocumentRepository, IFileStore, IDocumentInspector), so they "
			+ "are tested with fakes and the adapters can change without touching them.");

	private static void AssertNoReferencesStartingWith(Assembly assembly, string[] forbidden, string why)
	{
		var offending = assembly.GetReferencedAssemblies()
			.Select(static reference => reference.Name ?? string.Empty)
			.Where(name => forbidden.Any(prefix => IsPrefixMatch(name, prefix)))
			.ToArray();

		Assert.True(
			offending.Length == 0,
			$"{assembly.GetName().Name} must not reference [{string.Join(", ", offending)}]: {why}");
	}

	/// <summary>"Microsoft.Extensions.AI" must not match "Microsoft.Extensions.AI.Abstractions".</summary>
	private static bool IsPrefixMatch(string name, string prefix) =>
		name.Equals(prefix, StringComparison.OrdinalIgnoreCase)
		|| (name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
			&& !name.EndsWith(".Abstractions", StringComparison.OrdinalIgnoreCase));
}
