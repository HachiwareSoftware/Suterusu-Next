# Suterusu (Next)

> [!WARNING]
> This is a rewrite of [Suterusu](https://github.com/HachiwareSoftware/Suterusu), focused on stablity and coding experience. While this rewrite is aimed at these purposes, not all features are tested or properly implemented, use at your own risk.

Communicates with AI using your system clipboard (Windows only).

## Features

+ Vibe-coded with a handful of agents (GPT-5.4, Claude Sonnet 4.6, Qwen 3.5 Plus, ~~MiniMax M2.5 Free~~) (im too poor omg).
+ Compatible with any OpenAI-like API endpoints.
+ Model fallback support (automatically tries backup models if primary fails)
+ It works on my machine.

## Usage

### First time

To open the settings UI (which you have to in order to configure), launch with `--open-settings` argument. After configuration, you can launch the app normally.

If you need debugging, enable it with `--debug` and it should show a console when launching.

Keybinds:
- F6: Clear chat history
- F7: Send clipboard content to AI
- F8: Copy AI response to clipboard
- F12: Close the application

## License

[MIT](./LICENSE)
