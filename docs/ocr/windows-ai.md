# Windows AI OCR

`Windows AI OCR` uses a helper executable built with modern Windows OCR APIs. The main app targets .NET Framework 4.8, so this provider runs the OCR helper out of process.

## Settings

No provider-specific settings are required in Suterusu.

The helper is expected at:

```text
WindowsAiOcr/Suterusu.WindowsAiOcr.exe
```

relative to the main application directory.

## Requirements

- Full Suterusu package that includes the `WindowsAiOcr` helper, or a local source build of the helper.
- Copilot+ PC with supported NPU for Windows AI text recognition APIs.

If the helper is missing, Suterusu returns an error explaining that the full package or source build is required.

## Notes

- Prompt is ignored because Windows AI OCR is a direct OCR engine.
- Does not use Suterusu chat history.
- This provider is separate from `Windows OCR` and `Windows OneOCR`.
