# Changelog

## 0.1.3

- Serialize concurrent render/export operations in one Unity Editor process to avoid temporary camera, layer, and render target interference.

## 0.1.2

- Allow CLI JSON enum fields to use documented string names such as `"Both"` and `"Pico8"`.
- Keep numeric enum values supported for existing configs.

## 0.1.1

- Expose all export settings through the CLI JSON config.
- Include the full CLI-compatible settings object in metadata JSON output.
- Document the complete CLI config schema.

## 0.1.0

- Initial UPM package layout.
- Includes the editor converter window, exporter, GIF writer, batch settings, CLI entry point, and Edit Mode tests.
