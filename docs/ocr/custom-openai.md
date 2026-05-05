# Custom OpenAI-Compatible OCR

`Custom` OCR sends the selected screenshot to a custom OpenAI-compatible vision chat-completions endpoint.

## Settings

- `Base URL`: endpoint base URL. Required.
- `API Key`: bearer token. Required.
- `Model`: vision-capable model name. Required.
- `Prompt`: sent as the text part next to the image.
- `Max Tokens`: sent as `max_tokens`.

## Request Shape

Suterusu posts to:

```text
{Base URL}/v1/chat/completions
```

Body fields:

- `model`: configured model.
- `messages[0].role`: `user`.
- `messages[0].content[0]`: text prompt.
- `messages[0].content[1]`: screenshot as `data:image/png;base64,...` image URL.
- `max_tokens`: configured token limit.

The OCR output is read from `choices[0].message.content`.

## Notes

- Use this when your OCR/VLM provider is OpenAI-compatible but not covered by a dedicated mode.
- Screenshots leave your machine unless the endpoint is local.
- Does not use Suterusu chat history.
