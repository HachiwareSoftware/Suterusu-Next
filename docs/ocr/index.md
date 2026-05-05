# OCR Modes

Suterusu OCR captures a screen region with the OCR hotkey, sends the captured PNG to the selected OCR provider, stores the response as the last AI response, and notifies when done.

## Shared Settings

- `OCR Hotkey`: starts region selection. Press it again to cancel selection.
- `Prompt`: instruction sent to prompt-aware OCR providers.
- `Use clipboard text as prompt if not empty`: replaces the OCR prompt for that request.
- `Timeout (ms)`: request or local OCR timeout.
- `Max Tokens`: response limit for OpenAI-compatible OCR providers.
- `Downscale image`: resizes large screenshots before sending.
- `Max dimension`: largest width/height after downscaling.

Direct OCR engines ignore the prompt because they only extract text. Prompt-aware providers use it as instruction text.

## Providers

- [llama.cpp](llama-cpp.md): local OpenAI-compatible vision server.
- [Z.ai GLM OCR](zai.md): hosted Z.ai layout parsing/OCR API.
- [Hugging Face OCR](huggingface.md): hosted Hugging Face OCR-compatible endpoint.
- [PaddleX / PP-OCRv5](paddlex.md): local PaddleX OCR pipeline server.
- [Windows OneOCR](oneocr.md): local Snipping Tool OneOCR runtime through in-process native calls.
- [Windows OCR](windows-ocr.md): built-in `Windows.Media.Ocr` engine.
- [Windows AI OCR](windows-ai.md): Windows App SDK OCR helper for Copilot+ PCs.
- [VLM Chat](vlm-chat.md): sends the screenshot directly to configured chat vision models.
- [Custom OpenAI-Compatible](custom-openai.md): custom vision chat-completions endpoint.
