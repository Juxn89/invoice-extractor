namespace InvoiceExtractor.Evals.Paid;

using System.Globalization;

/// <summary>A paid call was refused before it was sent.</summary>
/// <param name="message">Why.</param>
internal sealed class BudgetExceededException(string message) : Exception(message);

/// <summary>
/// Counts and prices every model call of a run and refuses the next one before it is sent when, in the worst
/// case (its estimated input plus the whole output cap), it could take the run past its call or cost limit.
/// </summary>
/// <param name="maxCalls">Calls allowed in this run.</param>
/// <param name="maxCostUsd">Spend allowed in this run, already capped by what the ledger says is left.</param>
internal sealed class SpendingGuard(int maxCalls, decimal maxCostUsd)
{
	public int Calls { get; private set; }

	public long InputTokens { get; private set; }

	public long OutputTokens { get; private set; }

	public decimal CostUsd { get; private set; }

	public decimal MaxCostUsd => maxCostUsd;

	public void EnsureCanCall(decimal worstCaseUsd)
	{
		if (Calls + 1 > maxCalls)
		{
			throw new BudgetExceededException($"call {Calls + 1} would exceed --max-calls {maxCalls}");
		}

		if (CostUsd + worstCaseUsd > maxCostUsd)
		{
			throw new BudgetExceededException(string.Create(
				CultureInfo.InvariantCulture,
				$"call {Calls + 1} could cost up to {worstCaseUsd:F6} USD; spent {CostUsd:F6} of {maxCostUsd:F6} USD"));
		}
	}

	public void Record(long inputTokens, long outputTokens, decimal costUsd)
	{
		Calls++;
		InputTokens += inputTokens;
		OutputTokens += outputTokens;
		CostUsd += costUsd;
	}
}

/// <summary>
/// The budget ledger shared by every session on the same API account: a TSV outside the repo
/// (<c>~/.anthropic-budget/ledger.tsv</c>). Read before a paid run to know what is left; one row appended after
/// it with the real usage. Append-only: rows are never rewritten.
/// </summary>
/// <param name="Path">The ledger file.</param>
/// <param name="Session">This project's session name in the allocation table.</param>
/// <param name="SessionCapUsd">The cap the user approved for it.</param>
internal sealed record Ledger(string Path, string Session, decimal SessionCapUsd)
{
	/// <summary>This repo's allocation, approved by the user on 2026-10-21 (see the ledger's README).</summary>
	public static Ledger Default { get; } = new(
		System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".anthropic-budget", "ledger.tsv"),
		"workana-invoice-extractor",
		0.25m);

	private const int SessionColumn = 1;
	private const int UsdColumn = 10;

	public decimal SpentBySession() =>
		File.ReadLines(Path)
			.Skip(1)
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.Select(static line => line.Split('\t'))
			.Where(columns => columns.Length > UsdColumn && columns[SessionColumn] == Session)
			.Sum(static columns => decimal.Parse(columns[UsdColumn], NumberStyles.Float, CultureInfo.InvariantCulture));

	public decimal Remaining() => SessionCapUsd - SpentBySession();

	public void Append(string experiment, string model, SpendingGuard guard, string note)
	{
		ArgumentNullException.ThrowIfNull(guard);
		var row = string.Join('\t',
			DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
			Session,
			"invoice-extractor",
			experiment,
			model,
			guard.Calls.ToString(CultureInfo.InvariantCulture),
			guard.InputTokens.ToString(CultureInfo.InvariantCulture),
			"0",
			"0",
			guard.OutputTokens.ToString(CultureInfo.InvariantCulture),
			guard.CostUsd.ToString("0.000000", CultureInfo.InvariantCulture),
			note);
		File.AppendAllText(Path, row + Environment.NewLine);
	}
}

/// <summary>Reads <c>KEY=value</c> lines from the repo's git-ignored <c>.env</c>, never printing them.</summary>
internal static class DotEnv
{
	public static string? Read(string root, string key)
	{
		var path = Path.Combine(root, ".env");
		if (!File.Exists(path))
		{
			return Environment.GetEnvironmentVariable(key);
		}

		var prefix = key + "=";
		var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
		var value = line?[prefix.Length..].Trim().Trim('"', '\'');
		return string.IsNullOrEmpty(value) ? Environment.GetEnvironmentVariable(key) : value;
	}
}
