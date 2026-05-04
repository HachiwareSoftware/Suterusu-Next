# VLM Chat Screenshot Mode

`VLM Chat` sends the selected screenshot region directly to the configured chat models instead of running OCR first. Use it with OpenAI-compatible vision language models, including providers exposed through CLIProxyAPI.

## Setup

1. Open settings.
2. In the Chat tab, add at least one model entry that supports image input.
3. Set the entry capability to `Vision`, or leave it as `Auto` and let the provider response decide.
4. In the OCR tab, select `VLM Chat` as the provider.
5. Set the OCR prompt to the image instruction you want, such as `Describe this screenshot and answer any visible question.`

`VLM Chat` does not create a CLIProxyAPI endpoint automatically. If you want to use CLIProxyAPI, add it as a normal Chat tab model entry.

## Prompt Behavior

`VLM Chat` uses both prompt fields:

- Chat system prompt: sent as the `system` message.
- OCR prompt: sent as the text part next to the screenshot image.

If `Use clipboard text as prompt if not empty` is enabled, clipboard text replaces the OCR prompt for that request.

## Chat History

`VLM Chat` participates in chat history. After a successful response, Suterusu stores the user turn as text only:

```text
[Image] <prompt>
```

The screenshot bytes are not stored in history.

## Model Capability

Each Chat tab model entry has a capability setting:

- `Auto`: eligible for VLM Chat. If it rejects image input, Suterusu falls back to the next eligible model.
- `Text only`: skipped by VLM Chat.
- `Vision`: eligible for VLM Chat.

Normal clipboard chat ignores this setting.

## Optional OCR Fallback

The OCR tab has an optional fallback toggle for `VLM Chat`. It is disabled by default.

When enabled, Suterusu runs the selected OCR fallback provider only after all eligible VLM models fail. The fallback provider cannot be `VLM Chat`.
