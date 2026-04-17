using Suterusu.Configuration;

namespace Suterusu.Services
{
    public static class OcrClientFactory
    {
        public static IOcrClient Create(ILogger logger, AppConfig config)
        {
            if (!config.OcrEnabled)
                return null;

            if (config.OcrProvider == OcrProvider.HuggingFace)
            {
                return new HuggingFaceOcrClient(
                    logger,
                    config.OcrHfToken,
                    config.OcrHfModel);
            }
            else if (config.OcrProvider == OcrProvider.LlamaCpp)
            {
                return new LlamaCppOcrClient(
                    logger,
                    config.OcrLlamaCppUrl,
                    config.OcrLlamaCppModel);
            }
            return null;
        }
    }
}