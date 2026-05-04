using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Models;

namespace Suterusu.Services
{
    /// <summary>
    /// Local OCR using Snipping Tool's OneOCR runtime through in-process native calls.
    /// The <paramref name="prompt"/> parameter is ignored; OneOCR is a direct OCR engine.
    /// </summary>
    public class OneOcrClient : IOcrClient
    {
        private static readonly object OcrLock = new object();

        private readonly ILogger _logger;
        private readonly string _runtimePath;

        public OneOcrClient(ILogger logger, string runtimePath)
        {
            _logger = logger;
            _runtimePath = runtimePath;
        }

        public Task<AiSingleAttemptResult> RunOcrAsync(
            byte[] imageData,
            string prompt,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(RunOcr(imageData));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(AiSingleAttemptResult.Fail("OneOCR was cancelled."));
            }
        }

        private AiSingleAttemptResult RunOcr(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return AiSingleAttemptResult.Fail("OneOCR received an empty image.");

            if (!Environment.Is64BitProcess)
                return AiSingleAttemptResult.Fail("OneOCR requires the app to run as a 64-bit process.");

            OneOcrRuntimeResolution runtime = OneOcrRuntimeLocator.Resolve(_runtimePath);
            if (!runtime.Success)
                return AiSingleAttemptResult.Fail(runtime.Error);

            try
            {
                using (Bitmap bitmap = DecodeAsBgraBitmap(imageData))
                {
                    string modelPath = Path.Combine(runtime.RuntimePath, OneOcrRuntimeLocator.OneOcrModelName);
                    string text;

                    lock (OcrLock)
                    {
                        using (var native = new OneOcrNative(runtime.RuntimePath))
                        {
                            text = native.Recognize(bitmap, modelPath);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(text))
                        return AiSingleAttemptResult.Fail("No text recognized in the selected region.");

                    _logger.Info("OneOCR recognized text successfully.");
                    return AiSingleAttemptResult.Ok(text);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("OneOCR failed.", ex);
                return AiSingleAttemptResult.Fail("OneOCR failed: " + ex.Message);
            }
        }

        private static Bitmap DecodeAsBgraBitmap(byte[] imageData)
        {
            using (var stream = new MemoryStream(imageData))
            using (var source = new Bitmap(stream))
            {
                var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                }

                return bitmap;
            }
        }
    }
}
