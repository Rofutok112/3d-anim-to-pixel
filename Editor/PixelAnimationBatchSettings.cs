using UnityEngine;
using UnityEditor.Animations;

namespace AnimToPixel.Editor
{
    [CreateAssetMenu(menuName = "3D Anim To Pixel/Batch Settings", fileName = "PixelAnimationBatchSettings")]
    public sealed class PixelAnimationBatchSettings : ScriptableObject
    {
        public GameObject Prefab;
        public AnimatorController AnimatorController;
        public AnimationClip[] AnimationClips = { };
        public string OutputFolderPath = "Assets/Exports";
        public Vector2Int Resolution = new Vector2Int(64, 64);
        public int Fps = 12;
        public bool UseFullClipLength = true;
        public float DurationSeconds = 1f;
        public bool UseFrameRange;
        public int StartFrame;
        public int EndFrame = 11;
        public bool TransparentBackground = true;
        public Color BackgroundColor = Color.clear;
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
        public Color[] CustomPalette = { };
        public int MotionDecimation = 1;
        public int FrameHold = 1;

        public PixelAnimationExportSettings CreateSettings(AnimationClip clip)
        {
            return new PixelAnimationExportSettings
            {
                Prefab = Prefab,
                AnimationClip = clip,
                OutputFolderPath = OutputFolderPath,
                Resolution = Resolution,
                Fps = Fps,
                UseFullClipLength = UseFullClipLength,
                DurationSeconds = DurationSeconds,
                UseFrameRange = UseFrameRange,
                StartFrame = StartFrame,
                EndFrame = EndFrame,
                TransparentBackground = TransparentBackground,
                BackgroundColor = BackgroundColor,
                CameraPitch = CameraPitch,
                CameraZoom = CameraZoom,
                DirectionYaws = DirectionYaws,
                UseOrthographicCamera = UseOrthographicCamera,
                UseLighting = UseLighting,
                IncludeShadows = IncludeShadows,
                ForceUnlitMaterials = ForceUnlitMaterials,
                MaterialPreset = MaterialPreset,
                OutputMode = OutputMode,
                WriteMetadataJson = WriteMetadataJson,
                ApplySpriteImportSettings = ApplySpriteImportSettings,
                WriteGifPreview = WriteGifPreview,
                AutoTrim = AutoTrim,
                TrimPadding = TrimPadding,
                PixelSnap = PixelSnap,
                SnapModelToPixelGrid = SnapModelToPixelGrid,
                ApplyOutline = ApplyOutline,
                OutlineColor = OutlineColor,
                OutlineThickness = OutlineThickness,
                OutlineMode = OutlineMode,
                EnhanceEdges = EnhanceEdges,
                EdgeColor = EdgeColor,
                EdgeThreshold = EdgeThreshold,
                RemoveAntiAliasing = RemoveAntiAliasing,
                AlphaThreshold = AlphaThreshold,
                ShadeSteps = ShadeSteps,
                UseFixedShadeColors = UseFixedShadeColors,
                ShadowColor = ShadowColor,
                MidColor = MidColor,
                HighlightColor = HighlightColor,
                ReduceColors = ReduceColors,
                ColorCountPreset = ColorCountPreset,
                PalettePreset = PalettePreset,
                MaxColors = MaxColors,
                UseDithering = UseDithering,
                CustomPalette = CustomPalette,
                MotionDecimation = MotionDecimation,
                FrameHold = FrameHold
            };
        }
    }
}
