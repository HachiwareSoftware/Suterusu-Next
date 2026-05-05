# PaddleX / PP-OCRv5

`PaddleX` OCR sends the selected screenshot to a local PaddleX General OCR pipeline server. This mode is suited for PP-OCRv5 and other PaddleX OCR pipelines.

## Server Setup

Install PaddleX serving support:

```powershell
paddlex --install serving
```

Start the OCR pipeline server:

```powershell
paddlex --serve --pipeline OCR --host 0.0.0.0 --port 8080
```

## Settings

- `Server URL`: PaddleX serving URL. Default: `http://localhost:8080`.

## Request Shape

Suterusu posts to:

```text
{Server URL}/ocr
```

Body fields:

- `file`: screenshot as base64 PNG bytes.
- `fileType`: `1` for image input.
- `visualize`: `false` to avoid returning rendered images.

Suterusu reads text from:

- `result.ocrResults[*].prunedResult.rec_texts`
- `result.ocrResults[*].prunedResult.recTexts`

## Notes

- Local if PaddleX server runs locally.
- Prompt is ignored because PaddleX is a direct OCR engine.
- Does not use Suterusu chat history.
