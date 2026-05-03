using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                WriteResult(false, null, "Usage: Suterusu.WindowsAiOcr <image-path>", 0);
                return 2;
            }

            string imagePath = args[0];
            if (!File.Exists(imagePath))
            {
                WriteResult(false, null, "Image file does not exist: " + imagePath, 0);
                return 2;
            }

            TextRecognizer recognizer;
            try
            {
                recognizer = await EnsureRecognizerReadyAsync();
            }
            catch (Exception ex)
            {
                WriteResult(false, null, "TextRecognizer init failed: " + DescribeException(ex), 0);
                return 1;
            }

            ImageBuffer imageBuffer;
            try
            {
                imageBuffer = await LoadImageBufferFromFileAsync(imagePath);
            }
            catch (Exception ex)
            {
                recognizer.Dispose();
                WriteResult(false, null, "Image load failed: " + DescribeException(ex), 0);
                return 1;
            }

            RecognizedText recognizedText;
            try
            {
                recognizedText = recognizer.RecognizeTextFromImage(imageBuffer);
            }
            catch (Exception ex)
            {
                imageBuffer.Dispose();
                recognizer.Dispose();
                WriteResult(false, null, "RecognizeTextFromImage failed: " + DescribeException(ex), 0);
                return 1;
            }

            imageBuffer.Dispose();
            recognizer.Dispose();

            var lines = recognizedText.Lines.Select(line => line.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            if (lines.Count == 0)
            {
                WriteResult(false, null, "No text recognized in the selected region.", 0);
                return 1;
            }

            WriteResult(true, string.Join("\n", lines), null, lines.Count);
            return 0;
        }
        catch (Exception ex)
        {
            WriteResult(false, null, "Unhandled: " + DescribeException(ex), 0);
            return 1;
        }
    }

    static async Task<TextRecognizer> EnsureRecognizerReadyAsync()
    {
        if (TextRecognizer.GetReadyState() == AIFeatureReadyState.NotReady)
        {
            AIFeatureReadyResult loadResult = await TextRecognizer.EnsureReadyAsync();
            if (loadResult.Status != AIFeatureReadyResultState.Success)
                throw new InvalidOperationException("EnsureReadyAsync status=" + loadResult.Status + ": " + (loadResult.ExtendedError?.Message ?? "(null)"));
        }

        return await TextRecognizer.CreateAsync();
    }

    static async Task<ImageBuffer> LoadImageBufferFromFileAsync(string filePath)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();

        if (bitmap == null)
            throw new InvalidOperationException("Could not decode image.");

        return ImageBuffer.CreateForSoftwareBitmap(bitmap);
    }

    static string DescribeException(Exception ex)
    {
        if (ex == null) return "(null)";
        var sb = new StringBuilder();
        sb.Append(ex.GetType().Name);
        sb.Append(": ");
        sb.Append(ex.Message);
        sb.Append(" [0x" + ex.HResult.ToString("X8") + "]");
        if (ex.InnerException != null)
        {
            sb.Append(" | Inner: ");
            sb.Append(DescribeException(ex.InnerException));
        }
        return sb.ToString();
    }

    static void WriteResult(bool success, string text, string error, int lineCount)
    {
        string json = JsonSerializer.Serialize(new { success, text, error, line_count = lineCount });
        Console.WriteLine(json);
    }
}
