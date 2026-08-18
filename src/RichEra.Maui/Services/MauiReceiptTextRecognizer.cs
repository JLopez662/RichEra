using Plugin.Maui.OCR;
using RichEra.Application.Abstractions;

namespace RichEra.Maui.Services;

// On-device OCR adapter. Wraps Plugin.Maui.OCR, which uses the native text recognizer on each
// platform — Windows.Media.Ocr on Windows, Apple Vision on iOS/macOS, Google ML Kit on Android.
// Nothing leaves the device; there are no API keys, quotas, or network calls.
public sealed class MauiReceiptTextRecognizer : IReceiptTextRecognizer
{
    private readonly IOcrService _ocr;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    // DI supplies the IOcrService registered by builder.UseOcr(); the fallback keeps the type
    // usable outside DI (and mirrors the plugin's own documented pattern).
    public MauiReceiptTextRecognizer(IOcrService? ocr = null) => _ocr = ocr ?? OcrPlugin.Default;

    public async Task<string> RecognizeTextAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (imageData is null || imageData.Length == 0)
            return string.Empty;

        await EnsureInitializedAsync(ct);

        // tryHard: true asks the engine for its most thorough pass — worth it for the small,
        // dense text on a receipt.
        var result = await _ocr.RecognizeTextAsync(imageData, tryHard: true, ct);
        if (result is null || !result.Success)
            return string.Empty;

        // Prefer the line list (it preserves the row breaks the parser keys off); fall back to
        // the flattened AllText if the platform didn't populate Lines.
        return result.Lines is { Count: > 0 }
            ? string.Join('\n', result.Lines)
            : result.AllText ?? string.Empty;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (!_initialized)
            {
                await _ocr.InitAsync(ct);
                _initialized = true;
            }
        }
        finally
        {
            _initGate.Release();
        }
    }
}
