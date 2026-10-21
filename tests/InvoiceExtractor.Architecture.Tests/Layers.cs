namespace InvoiceExtractor.Architecture.Tests;

using System.Reflection;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Infrastructure.Persistence;

/// <summary>
/// Assembly OBJECTS, not names: ArchUnitNET's ResideInAssembly(string) matches the full assembly name, so a
/// simple name matches nothing and every rule passes vacuously. Learned in talent-mcp.
/// </summary>
internal static class Layers
{
	public static readonly Assembly Domain = typeof(Document).Assembly;

	public static readonly Assembly Application = typeof(IDocumentRepository).Assembly;

	public static readonly Assembly Infrastructure = typeof(InvoiceExtractorDbContext).Assembly;

	public static readonly Assembly Api = typeof(Program).Assembly;
}
