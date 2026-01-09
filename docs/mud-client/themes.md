# MUD Client Themes

Themes control the color scheme used by the text client. Fonts are handled by
your terminal emulator, not the client.

## Presets

- `ember` (default)
- `dusk`
- `terminal`
- `parchment`

## Custom Theme

Set `theme` to `custom` in `config.json` and provide any of these fields:

```json
{
  "theme": "custom",
  "customTheme": {
    "normalForeground": "BrightWhite",
    "normalBackground": "Black",
    "accentForeground": "BrightCyan",
    "accentBackground": "Black",
    "mutedForeground": "DarkGray"
  }
}
```

## In-Client Commands

- `/theme list`
- `/theme ember`
- `/theme custom`
