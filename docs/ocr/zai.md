# Z.ai GLM OCR

`Z.ai` OCR sends the selected screenshot to Z.ai's layout parsing endpoint.

## Settings

- `Token`: Z.ai API token. Required when OCR is enabled with this provider.
- `Model`: model name. Default: `glm-ocr`.
- `Prompt`: sent as the request `prompt` field.

## Request Shape

Suterusu posts to:

```text
https://api.z.ai/api/paas/v4/layout_parsing
```

Body fields:

- `model`: configured model.
- `prompt`: OCR prompt, or default OCR prompt if blank.
- `file`: screenshot as `data:image/png;base64,...`.

The response text is read from `text` or `parsed_text`.

## Notes

- Hosted provider; screenshots leave your machine.
- Uses the OCR tab prompt.
- Does not use Suterusu chat history.
