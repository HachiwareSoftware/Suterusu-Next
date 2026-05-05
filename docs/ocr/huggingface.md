# Hugging Face OCR

`Hugging Face` OCR sends the selected screenshot to a Hugging Face OCR-compatible endpoint.

## Settings

- `Base URL`: API base URL. Default: `https://api.huggingface.co/v1`.
- `Token`: Hugging Face token. Required when OCR is enabled with this provider.
- `Model`: model name. Default: `google/ocr`.
- `Prompt`: sent as the request `prompt` field.

## Request Shape

Suterusu posts to:

```text
{Base URL}/vision/ocr
```

Body fields:

- `model`: configured model.
- `prompt`: OCR prompt, or default OCR prompt if blank.
- `inputs`: screenshot as `data:image/png;base64,...`.

The first item from the JSON array response is used as OCR output.

## Notes

- Hosted provider unless the base URL points to a self-hosted compatible service.
- Uses the OCR tab prompt.
- Does not use Suterusu chat history.
