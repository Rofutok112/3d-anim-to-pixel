# 3D Anim To Pixel CLI Usage

This editor tool can be controlled from Unity batchmode without opening the GUI.

## Export From JSON

Run Unity with `-executeMethod AnimToPixel.Editor.PixelAnimationCli.Export` and pass a JSON config path.

```powershell
Unity.exe -batchmode -quit `
  -projectPath "D:\Users\rento\Documents\Unity Projects\3Danim2bit" `
  -executeMethod AnimToPixel.Editor.PixelAnimationCli.Export `
  -animToPixelConfig "D:\path\anim-to-pixel.json" `
  -animToPixelResult "D:\path\result.json"
```

Required JSON fields:

- `prefabPath`: Unity project asset path to a prefab.
- `animationClipPath`: Unity project asset path to an animation clip.

Minimal config:

```json
{
  "prefabPath": "Assets/Characters/Hero.prefab",
  "animationClipPath": "Assets/Animations/Run.anim",
  "outputFolderPath": "Assets/Exports",
  "resolutionX": 64,
  "resolutionY": 64,
  "fps": 12
}
```

Pixel-art style config:

```json
{
  "prefabPath": "Assets/Characters/Hero.prefab",
  "animationClipPath": "Assets/Animations/Run.anim",
  "outputFolderPath": "Assets/Exports",
  "resolutionX": 64,
  "resolutionY": 64,
  "fps": 12,
  "transparentBackground": true,
  "cameraYaw": 45,
  "cameraPitch": 15,
  "cameraZoom": 1.2,
  "useOrthographicCamera": true,
  "useLighting": false,
  "forceUnlitMaterials": true,
  "materialPreset": "Flat",
  "shadeSteps": 6,
  "removeAntiAliasing": true,
  "alphaThreshold": 0.5,
  "applyOutline": true,
  "outlineThickness": 1,
  "outlineMode": "Outside",
  "reduceColors": true,
  "colorCountPreset": "Sixteen",
  "palettePreset": "Pico8",
  "motionDecimation": 2,
  "frameHold": 2
}
```

Complete config fields:

```json
{
  "prefabPath": "Assets/Characters/Hero.prefab",
  "animationClipPath": "Assets/Animations/Run.anim",
  "outputFolderPath": "Assets/Exports",
  "resolutionX": 64,
  "resolutionY": 64,
  "fps": 12,
  "useFullClipLength": true,
  "durationSeconds": 1.0,
  "useFrameRange": false,
  "startFrame": 0,
  "endFrame": 11,
  "backgroundColor": { "r": 0.0, "g": 0.0, "b": 0.0, "a": 0.0 },
  "transparentBackground": true,
  "cameraYaw": 0.0,
  "cameraPitch": 15.0,
  "cameraZoom": 1.2,
  "directionYaws": [0.0],
  "useOrthographicCamera": true,
  "useLighting": true,
  "includeShadows": true,
  "forceUnlitMaterials": false,
  "materialPreset": "SoftShade",
  "outputMode": "PngSequence",
  "writeMetadataJson": false,
  "applySpriteImportSettings": true,
  "writeGifPreview": false,
  "autoTrim": false,
  "trimPadding": 1,
  "pixelSnap": true,
  "snapModelToPixelGrid": false,
  "applyOutline": false,
  "outlineColor": { "r": 0.0, "g": 0.0, "b": 0.0, "a": 1.0 },
  "outlineThickness": 1,
  "outlineMode": "Outside",
  "enhanceEdges": false,
  "edgeColor": { "r": 0.0, "g": 0.0, "b": 0.0, "a": 1.0 },
  "edgeThreshold": 0.25,
  "removeAntiAliasing": false,
  "alphaThreshold": 0.5,
  "shadeSteps": 0,
  "useFixedShadeColors": false,
  "shadowColor": { "r": 0.15, "g": 0.13, "b": 0.16, "a": 1.0 },
  "midColor": { "r": 0.55, "g": 0.52, "b": 0.5, "a": 1.0 },
  "highlightColor": { "r": 0.92, "g": 0.88, "b": 0.78, "a": 1.0 },
  "reduceColors": false,
  "colorCountPreset": "Sixteen",
  "palettePreset": "Auto",
  "maxColors": 16,
  "useDithering": false,
  "customPalette": [],
  "motionDecimation": 1,
  "frameHold": 1
}
```

## Export From Batch Asset

If a `PixelAnimationBatchSettings` asset already exists, pass it directly.

```powershell
Unity.exe -batchmode -quit `
  -projectPath "D:\Users\rento\Documents\Unity Projects\3Danim2bit" `
  -executeMethod AnimToPixel.Editor.PixelAnimationCli.Export `
  -animToPixelBatchAsset "Assets/Settings/MyBatch.asset" `
  -animToPixelResult "D:\path\result.json"
```

## Result JSON

When `-animToPixelResult` is provided, the CLI writes:

```json
{
  "outputDirectory": "Assets/Exports/Hero/Run",
  "generatedFiles": [
    "Assets/Exports/Hero/Run/Run_0000.png"
  ]
}
```

When `writeMetadataJson` is true, each export folder also receives a metadata JSON file. Its `settings` object contains the same fields as the complete CLI config, plus export-derived values such as `frameCount`, `startFrame`, `endFrame`, `directionIndex`, and `directionYaw`.

## Useful Enum Values

`materialPreset`:

- `SoftShade`
- `Flat`
- `HighContrast`
- `Silhouette`

`outputMode`:

- `PngSequence`
- `SpriteSheet`
- `Both`

`outlineMode`:

- `Outside`
- `Inside`
- `Both`

`colorCountPreset`:

- `Custom`
- `Four`
- `Eight`
- `Sixteen`
- `ThirtyTwo`

`palettePreset`:

- `Auto`
- `Custom`
- `GameBoy`
- `Pico8`
- `DawnBringer16`

## Notes For AI Agents

- Use Unity project asset paths for `prefabPath` and `animationClipPath`, not absolute filesystem paths.
- `shadeSteps` is `0` for off, or `2` to `32` for quantized shading.
- `palettePreset: "Custom"` requires `customPalette`.
- `useLighting: false` disables the export light. Use `forceUnlitMaterials: true` when the source material should ignore lighting too.
- Export and preview rendering use a dedicated render layer, so open scene objects should not appear in the output.
