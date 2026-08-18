namespace RichEra.Application.Abstractions;

// Platform OCR port: turns raw image bytes into recognized plain text. Implemented in the MAUI
// head (on-device: Windows.Media.Ocr / Apple Vision / Google ML Kit via Plugin.Maui.OCR) so the
// Application and Infrastructure layers stay platform-free and unit-testable with a fake.
public interface IReceiptTextRecognizer
{
    // Recognized text as newline-separated lines (row breaks preserved where the engine gives
    // them, since the receipt parser keys off line structure). Empty string if nothing was read.
    Task<string> RecognizeTextAsync(byte[] imageData, CancellationToken ct = default);
}
