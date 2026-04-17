using Suterusu.Configuration;

namespace Suterusu.Services
{
    public static class OcrClientFactory
    {
        public static IOcrClient Create(ILogger logger, AppConfig config)
        {
            if (config.Ocr == null || !config.Ocr.Enabled)
                return null;

            if (config.Ocr.Provider == OcrProvider.HuggingFace)
            {
                return new HuggingFaceOcrClient(
                    logger,
                    config.Ocr.HfToken,
                    config.Ocr.HfModel);
            }
            else if (config.Ocr.Provider == OcrProvider.LlamaCpp)
            {
                return new LlamaCppOcrClient(
                    logger,
                    config.Ocr.LlamaCppUrl,
                    config.Ocr.LlamaCppModel);
            }
            return null;
        }
    }
}