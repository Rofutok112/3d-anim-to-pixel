using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public static class PixelAnimationCli
    {
        private const string ConfigArg = "-animToPixelConfig";
        private const string BatchAssetArg = "-animToPixelBatchAsset";
        private const string ResultArg = "-animToPixelResult";

        public static void Export()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                var resultPath = GetArgValue(args, ResultArg);
                var result = ExportFromArgs(args);
                WriteResult(result, resultPath);
                Debug.Log($"3D Anim To Pixel CLI exported {result.GeneratedFiles.Count} files to {result.OutputDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static PixelAnimationExportResult ExportFromArgs(string[] args)
        {
            var batchAssetPath = GetArgValue(args, BatchAssetArg);
            if (!string.IsNullOrWhiteSpace(batchAssetPath))
            {
                var batchSettings = AssetDatabase.LoadAssetAtPath<PixelAnimationBatchSettings>(batchAssetPath);
                if (batchSettings == null)
                {
                    throw new ArgumentException($"Batch asset not found: {batchAssetPath}");
                }

                return PixelAnimationBatchExporter.Export(batchSettings);
            }

            var configPath = GetArgValue(args, ConfigArg);
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException($"Missing {ConfigArg} <json path> or {BatchAssetArg} <asset path>.");
            }

            return ExportFromConfig(configPath);
        }

        public static PixelAnimationExportResult ExportFromConfig(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("Config path is required.", nameof(configPath));
            }

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Config file was not found.", configPath);
            }

            var json = File.ReadAllText(configPath);
            var config = ParseConfigJson(json);
            if (config == null)
            {
                throw new ArgumentException($"Config file could not be parsed: {configPath}", nameof(configPath));
            }

            return PixelAnimationExporter.Export(CreateSettings(config));
        }

        public static PixelAnimationCliConfig ParseConfigJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Config JSON is required.", nameof(json));
            }

            var config = JsonUtility.FromJson<PixelAnimationCliConfig>(json);
            if (config == null)
            {
                return null;
            }

            ApplyStringEnum(json, "materialPreset", ref config.materialPreset);
            ApplyStringEnum(json, "outputMode", ref config.outputMode);
            ApplyStringEnum(json, "outlineMode", ref config.outlineMode);
            ApplyStringEnum(json, "colorCountPreset", ref config.colorCountPreset);
            ApplyStringEnum(json, "palettePreset", ref config.palettePreset);
            return config;
        }

        public static PixelAnimationExportSettings CreateSettings(PixelAnimationCliConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var prefab = LoadRequiredAsset<GameObject>(config.prefabPath, "prefabPath");
            var clip = LoadRequiredAsset<AnimationClip>(config.animationClipPath, "animationClipPath");

            return new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = string.IsNullOrWhiteSpace(config.outputFolderPath) ? "Assets/Exports" : config.outputFolderPath,
                Resolution = new Vector2Int(UsePositive(config.resolutionX, 64), UsePositive(config.resolutionY, 64)),
                Fps = UsePositive(config.fps, 12),
                UseFullClipLength = config.useFullClipLength,
                DurationSeconds = config.durationSeconds <= 0f ? 1f : config.durationSeconds,
                UseFrameRange = config.useFrameRange,
                StartFrame = Mathf.Max(0, config.startFrame),
                EndFrame = Mathf.Max(0, config.endFrame),
                BackgroundColor = config.backgroundColor,
                TransparentBackground = config.transparentBackground,
                CameraYaw = config.cameraYaw,
                CameraPitch = config.cameraPitch,
                CameraZoom = config.cameraZoom <= 0f ? 1.2f : config.cameraZoom,
                DirectionYaws = config.directionYaws == null || config.directionYaws.Length == 0 ? new[] { config.cameraYaw } : config.directionYaws,
                UseOrthographicCamera = config.useOrthographicCamera,
                UseLighting = config.useLighting,
                IncludeShadows = config.includeShadows,
                ForceUnlitMaterials = config.forceUnlitMaterials,
                MaterialPreset = config.materialPreset,
                OutputMode = config.outputMode,
                WriteMetadataJson = config.writeMetadataJson,
                ApplySpriteImportSettings = config.applySpriteImportSettings,
                WriteGifPreview = config.writeGifPreview,
                AutoTrim = config.autoTrim,
                TrimPadding = Mathf.Max(0, config.trimPadding),
                PixelSnap = config.pixelSnap,
                SnapModelToPixelGrid = config.snapModelToPixelGrid,
                ApplyOutline = config.applyOutline,
                OutlineColor = config.outlineColor,
                OutlineThickness = Mathf.Clamp(config.outlineThickness <= 0 ? 1 : config.outlineThickness, 1, 2),
                OutlineMode = config.outlineMode,
                EnhanceEdges = config.enhanceEdges,
                EdgeColor = config.edgeColor,
                EdgeThreshold = config.edgeThreshold <= 0f ? 0.25f : config.edgeThreshold,
                RemoveAntiAliasing = config.removeAntiAliasing,
                AlphaThreshold = config.alphaThreshold <= 0f ? 0.5f : config.alphaThreshold,
                ShadeSteps = config.shadeSteps,
                UseFixedShadeColors = config.useFixedShadeColors,
                ShadowColor = config.shadowColor,
                MidColor = config.midColor,
                HighlightColor = config.highlightColor,
                ReduceColors = config.reduceColors,
                ColorCountPreset = config.colorCountPreset,
                PalettePreset = config.palettePreset,
                MaxColors = config.maxColors <= 0 ? 16 : config.maxColors,
                UseDithering = config.useDithering,
                CustomPalette = config.customPalette ?? Array.Empty<Color>(),
                MotionDecimation = Mathf.Max(1, config.motionDecimation),
                FrameHold = Mathf.Max(1, config.frameHold)
            };
        }

        public static PixelAnimationCliConfig CreateConfig(PixelAnimationExportSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new PixelAnimationCliConfig
            {
                prefabPath = settings.Prefab == null ? string.Empty : AssetDatabase.GetAssetPath(settings.Prefab),
                animationClipPath = settings.AnimationClip == null ? string.Empty : AssetDatabase.GetAssetPath(settings.AnimationClip),
                outputFolderPath = settings.OutputFolderPath,
                resolutionX = settings.Resolution.x,
                resolutionY = settings.Resolution.y,
                fps = settings.Fps,
                useFullClipLength = settings.UseFullClipLength,
                durationSeconds = settings.DurationSeconds,
                useFrameRange = settings.UseFrameRange,
                startFrame = settings.StartFrame,
                endFrame = settings.EndFrame,
                backgroundColor = settings.BackgroundColor,
                transparentBackground = settings.TransparentBackground,
                cameraYaw = settings.CameraYaw,
                cameraPitch = settings.CameraPitch,
                cameraZoom = settings.CameraZoom,
                directionYaws = settings.DirectionYaws ?? Array.Empty<float>(),
                useOrthographicCamera = settings.UseOrthographicCamera,
                useLighting = settings.UseLighting,
                includeShadows = settings.IncludeShadows,
                forceUnlitMaterials = settings.ForceUnlitMaterials,
                materialPreset = settings.MaterialPreset,
                outputMode = settings.OutputMode,
                writeMetadataJson = settings.WriteMetadataJson,
                applySpriteImportSettings = settings.ApplySpriteImportSettings,
                writeGifPreview = settings.WriteGifPreview,
                autoTrim = settings.AutoTrim,
                trimPadding = settings.TrimPadding,
                pixelSnap = settings.PixelSnap,
                snapModelToPixelGrid = settings.SnapModelToPixelGrid,
                applyOutline = settings.ApplyOutline,
                outlineColor = settings.OutlineColor,
                outlineThickness = settings.OutlineThickness,
                outlineMode = settings.OutlineMode,
                enhanceEdges = settings.EnhanceEdges,
                edgeColor = settings.EdgeColor,
                edgeThreshold = settings.EdgeThreshold,
                removeAntiAliasing = settings.RemoveAntiAliasing,
                alphaThreshold = settings.AlphaThreshold,
                shadeSteps = settings.ShadeSteps,
                useFixedShadeColors = settings.UseFixedShadeColors,
                shadowColor = settings.ShadowColor,
                midColor = settings.MidColor,
                highlightColor = settings.HighlightColor,
                reduceColors = settings.ReduceColors,
                colorCountPreset = settings.ColorCountPreset,
                palettePreset = settings.PalettePreset,
                maxColors = settings.MaxColors,
                useDithering = settings.UseDithering,
                customPalette = settings.CustomPalette ?? Array.Empty<Color>(),
                motionDecimation = settings.MotionDecimation,
                frameHold = settings.FrameHold
            };
        }

        private static T LoadRequiredAsset<T>(string path, string fieldName) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"{fieldName} is required.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new ArgumentException($"{fieldName} asset not found or has wrong type: {path}");
            }

            return asset;
        }

        private static string GetArgValue(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        private static int UsePositive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static void ApplyStringEnum<TEnum>(string json, string fieldName, ref TEnum target) where TEnum : struct, Enum
        {
            if (!TryGetStringField(json, fieldName, out var value))
            {
                return;
            }

            if (!Enum.TryParse(value, true, out TEnum parsed))
            {
                throw new ArgumentException($"{fieldName} has unknown value '{value}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
            }

            target = parsed;
        }

        private static bool TryGetStringField(string json, string fieldName, out string value)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                value = string.Empty;
                return false;
            }

            value = Regex.Unescape(match.Groups["value"].Value);
            return true;
        }

        private static void WriteResult(PixelAnimationExportResult result, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var json = JsonUtility.ToJson(new PixelAnimationCliResult
            {
                outputDirectory = result.OutputDirectory,
                generatedFiles = result.GeneratedFiles.ToArray()
            }, true);
            File.WriteAllText(path, json);
        }
    }

    [Serializable]
    public sealed class PixelAnimationCliConfig
    {
        public string prefabPath;
        public string animationClipPath;
        public string outputFolderPath = "Assets/Exports";
        public int resolutionX = 64;
        public int resolutionY = 64;
        public int fps = 12;
        public bool useFullClipLength = true;
        public float durationSeconds = 1f;
        public bool useFrameRange;
        public int startFrame;
        public int endFrame = 11;
        public Color backgroundColor = Color.clear;
        public bool transparentBackground = true;
        public float cameraYaw;
        public float cameraPitch = 15f;
        public float cameraZoom = 1.2f;
        public float[] directionYaws = { 0f };
        public bool useOrthographicCamera = true;
        public bool useLighting = true;
        public bool includeShadows = true;
        public bool forceUnlitMaterials;
        public PixelAnimationMaterialPreset materialPreset = PixelAnimationMaterialPreset.SoftShade;
        public PixelAnimationOutputMode outputMode = PixelAnimationOutputMode.PngSequence;
        public bool writeMetadataJson;
        public bool applySpriteImportSettings = true;
        public bool writeGifPreview;
        public bool autoTrim;
        public int trimPadding = 1;
        public bool pixelSnap = true;
        public bool snapModelToPixelGrid;
        public bool applyOutline;
        public Color outlineColor = Color.black;
        public int outlineThickness = 1;
        public PixelOutlineMode outlineMode = PixelOutlineMode.Outside;
        public bool enhanceEdges;
        public Color edgeColor = Color.black;
        public float edgeThreshold = 0.25f;
        public bool removeAntiAliasing;
        public float alphaThreshold = 0.5f;
        public int shadeSteps;
        public bool useFixedShadeColors;
        public Color shadowColor = new Color(0.15f, 0.13f, 0.16f, 1f);
        public Color midColor = new Color(0.55f, 0.52f, 0.50f, 1f);
        public Color highlightColor = new Color(0.92f, 0.88f, 0.78f, 1f);
        public bool reduceColors;
        public PixelColorCountPreset colorCountPreset = PixelColorCountPreset.Sixteen;
        public PixelPalettePreset palettePreset = PixelPalettePreset.Auto;
        public int maxColors = 16;
        public bool useDithering;
        public Color[] customPalette = Array.Empty<Color>();
        public int motionDecimation = 1;
        public int frameHold = 1;
    }

    [Serializable]
    internal sealed class PixelAnimationCliResult
    {
        public string outputDirectory;
        public string[] generatedFiles;
    }
}
