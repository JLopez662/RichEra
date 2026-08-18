using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;

namespace RichEra.Infrastructure.Services;

// Composes the platform OCR (IReceiptTextRecognizer, implemented in the MAUI head) with the pure
// ReceiptParser. Stays platform-free so it can be exercised with a fake recognizer in tests.
public sealed class ReceiptScanService : IReceiptScanService
{
    private readonly IReceiptTextRecognizer _recognizer;

    public ReceiptScanService(IReceiptTextRecognizer recognizer) => _recognizer = recognizer;

    public async Task<ReceiptScanResult> ScanAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (imageData is null || imageData.Length == 0)
            return ReceiptScanResult.Empty;

        var text = await _recognizer.RecognizeTextAsync(imageData, ct);
        var lines = ReceiptParser.Parse(text);
        return new ReceiptScanResult(lines, text ?? string.Empty);
    }
}
