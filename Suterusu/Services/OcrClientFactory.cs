using Suterusu.Configuration;

namespace Suterusu.Services
{
    public static class OcrClientFactory
    {
        public static IOcrClient Create(ILogger logger, AppConfig config)
        {
            if (config.Ocr == null || !config.Ocr.Enabled)
                return null;

            return Create(logger, config, config.Ocr.Provider);
        }

        public static IOcrClient Create(ILogger logger, AppConfig config, OcrProvider provider)
        {
            if (config.Ocr == null)
                return null;

            switch (provider)
            {
                case OcrProvider.LlamaCpp:
                    return new LlamaCppOcrClient(
                        logger,
                        config.Ocr.LlamaCppUrl,
                        config.Ocr.LlamaCppModel,
                        config.Ocr.MaxTokens);

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
                        config.Ocr.CustomModel,
                        config.Ocr.MaxTokens);

                case OcrProvider.HuggingFace:
                    return new HuggingFaceOcrClient(
                        logger,
                        config.Ocr.HfUrl,
                        config.Ocr.HfToken,
                        config.Ocr.HfModel);

                case OcrProvider.PaddleX:
                    return new PaddleXOcrClient(
                        logger,
                        config.Ocr.PaddleXUrl);

                case OcrProvider.OneOcr:
                    return new OneOcrClient(
                        new NLogLogger("Suterusu.OCR.OneOCR"),
                        config.Ocr.OneOcrRuntimePath);

                case OcrProvider.WindowsOcr:
                    return new WindowsOcrClient(
                        new NLogLogger("Suterusu.OCR.Windows"),
                        config.Ocr.WindowsOcrLanguage);

                case OcrProvider.WindowsAi:
                    return new WindowsAiOcrClient(
                        new NLogLogger("Suterusu.OCR.WindowsAI"));

                default:
                    return null;
            }
        }
    }
}
