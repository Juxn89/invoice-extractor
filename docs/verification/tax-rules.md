# Verification · Tax rates and tax-id formats

| | |
|---|---|
| **Date** | 21 Oct 2026 |
| **Method** | Web search; each claim below says how reliable its sources are |
| **Why** | The validator's country rules (`src/InvoiceExtractor.Domain/Tax/`) decide whether a document is approved or sent to review. A wrong rule in the strict direction costs a review; in the lenient direction it lets a misread through, so the lenient side matters more |

---

## Nicaragua

**IVA rate — 15%, and 0% for exports and exempt goods.** Well supported: Grant Thornton's indirect tax
guide ([link](https://www.grantthornton.global/en/insights/indirect-tax-guide/indirect-tax---Nicaragua/)) and
secondary sites attribute it to Ley 822, Ley de Concertación Tributaria (in force since January 2013). Not
read in the official text (La Gaceta or the DGI).

**RUC — 14 characters.** A legal entity's starts with `J` followed by 13 digits (example in the source:
`J1310000252297`); an individual's is their cédula, 13 digits and a check letter. **Only a third-party guide
supports this** ([LookupTax](https://lookuptax.com/docs/es/numero-identificacion-fiscal/guia-ruc-nicaragua));
no DGI source was found. Accepted as the format because a mismatch only sends a document to review.

## Mexico

**IVA rate — 16%, 0% for zero-rated goods.** Ley del IVA, article 1; consistent across sources.

**8% in the border regions — not in the law.** It comes from a decree of fiscal incentives for the northern
border region (first published 30 Dec 2018) and one for the southern border (30 Dec 2020), renewed year by
year. The latest renewal found runs to 31 Dec 2025
([Zeta Tijuana](https://zetatijuana.com/2024/12/prorrogan-estimulos-fiscales-para-la-franja-fronteriza-norte-de-mexico-hasta-diciembre-2025/));
**a 2026 renewal was not confirmed** (search results end in December 2025). That is why rates are
configuration: 8% stays in the default list until a source says otherwise, and dropping it is a settings
change.

**RFC — 12 characters for a company, 13 for an individual.** Company: 3 letters from the business name, the
incorporation date as YYMMDD, a 3-character homoclave. Individual: 4 letters from the names, the birth date,
the homoclave. Several sources agree
([N+](https://www.nmas.com.mx/economia/finanzas-personales/sat-que-significan-los-digitos-del-rfc-y-homoclave/),
[Edifact](https://www.edifact.com.mx/masinfo/homoclave-rfc)). The homoclave is assigned by the SAT; its check
digit is **not** verified here.

## Open

- Read the official texts (La Gaceta for Ley 822; the DOF for the 2026 border decree) before any client uses
  these defaults in production.
- Colombia and Costa Rica are out of the MVP; their profiles slot in as further `RateListTaxProfile`s.
