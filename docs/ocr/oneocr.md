# Windows OneOCR

`Windows OneOCR` uses the Windows 11 Snipping Tool OneOCR runtime through in-process native calls.

## Settings

- `Runtime Path`: optional folder containing `oneocr.dll`, `oneocr.onemodel`, and `onnxruntime.dll`.
- Leave runtime path blank to auto-detect Snipping Tool.

When auto-detect succeeds, Suterusu copies the needed runtime files into an app-local `OneOCR` folder before loading them. This avoids WindowsApps ACL issues when loading native DLLs.

## Requirements

- 64-bit Suterusu process.
- Windows 11 Snipping Tool package with OneOCR runtime files.
- Files required: `oneocr.dll`, `oneocr.onemodel`, `onnxruntime.dll`.

## Notes

- Local/offline OCR.
- Prompt is ignored because OneOCR is a direct OCR engine.
- Does not use Suterusu chat history.
- Uses reverse-engineered Snipping Tool runtime exports, so future Snipping Tool updates can break compatibility.
