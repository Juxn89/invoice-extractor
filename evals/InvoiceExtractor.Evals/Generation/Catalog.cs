namespace InvoiceExtractor.Evals.Generation;

/// <summary>A fictional business. Tax ids have each country's documented shape and belong to no one.</summary>
/// <param name="Name">The business name.</param>
/// <param name="TaxId">The tax id as printed.</param>
/// <param name="Address">Street address.</param>
/// <param name="City">City.</param>
/// <param name="TaxRate">The rate its sales carry.</param>
internal sealed record Issuer(string Name, string TaxId, string Address, string City, decimal TaxRate);

/// <summary>Something sold, with a price range in Nicaraguan córdobas (scaled per currency).</summary>
/// <param name="Spanish">Description in Spanish.</param>
/// <param name="English">Description in English.</param>
/// <param name="MinPrice">Lowest unit price, NIO.</param>
/// <param name="MaxPrice">Highest unit price, NIO.</param>
/// <param name="ByWeight">Sold in fractional quantities (kg).</param>
internal sealed record Item(string Spanish, string English, decimal MinPrice, decimal MaxPrice, bool ByWeight = false);

/// <summary>How a country's documents look.</summary>
/// <param name="Code">Profile code: NI, MX or XX.</param>
/// <param name="Currency">ISO 4217 code.</param>
/// <param name="Symbol">Currency symbol as printed.</param>
/// <param name="PriceFactor">Converts the catalog's NIO prices into this currency, roughly.</param>
/// <param name="DecimalComma">Whether amounts print as 1.150,00 instead of 1,150.00.</param>
/// <param name="Language">Printed language: es or en.</param>
/// <param name="Issuers">Businesses that issue documents.</param>
/// <param name="Receivers">Customers named on invoices.</param>
/// <param name="NumberPrefix">Invoice number prefix.</param>
internal sealed record Market(
	string Code,
	string Currency,
	string Symbol,
	decimal PriceFactor,
	bool DecimalComma,
	string Language,
	IReadOnlyList<Issuer> Issuers,
	IReadOnlyList<string> Receivers,
	string NumberPrefix);

/// <summary>The fictional world the dataset is drawn from.</summary>
internal static class Catalog
{
	public static IReadOnlyList<Item> Items { get; } =
	[
		new("Café en grano 1 kg", "Coffee beans 1 kg", 250m, 420m),
		new("Filtros de papel (100 u.)", "Paper filters (100 pcs)", 80m, 150m),
		new("Leche entera 1 L", "Whole milk 1 L", 35m, 60m),
		new("Vasos desechables (50 u.)", "Disposable cups (50 pcs)", 90m, 180m),
		new("Azúcar morena", "Brown sugar", 40m, 70m, ByWeight: true),
		new("Cartucho de tinta negra", "Black ink cartridge", 900m, 1500m),
		new("Resma de papel carta", "Letter paper ream", 180m, 320m),
		new("Martillo de uña", "Claw hammer", 250m, 400m),
		new("Caja de tornillos", "Box of screws", 60m, 120m),
		new("Pintura látex, galón", "Latex paint, gallon", 600m, 900m),
		new("Servicio de limpieza (hora)", "Cleaning service (hour)", 300m, 500m),
		new("Teclado USB", "USB keyboard", 450m, 800m),
		new("Café molido", "Ground coffee", 300m, 380m, ByWeight: true),
	];

	public static Market Nicaragua { get; } = new(
		"NI", "NIO", "C$", 1m, DecimalComma: false, "es",
		[
			new("Distribuidora El Mirador, S.A.", "J0310000252297", "Km 4½ Carretera a Masaya", "Managua", 0.15m),
			new("Ferretería La Esperanza", "001-150385-0002K", "Barrio San Felipe, de la iglesia 2 c. al sur", "León", 0.15m),
			new("Imprenta Los Robles, S.A.", "J0310000988417", "Colonia Los Robles, casa 14", "Managua", 0.15m),
		],
		["Café Mirlo", "Hotel Lago Azul", "Escuela Rubén Darío"],
		"F-");

	public static Market Mexico { get; } = new(
		"MX", "MXN", "$", 0.5m, DecimalComma: false, "es",
		[
			new("Comercializadora Mirlo, S.A. de C.V.", "CMI050101AB1", "Av. Insurgentes Sur 1458", "Ciudad de México", 0.16m),
			new("Papelería del Centro", "GOLR800315H21", "Calle Madero 22", "Puebla", 0.16m),
			new("Tostadores del Norte, S.A. de C.V.", "TNO120604KJ8", "Blvd. Agua Caliente 10500", "Tijuana", 0.08m),
		],
		["Cafetería La Ceiba", "Hotel Paseo", "Despacho Contable Ruiz"],
		"A-");

	public static Market UnitedStates { get; } = new(
		"XX", "USD", "$", 1m / 36m, DecimalComma: false, "en",
		[
			new("Mirlo Coffee Supply LLC", "98-7654321", "1200 Pine St", "Seattle, WA", 0.1025m),
			new("Northwind Office Goods Inc.", "47-1234567", "500 Market St", "Denver, CO", 0.08m),
		],
		["Blue Lake Café", "Harbor Hotel"],
		"INV-");

	public static Market Spain { get; } = new(
		"XX", "EUR", "€", 1m / 40m, DecimalComma: true, "es",
		[new("Suministros Ibéricos, S.L.", "B12345678", "Calle de Alcalá 120", "Madrid", 0.21m)],
		["Cafetería El Retiro"],
		"FV-");
}
