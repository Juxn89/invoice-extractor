namespace InvoiceExtractor.Application.Features.Processing;

using System.ComponentModel.DataAnnotations;
using InvoiceExtractor.Domain.Tax;

/// <summary>The extraction worker, bound from the <c>Processing</c> section and validated at startup.</summary>
public sealed class ProcessingOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Processing";

	/// <summary>Off in tests and wherever only the upload side should run.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Documents processed at once by this instance. Each one is a model call of a few seconds, so a handful
	/// keeps a batch of 20 moving without hitting the API's rate limits.
	/// </summary>
	[Range(1, 32)]
	public int Workers { get; set; } = 4;

	/// <summary>How long an idle worker waits before looking for work again.</summary>
	[Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <summary>
	/// How long a claim holds. An extraction takes seconds; five minutes covers a slow call and its retries,
	/// and after that a crashed worker's document goes back to the others.
	/// </summary>
	[Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
	public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>Attempts per document before it is marked failed (a transient error or a crash costs one each).</summary>
	[Range(1, 10)]
	public int MaxAttempts { get; set; } = 3;

	/// <summary>
	/// Model spend allowed per UTC day. When reached, workers stop claiming until the next day: a loop or a
	/// flood of uploads costs at most this much. At about USD 0.0005 a document, one dollar is ~2,000 of them.
	/// </summary>
	[Range(typeof(decimal), "0", "1000")]
	public decimal DailyBudgetUsd { get; set; } = 1.00m;
}

/// <summary>
/// Tax rates per country, bound from the <c>Tax</c> section. Configuration, not constants: rates change by
/// decree (Mexico's 8% border rate is renewed yearly). Defaults and sources: docs/verification/tax-rules.md.
/// </summary>
public sealed class TaxOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Tax";

	/// <summary>Nicaragua's IVA: 15%, and 0% for exempt sales.</summary>
	[MinLength(1)]
	public IList<decimal> NicaraguaRates { get; init; } = [0.15m, 0m];

	/// <summary>Mexico's IVA: 16% general, 8% in the northern and southern border regions, 0%.</summary>
	[MinLength(1)]
	public IList<decimal> MexicoRates { get; init; } = [0.16m, 0.08m, 0m];
}

/// <summary>The country profiles, built once from configuration.</summary>
public sealed class TaxProfileCatalog
{
	private readonly Dictionary<string, ITaxProfile> _profiles;

	/// <summary>Builds the profiles.</summary>
	/// <param name="options">The rates.</param>
	public TaxProfileCatalog(Microsoft.Extensions.Options.IOptions<TaxOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ITaxProfile[] profiles =
		[
			new NicaraguaTaxProfile(options.Value.NicaraguaRates),
			new MexicoTaxProfile(options.Value.MexicoRates),
			new GenericTaxProfile(),
		];
		_profiles = profiles.ToDictionary(static p => p.CountryCode, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The codes a batch can be uploaded with.</summary>
	public IReadOnlyCollection<string> Codes => _profiles.Keys;

	/// <summary>Whether a code names a profile.</summary>
	/// <param name="code">The code.</param>
	/// <returns><see langword="true"/> when it does.</returns>
	public bool IsKnown(string? code) => code is not null && _profiles.ContainsKey(code);

	/// <summary>The profile for a code.</summary>
	/// <param name="code">NI, MX or XX.</param>
	/// <returns>The profile.</returns>
	/// <exception cref="KeyNotFoundException">Unknown code; uploads are validated, so this is a bug.</exception>
	public ITaxProfile For(string code) => _profiles[code];
}
