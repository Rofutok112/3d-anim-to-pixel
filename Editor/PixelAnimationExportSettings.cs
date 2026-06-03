using System;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public enum PixelAnimationOutputMode
    {
        PngSequence,
        SpriteSheet,
        Both
    }

    public enum PixelAnimationMaterialPreset
    {
        SoftShade,
        Flat,
        HighContrast,
        Silhouette
    }

    public enum PixelPreviewScaleMode
    {
        Fit,
        OneX,
        TwoX,
        FourX
    }

    public enum PixelPreviewBackgroundMode
    {
        Solid,
        Checker,
        Grid
    }

    public enum PixelColorCountPreset
    {
        Custom = 0,
        Four = 4,
        Eight = 8,
        Sixteen = 16,
        ThirtyTwo = 32
    }

    public enum PixelPalettePreset
    {
        Auto,
        Custom,
        GameBoy,
        Pico8,
        DawnBringer16
    }

    public enum PixelOutlineMode
    {
        Outside,
        Inside,
        Both
    }

    [Serializable]
    public sealed class PixelAnimationExportSettings
    {
        public GameObject Prefab;
        public AnimationClip AnimationClip;
        public string OutputFolderPath = "Assets/Exports";
        public Vector2Int Resolution = new Vector2Int(64, 64);
        public int Fps = 12;
        public bool UseFullClipLength = true;
        public float DurationSeconds = 1f;
        public bool UseFrameRange;
        public int StartFrame;
        public int EndFrame = 11;
        public Color BackgroundColor = Color.clear;
        public bool TransparentBackground = true;
        public float CameraYaw;
        public float CameraPitch = 15f;
        public float CameraZoom = 1.2f;
        public float[] DirectionYaws = { 0f };
        public bool UseOrthographicCamera = true;
        public bool UseLighting = true;
        public bool IncludeShadows = true;
        public bool ForceUnlitMaterials;
        public PixelAnimationMaterialPreset MaterialPreset = PixelAnimationMaterialPreset.SoftShade;
        public PixelAnimationOutputMode OutputMode = PixelAnimationOutputMode.PngSequence;
        public bool WriteMetadataJson;
        public bool ApplySpriteImportSettings = true;
        public bool WriteGifPreview;
        public bool AutoTrim;
        public int TrimPadding = 1;
        public bool PixelSnap = true;
        public bool SnapModelToPixelGrid;
        public bool ApplyOutline;
        public Color OutlineColor = Color.black;
        public int OutlineThickness = 1;
        public PixelOutlineMode OutlineMode = PixelOutlineMode.Outside;
        public bool EnhanceEdges;
        public Color EdgeColor = Color.black;
        public float EdgeThreshold = 0.25f;
        public bool RemoveAntiAliasing;
        public float AlphaThreshold = 0.5f;
        public int ShadeSteps;
        public bool UseFixedShadeColors;
        public Color ShadowColor = new Color(0.15f, 0.13f, 0.16f, 1f);
        public Color MidColor = new Color(0.55f, 0.52f, 0.50f, 1f);
        public Color HighlightColor = new Color(0.92f, 0.88f, 0.78f, 1f);
        public bool ReduceColors;
        public PixelColorCountPreset ColorCountPreset = PixelColorCountPreset.Sixteen;
        public PixelPalettePreset PalettePreset = PixelPalettePreset.Auto;
        public int MaxColors = 16;
        public bool UseDithering;
        public Color[] CustomPalette = Array.Empty<Color>();
        public int MotionDecimation = 1;
        public int FrameHold = 1;

        public float ExportDuration
        {
            get
            {
                if (AnimationClip == null)
                {
                    return 0f;
                }

                return UseFullClipLength ? AnimationClip.length : Mathf.Max(0.01f, DurationSeconds);
            }
        }

        public int BaseFrameCount
        {
            get
            {
                if (AnimationClip == null || Fps <= 0)
                {
                    return 0;
                }

                return Mathf.Max(1, Mathf.CeilToInt(ExportDuration * Fps));
            }
        }

        public int FrameCount
        {
            get
            {
                return SampleFrameCount * Mathf.Max(1, FrameHold);
            }
        }

        public int RangeFrameCount
        {
            get
            {
                var baseFrameCount = BaseFrameCount;
                if (baseFrameCount == 0)
                {
                    return 0;
                }

                if (!UseFrameRange)
                {
                    return baseFrameCount;
                }

                var startFrame = Mathf.Clamp(StartFrame, 0, baseFrameCount - 1);
                var endFrame = Mathf.Clamp(EndFrame, startFrame, baseFrameCount - 1);
                return endFrame - startFrame + 1;
            }
        }

        public int SampleFrameCount
        {
            get
            {
                var rangeFrameCount = RangeFrameCount;
                if (rangeFrameCount == 0)
                {
                    return 0;
                }

                return Mathf.CeilToInt(rangeFrameCount / (float)Mathf.Max(1, MotionDecimation));
            }
        }

        public int EffectiveMaxColors
        {
            get
            {
                return ColorCountPreset == PixelColorCountPreset.Custom
                    ? MaxColors
                    : Mathf.Max(2, (int)ColorCountPreset);
            }
        }
    }
}
