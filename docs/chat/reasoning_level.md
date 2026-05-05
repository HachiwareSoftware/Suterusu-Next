# Reasoning Level

Suterusu can send a per-model `reasoning_effort` value for OpenAI-compatible chat requests when a provider supports it.

## Behavior

- `Default` is the safe default and omits `reasoning_effort` from the request body.
- Any non-default value is sent as `reasoning_effort` exactly as configured.
- Suterusu does not infer reasoning support from model names.
- Fetch Models can populate reasoning levels only when `/models` or linked model-detail metadata exposes exact level values.

## Supported Metadata Fields

When fetching models, Suterusu looks for these fields on each returned model object:

- `reasoning_efforts`
- `reasoning_effort`
- `supported_reasoning_efforts`
- `supported_reasoning_levels`
- `reasoning.levels`
- `reasoning.efforts`
- `reasoning.values`
- `reasoning.supported_efforts`
- `capabilities.reasoning_efforts`
- `capabilities.reasoning.levels`
- `capabilities.reasoning.efforts`

Suterusu does not treat `supported_parameters` values like `reasoning` or `include_reasoning` as reasoning levels. Those fields only prove the endpoint accepts a reasoning parameter; they do not say which values are valid.

If a model object links to a details endpoint through `links.details`, `details_url`, or `detailsUrl`, Suterusu fetches that details document and looks for the same explicit metadata fields on the root object, `data`, `result`, and `data.endpoints[*]`.

If none are present, the model editor shows `Default` plus `Custom...`.

## Custom Values

Use `Custom...` when your provider supports a reasoning value that is not advertised by `/models`.

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
