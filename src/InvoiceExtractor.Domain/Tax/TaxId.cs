namespace InvoiceExtractor.Domain.Tax;

using System.Globalization;
using System.Text;

/// <summary>Turns a tax id as printed into the form the format rules check.</summary>
public static class TaxId
{
	/// <summary>
	/// Drops the separators people print between groups (spaces, hyphens, dots, slashes) and upper-cases the
	/// rest. "J031-0000-25229-7" and "j0310000252297" are the same id.
	/// </summary>
	/// <param name="taxId">The id as printed.</param>
	/// <returns>The normalised id.</returns>
	public static string Normalize(string taxId)
	{
		ArgumentNullException.ThrowIfNull(taxId);

		var normalized = new StringBuilder(taxId.Length);
		foreach (var character in taxId)
		{
			if (character is ' ' or '-' or '.' or '/')
			{
				continue;
			}

			normalized.Append(char.ToUpper(character, CultureInfo.InvariantCulture));
		}

		return normalized.ToString();
	}
}
