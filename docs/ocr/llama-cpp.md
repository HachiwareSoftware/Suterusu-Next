# llama.cpp OCR

`llama.cpp` OCR sends the selected screenshot to a local OpenAI-compatible `/v1/chat/completions` server with image input.

## Settings

- `Server URL`: base llama.cpp server URL. Default: `http://localhost:8080`.
- `Model`: model name sent in the request. Default: `ggml-org/GLM-OCR-GGUF`.
- `Prompt`: sent as a `system` message.
- `Max Tokens`: sent as `max_tokens`.

If the URL does not already end with `/chat/completions`, Suterusu appends `/v1/chat/completions`.

## Request Shape

Suterusu sends:

- `model`: configured model.
- `max_tokens`: configured token limit.
- `messages[0]`: `system` prompt from the OCR tab.
- `messages[1]`: `user` message containing the screenshot as a `data:image/png;base64,...` image URL.

## Notes

- Runs locally if your llama.cpp server and model are local.
- Requires a vision-capable OCR/VLM model.
- Does not use Suterusu chat history.
