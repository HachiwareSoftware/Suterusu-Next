# Reasoning Level

Suterusu can send a per-model `reasoning_effort` value for OpenAI-compatible chat requests when a provider supports it.

## Behavior

- `default` is the safe default and omits `reasoning_effort` from the request body.
- Any non-default value is sent as `reasoning_effort` exactly as configured.
- Suterusu does not infer reasoning support from model names or provider names.
- Fetch Models can populate reasoning levels only when the `/models` response exposes direct metadata.

## Supported Metadata Fields

When fetching models, Suterusu looks for these fields on each returned model object:

- `reasoning_efforts`
- `reasoning_effort`
- `supported_reasoning_efforts`
- `supported_reasoning_levels`
- `reasoning.levels`
- `capabilities.reasoning_efforts`

If none are present, the model editor shows `default` plus `custom...`.

## Custom Values

Use `custom...` when your provider supports a reasoning value that is not advertised by `/models`.

Examples include provider-specific values such as `none`, `low`, `medium`, `high`, or `xhigh`, but Suterusu treats them as opaque strings.

## Request Shape

Default reasoning level:

```json
{
  "model": "example-model",
  "messages": []
}
```

Non-default reasoning level:

```json
{
  "model": "example-model",
  "messages": [],
  "reasoning_effort": "high"
}
```
