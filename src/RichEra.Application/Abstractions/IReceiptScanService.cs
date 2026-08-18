using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// Scans a receipt photo into candidate purchase lines. Orchestrates the platform OCR
// (IReceiptTextRecognizer) and the receipt-text parser. The result is always a *proposal*: the
// scan page lets the user edit, drop, and confirm lines before any of them become transactions.
public interface IReceiptScanService
{
    Task<ReceiptScanResult> ScanAsync(byte[] imageData, CancellationToken ct = default);
}
