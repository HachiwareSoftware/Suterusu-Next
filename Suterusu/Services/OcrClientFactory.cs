using Suterusu.Configuration;

namespace Suterusu.Services
{
    public static class OcrClientFactory
    {
        public static IOcrClient Create(ILogger logger, AppConfig config)
        {
            if (config.Ocr == null || !config.Ocr.Enabled)
                return null;

            switch (config.Ocr.Provider)
            {
                case OcrProvider.LlamaCpp:
                    return new LlamaCppOcrClient(
                        logger,
                        config.Ocr.LlamaCppUrl,
                        config.Ocr.LlamaCppModel);

                case OcrProvider.Zai:
                    return new ZaiOcrClient(
                        logger,
                        config.Ocr.ZaiToken,
                        config.Ocr.ZaiModel);

                case OcrProvider.Custom:
                    return new CustomOcrClient(
                        logger,
                        config.Ocr.CustomUrl,
                        config.Ocr.CustomApiKey,
                        config.Ocr.CustomModel);

                case OcrProvider.HuggingFace:
                    return new HuggingFaceOcrClient(
                        logger,
                        config.Ocr.HfUrl,
                        config.Ocr.HfToken,
                        config.Ocr.HfModel);

                case OcrProvider.WindowsOcr:
                    return new WindowsOcrClient(
                        new NLogLogger("Suterusu.OCR.Windows"),
                        config.Ocr.WindowsOcrLanguage);

                default:
                    return null;
            }
        }
    }
}