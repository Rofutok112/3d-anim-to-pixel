# 3D Anim To Pixel

Unity Editor tool for converting a prefab and animation clip into pixel-art-style images.

## Install From GitHub

In Unity, open **Window > Package Manager**, press **+**, choose **Install package from git URL...**, and enter:

```text
https://github.com/Rofutok112/3d-anim-to-pixel.git
```

## Usage

Open **Tools > 3D Anim To Pixel > Converter**.

1. Assign a prefab and an animation clip.
2. Set resolution, FPS, camera angle, material, lighting, outline, palette, and export mode.
3. Check the preview.
4. Press **Export**.

The tool can export PNG sequences, sprite sheets, and GIFs.

## CLI

CLI export is available through Unity batch mode:

```text
-executeMethod AnimToPixel.Editor.PixelAnimationCli.Export
```

See `Editor/CLI_USAGE.md` for arguments and examples.

## Tests

The package includes Edit Mode tests under `Tests/Editor`.

To make package tests visible in a Unity project, add this to the project's `Packages/manifest.json`:

```json
{
  "testables": [
    "com.rento.anim-to-pixel"
  ]
}
```
