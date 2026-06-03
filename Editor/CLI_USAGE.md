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
