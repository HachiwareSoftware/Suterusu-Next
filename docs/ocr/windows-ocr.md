# Windows OCR

`Windows OCR` uses the built-in `Windows.Media.Ocr` engine.

## Settings

- `Language`: optional BCP-47 OCR language tag, such as `en-US` or `vi-VN`.
- Blank language uses Windows user profile languages.

## Requirements

- Windows OCR language packs installed.
- Captured region must fit the Windows OCR maximum image dimension.

Install OCR languages through Windows Settings:

```text
Settings > Time & language > Language & region > Add a language
```

Enable the OCR capability for the language if Windows does not install it automatically.

## Notes

- Local/offline OCR.
- Prompt is ignored because Windows OCR is a direct OCR engine.
- Does not use Suterusu chat history.
- Best for simple text extraction with installed languages.
