# Errors

Every error the API returns is an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem with a stable
`code` and a `type` linking to its entry here. An error for a whole request comes back as a problem (400 or
404). An error for one file of a batch comes back inside the batch's 200/202 response, next to that file,
because one bad file never sinks the rest.

## Batch

### batch-empty
`batch.empty` · 400. The request carried no files. Send them as `multipart/form-data`, each part named
`files`.

### batch-too_many_files
`batch.too_many_files` · 400. More files than one batch takes (`Upload:MaxFilesPerBatch`, 20 by default).
Split the upload.

### batch-unknown_country
`batch.unknown_country` · 400. The `country` form field names no tax profile. Use `NI` (Nicaragua), `MX`
(Mexico) or `XX` (generic rules). Without the field, the batch takes `Upload:DefaultCountry`.

### batch-not_found
`batch.not_found` · 404. No batch has that id.

## Document

### document-empty
`document.empty` · per file. The file has zero bytes.

### document-too_large
`document.too_large` · per file. The file is over `Upload:MaxFileBytes` (10 MB by default). A phone photo
is 3-5 MB; a larger one can be re-exported at a lower resolution without losing legibility.

### document-unsupported_format
`document.unsupported_format` · per file. Not a PDF, JPG or PNG, judged by the file's first bytes, not
its name. HEIC (iPhone) is not supported: export the photo as JPG.

### document-too_many_pages
`document.too_many_pages` · per file. A PDF over `Upload:MaxPdfPages` (5 by default). An invoice is one or
two pages; the cap bounds what one scanned PDF can cost, since the model reads every page as an image.

### document-unreadable
`document.unreadable` · per file. The file starts like a PDF or an image but cannot be opened as one:
truncated or corrupted.

### document-encrypted
`document.encrypted` · per file. A password-protected PDF. Upload an unprotected copy.

### document-image_too_large
`document.image_too_large` · per file. An image over 50 megapixels. The cap bounds the memory one upload
can make the server allocate.

### document-not_in_review
`document.not_in_review` · 409. Only a document waiting for review can be approved; this one is queued,
being read, already approved or failed.

### review-missing_invoice
`review.missing_invoice` · 400. An approval must carry the invoice as approved, in the body's `invoice`.

### review-duplicate_invoice
`review.duplicate_invoice` · 409. The approved data is an invoice another document already settled: same
issuer tax id, number and total ([ADR 0004](adr/0004-duplicate-invoices.md)). Approving it would put the
purchase in the books twice. The problem carries `originalId`, the settled document's id, so the reviewer can
open it; the document stays in review.

### document-not_found
`document.not_found` · 404. No document has that id.

## Examples

### sample-not_found
`sample.not_found` · 404. No example has that name. Only the files listed in the examples' `manifest.json` are
served, and none when `Samples:Path` is not configured.

## Export

### export-unknown_option
`export.unknown_option` · 400. `GET /v1/export` takes `format=xlsx` or `csv` and `lang=es` or `en`; both
are optional (Excel, Spanish headers).
