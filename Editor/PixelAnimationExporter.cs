using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public static class PixelAnimationExporter
    {
        private const int MinResolution = 8;
        private const int MaxResolution = 2048;
        private const int ExportLayer = 31;
        private const int ExportLayerMask = 1 << ExportLayer;
        private static readonly SemaphoreSlim RenderSemaphore = new SemaphoreSlim(1, 1);

        public static PixelAnimationExportResult Export(PixelAnimationExportSettings settings)
        {
            using var renderLock = EnterRenderOperation();
            return ExportInternal(settings);
        }

        private static PixelAnimationExportResult ExportInternal(PixelAnimationExportSettings settings)
        {
            Validate(settings);

            var generatedFiles = new List<string>();
            var directionYaws = GetDirectionYaws(settings);

            for (var directionIndex = 0; directionIndex < directionYaws.Length; directionIndex++)
            {
                var yaw = directionYaws[directionIndex];
                var outputDirectory = BuildOutputDirectory(settings, directionIndex, directionYaws.Length);
                Directory.CreateDirectory(outputDirectory);

                using var frameSet = CaptureFrames(settings, yaw, 0, settings.FrameCount);
                var frameFiles = WriteOutputs(settings, outputDirectory, directionIndex, yaw, frameSet.Frames);
                generatedFiles.AddRange(frameFiles);
            }

            if (generatedFiles.Any(IsProjectRelativePath))
            {
                AssetDatabase.Refresh();
            }

            return new PixelAnimationExportResult(
                BuildOutputDirectory(settings, 0, directionYaws.Length),
                generatedFiles);
        }

        public static async Task<PixelAnimationExportResult> ExportAsync(
            PixelAnimationExportSettings settings,
            Func<float, string, bool> progress)
        {
            using var renderLock = await EnterRenderOperationAsync();
            Validate(settings);

            var generatedFiles = new List<string>();
            var directionYaws = GetDirectionYaws(settings);
            var totalSteps = Mathf.Max(1, directionYaws.Length * (settings.FrameCount + GetEstimatedOutputStepCount(settings)));
            var completedSteps = 0;

            for (var directionIndex = 0; directionIndex < directionYaws.Length; directionIndex++)
            {
                var yaw = directionYaws[directionIndex];
                var outputDirectory = BuildOutputDirectory(settings, directionIndex, directionYaws.Length);
                Directory.CreateDirectory(outputDirectory);
                ThrowIfCanceled(progress, completedSteps / (float)totalSteps, $"Direction {directionIndex + 1}/{directionYaws.Length}: preparing");

                using var frameSet = await CaptureFramesAsync(
                    settings,
                    yaw,
                    0,
                    settings.FrameCount,
                    progress,
                    () => ++completedSteps / (float)totalSteps);
                var frameFiles = await WriteOutputsAsync(
                    settings,
                    outputDirectory,
                    directionIndex,
                    yaw,
                    frameSet.Frames,
                    progress,
                    () => ++completedSteps / (float)totalSteps);
                generatedFiles.AddRange(frameFiles);
            }

            if (generatedFiles.Any(IsProjectRelativePath))
            {
                AssetDatabase.Refresh();
            }

            ThrowIfCanceled(progress, 1f, "Export complete");
            return new PixelAnimationExportResult(
                BuildOutputDirectory(settings, 0, directionYaws.Length),
                generatedFiles);
        }

        public static Texture2D RenderPreview(PixelAnimationExportSettings settings, int frameIndex)
        {
            using var renderLock = EnterRenderOperation();
            Validate(settings);
            var yaw = GetDirectionYaws(settings)[0];
            var outputFrameIndex = Mathf.Clamp(frameIndex, 0, settings.FrameCount - 1);
            using var frameSet = CaptureFrames(settings, yaw, outputFrameIndex, 1);
            var preview = CloneTexture(frameSet.Frames[0]);
            preview.name = "Pixel Animation Preview";
            preview.hideFlags = HideFlags.HideAndDontSave;
            return preview;
        }

        public static void Validate(PixelAnimationExportSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.Prefab == null)
            {
                throw new ArgumentException("Prefab is required.", nameof(settings));
            }

            if (settings.AnimationClip == null)
            {
                throw new ArgumentException("AnimationClip is required.", nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.OutputFolderPath))
            {
                throw new ArgumentException("Output folder is required.", nameof(settings));
            }

            if (settings.Resolution.x < MinResolution || settings.Resolution.y < MinResolution)
            {
                throw new ArgumentException($"Resolution must be at least {MinResolution}x{MinResolution}.", nameof(settings));
            }

            if (settings.Resolution.x > MaxResolution || settings.Resolution.y > MaxResolution)
            {
                throw new ArgumentException($"Resolution must be {MaxResolution}x{MaxResolution} or smaller.", nameof(settings));
            }

            if (settings.Fps <= 0)
            {
                throw new ArgumentException("FPS must be greater than 0.", nameof(settings));
            }

            if (!settings.UseFullClipLength && settings.DurationSeconds <= 0f)
            {
                throw new ArgumentException("Duration must be greater than 0.", nameof(settings));
            }

            if (settings.UseFrameRange && settings.EndFrame < settings.StartFrame)
            {
                throw new ArgumentException("End Frame must be greater than or equal to Start Frame.", nameof(settings));
            }

            if (settings.TrimPadding < 0)
            {
                throw new ArgumentException("Trim Padding must be 0 or greater.", nameof(settings));
            }

            if (settings.ReduceColors
                && settings.PalettePreset == PixelPalettePreset.Custom
                && (settings.CustomPalette == null || settings.CustomPalette.Length < 2))
            {
                throw new ArgumentException("Color reduction requires at least 2 colors.", nameof(settings));
            }

            if (settings.OutlineThickness < 1 || settings.OutlineThickness > 2)
            {
                throw new ArgumentException("Outline Thickness must be 1 or 2.", nameof(settings));
            }

            if (settings.MotionDecimation < 1)
            {
                throw new ArgumentException("Motion Decimation must be 1 or greater.", nameof(settings));
            }

            if (settings.FrameHold < 1)
            {
                throw new ArgumentException("Frame Hold must be 1 or greater.", nameof(settings));
            }

            if (settings.ShadeSteps == 1 || settings.ShadeSteps < 0 || settings.ShadeSteps > 32)
            {
                throw new ArgumentException("Shade Steps must be 0, or between 2 and 32.", nameof(settings));
            }
        }

        private static IDisposable EnterRenderOperation()
        {
            if (!RenderSemaphore.Wait(0))
            {
                throw new InvalidOperationException("Another 3D Anim To Pixel render/export is already running. Wait for it to finish before starting another one.");
            }

            return new RenderOperationLock();
        }

        private static async Task<IDisposable> EnterRenderOperationAsync()
        {
            await RenderSemaphore.WaitAsync();
            return new RenderOperationLock();
        }

        private static CapturedFrameSet CaptureFrames(PixelAnimationExportSettings settings, float yaw, int startFrame = 0, int? limit = null)
        {
            var frames = new List<Texture2D>();
            using (var session = new FrameCaptureSession(settings, yaw))
            {
                var frameLimit = limit ?? settings.FrameCount;
                for (var offset = 0; offset < frameLimit; offset++)
                {
                    frames.Add(session.CaptureFrame(startFrame + offset));
                }
            }

            TrimFramesToSharedBounds(frames, settings.TrimPadding, settings.AutoTrim);
            return new CapturedFrameSet(frames);
        }

        private static async Task<CapturedFrameSet> CaptureFramesAsync(
            PixelAnimationExportSettings settings,
            float yaw,
            int startFrame,
            int limit,
            Func<float, string, bool> progress,
            Func<float> advanceProgress)
        {
            var frames = new List<Texture2D>();
            using (var session = new FrameCaptureSession(settings, yaw))
            {
                for (var offset = 0; offset < limit; offset++)
                {
                    frames.Add(session.CaptureFrame(startFrame + offset));
                    ThrowIfCanceled(progress, advanceProgress(), $"Rendering frame {offset + 1}/{limit}");
                    await Task.Yield();
                }
            }

            TrimFramesToSharedBounds(frames, settings.TrimPadding, settings.AutoTrim);
            return new CapturedFrameSet(frames);
        }

        private static IReadOnlyList<string> WriteOutputs(
            PixelAnimationExportSettings settings,
            string outputDirectory,
            int directionIndex,
            float yaw,
            IReadOnlyList<Texture2D> frames)
        {
            var generatedFiles = new List<string>();
            var clipName = SanitizeFileName(settings.AnimationClip.name);

            if (settings.OutputMode is PixelAnimationOutputMode.PngSequence or PixelAnimationOutputMode.Both)
            {
                for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                {
                    var filePath = Path.Combine(outputDirectory, $"{clipName}_{frameIndex:D4}.png").Replace('\\', '/');
                    File.WriteAllBytes(filePath, frames[frameIndex].EncodeToPNG());
                    generatedFiles.Add(filePath);
                }
            }

            if (settings.OutputMode is PixelAnimationOutputMode.SpriteSheet or PixelAnimationOutputMode.Both)
            {
                var spriteSheet = BuildSpriteSheet(frames, settings.Resolution);
                var filePath = Path.Combine(outputDirectory, $"{clipName}_sheet.png").Replace('\\', '/');
                File.WriteAllBytes(filePath, spriteSheet.EncodeToPNG());
                generatedFiles.Add(filePath);
                DestroyImmediateSafe(spriteSheet);
            }

            if (settings.WriteGifPreview)
            {
                var gifPath = Path.Combine(outputDirectory, $"{clipName}_preview.gif").Replace('\\', '/');
                PixelAnimationGifWriter.Write(gifPath, frames, settings.Fps);
                generatedFiles.Add(gifPath);
            }

            if (settings.WriteMetadataJson)
            {
                var metadataPath = Path.Combine(outputDirectory, $"{clipName}.json").Replace('\\', '/');
                File.WriteAllText(metadataPath, BuildMetadataJson(settings, directionIndex, yaw, frames.Count));
                generatedFiles.Add(metadataPath);
            }

            ApplySpriteImportSettings(generatedFiles, settings);

            return generatedFiles;
        }

        private static async Task<IReadOnlyList<string>> WriteOutputsAsync(
            PixelAnimationExportSettings settings,
            string outputDirectory,
            int directionIndex,
            float yaw,
            IReadOnlyList<Texture2D> frames,
            Func<float, string, bool> progress,
            Func<float> advanceProgress)
        {
            var generatedFiles = new List<string>();
            var clipName = SanitizeFileName(settings.AnimationClip.name);

            if (settings.OutputMode is PixelAnimationOutputMode.PngSequence or PixelAnimationOutputMode.Both)
            {
                for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                {
                    var filePath = Path.Combine(outputDirectory, $"{clipName}_{frameIndex:D4}.png").Replace('\\', '/');
                    File.WriteAllBytes(filePath, frames[frameIndex].EncodeToPNG());
                    generatedFiles.Add(filePath);
                    ThrowIfCanceled(progress, advanceProgress(), $"Writing PNG {frameIndex + 1}/{frames.Count}");
                    await Task.Yield();
                }
            }

            if (settings.OutputMode is PixelAnimationOutputMode.SpriteSheet or PixelAnimationOutputMode.Both)
            {
                var spriteSheet = BuildSpriteSheet(frames, settings.Resolution);
                var filePath = Path.Combine(outputDirectory, $"{clipName}_sheet.png").Replace('\\', '/');
                File.WriteAllBytes(filePath, spriteSheet.EncodeToPNG());
                generatedFiles.Add(filePath);
                DestroyImmediateSafe(spriteSheet);
                ThrowIfCanceled(progress, advanceProgress(), "Writing SpriteSheet");
                await Task.Yield();
            }

            if (settings.WriteGifPreview)
            {
                var gifPath = Path.Combine(outputDirectory, $"{clipName}_preview.gif").Replace('\\', '/');
                PixelAnimationGifWriter.Write(gifPath, frames, settings.Fps);
                generatedFiles.Add(gifPath);
                ThrowIfCanceled(progress, advanceProgress(), "Writing GIF preview");
                await Task.Yield();
            }

            if (settings.WriteMetadataJson)
            {
                var metadataPath = Path.Combine(outputDirectory, $"{clipName}.json").Replace('\\', '/');
                File.WriteAllText(metadataPath, BuildMetadataJson(settings, directionIndex, yaw, frames.Count));
                generatedFiles.Add(metadataPath);
                ThrowIfCanceled(progress, advanceProgress(), "Writing metadata");
                await Task.Yield();
            }

            await ApplySpriteImportSettingsAsync(generatedFiles, settings, progress, advanceProgress);
            return generatedFiles;
        }

        private static Texture2D BuildSpriteSheet(IReadOnlyList<Texture2D> frames, Vector2Int resolution)
        {
            var frameWidth = frames.Max(frame => frame.width);
            var frameHeight = frames.Max(frame => frame.height);
            var spriteSheet = new Texture2D(frameWidth * frames.Count, frameHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            spriteSheet.SetPixels(Enumerable.Repeat(Color.clear, spriteSheet.width * spriteSheet.height).ToArray());

            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var frame = frames[frameIndex];
                var offsetX = Mathf.Max(0, (frameWidth - frame.width) / 2);
                var offsetY = Mathf.Max(0, (frameHeight - frame.height) / 2);
                spriteSheet.SetPixels(frameIndex * frameWidth + offsetX, offsetY, frame.width, frame.height, frame.GetPixels());
            }

            spriteSheet.Apply(false);
            return spriteSheet;
        }

        private static string BuildMetadataJson(PixelAnimationExportSettings settings, int directionIndex, float yaw, int frameCount)
        {
            return JsonUtility.ToJson(new Metadata
            {
                settings = PixelAnimationCli.CreateConfig(settings),
                prefabName = settings.Prefab.name,
                animationClipName = settings.AnimationClip.name,
                frameCount = frameCount,
                startFrame = GetFirstExportFrame(settings),
                endFrame = GetSourceFrameForOutputFrame(settings, Mathf.Max(0, frameCount - 1)),
                baseFrameCount = settings.BaseFrameCount,
                rangeFrameCount = settings.RangeFrameCount,
                sampleFrameCount = settings.SampleFrameCount,
                effectiveMaxColors = settings.EffectiveMaxColors,
                directionIndex = directionIndex,
                directionYaw = yaw
            }, true);
        }

        private static void ProcessFrame(Texture2D frame, PixelAnimationExportSettings settings)
        {
            if (settings.RemoveAntiAliasing)
            {
                ApplyAlphaThreshold(frame, settings.AlphaThreshold);
            }

            if (settings.ShadeSteps > 1)
            {
                ApplyShadeSteps(frame, settings);
            }

            if (settings.ApplyOutline)
            {
                ApplyOutline(frame, settings);
            }

            if (settings.EnhanceEdges)
            {
                ApplyEdgeEnhancement(frame, settings);
            }

            if (settings.ReduceColors)
            {
                ApplyColorReduction(frame, settings);
            }
        }

        private static void TrimFramesToSharedBounds(IList<Texture2D> frames, int padding, bool trim)
        {
            if (frames == null || frames.Count == 0)
            {
                return;
            }

            if (!trim)
            {
                NormalizeFrameCanvases(frames);
                return;
            }

            if (!TryGetSharedOpaqueBounds(frames, padding, out var bounds))
            {
                NormalizeFrameCanvases(frames);
                return;
            }

            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                frames[index] = CropFrame(frame, bounds);
                DestroyImmediateSafe(frame);
            }

            NormalizeFrameCanvases(frames);
        }

        private static bool TryGetSharedOpaqueBounds(IList<Texture2D> frames, int padding, out RectInt bounds)
        {
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = -1;
            var maxY = -1;

            foreach (var frame in frames)
            {
                var pixels = frame.GetPixels32();
                for (var y = 0; y < frame.height; y++)
                {
                    for (var x = 0; x < frame.width; x++)
                    {
                        if (pixels[y * frame.width + x].a <= 8)
                        {
                            continue;
                        }

                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                bounds = default;
                return false;
            }

            var first = frames[0];
            minX = Mathf.Max(0, minX - padding);
            minY = Mathf.Max(0, minY - padding);
            maxX = Mathf.Min(first.width - 1, maxX + padding);
            maxY = Mathf.Min(first.height - 1, maxY + padding);
            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        private static Texture2D CropFrame(Texture2D source, RectInt bounds)
        {
            var cropped = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            cropped.SetPixels(source.GetPixels(bounds.x, bounds.y, bounds.width, bounds.height));
            cropped.Apply(false);
            return cropped;
        }

        private static Texture2D TrimTransparentPixels(Texture2D source, int padding)
        {
            var pixels = source.GetPixels32();
            var minX = source.width;
            var minY = source.height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < source.height; y++)
            {
                for (var x = 0; x < source.width; x++)
                {
                    if (pixels[y * source.width + x].a <= 8)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return CloneTexture(source);
            }

            minX = Mathf.Max(0, minX - padding);
            minY = Mathf.Max(0, minY - padding);
            maxX = Mathf.Min(source.width - 1, maxX + padding);
            maxY = Mathf.Min(source.height - 1, maxY + padding);

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var trimmed = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            trimmed.SetPixels(source.GetPixels(minX, minY, width, height));
            trimmed.Apply(false);
            return trimmed;
        }

        private static void NormalizeFrameCanvases(IList<Texture2D> frames)
        {
            if (frames == null || frames.Count <= 1)
            {
                return;
            }

            var width = frames.Max(frame => frame.width);
            var height = frames.Max(frame => frame.height);
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                if (frame.width == width && frame.height == height)
                {
                    continue;
                }

                frames[index] = PadFrameToCanvas(frame, width, height);
                DestroyImmediateSafe(frame);
            }
        }

        private static Texture2D PadFrameToCanvas(Texture2D source, int width, int height)
        {
            var padded = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            padded.SetPixels(Enumerable.Repeat(Color.clear, width * height).ToArray());

            var offsetX = Mathf.Max(0, (width - source.width) / 2);
            var offsetY = Mathf.Max(0, (height - source.height) / 2);
            padded.SetPixels(offsetX, offsetY, source.width, source.height, source.GetPixels());
            padded.Apply(false);
            return padded;
        }

        private static void ApplySpriteImportSettings(IEnumerable<string> generatedFiles, PixelAnimationExportSettings settings)
        {
            if (!settings.ApplySpriteImportSettings)
            {
                return;
            }

            foreach (var file in generatedFiles)
            {
                if (!IsProjectRelativePath(file) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(file) is not TextureImporter importer)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        private static async Task ApplySpriteImportSettingsAsync(
            IEnumerable<string> generatedFiles,
            PixelAnimationExportSettings settings,
            Func<float, string, bool> progress,
            Func<float> advanceProgress)
        {
            if (!settings.ApplySpriteImportSettings)
            {
                return;
            }

            foreach (var file in generatedFiles)
            {
                if (!IsProjectRelativePath(file) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(file) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                }

                ThrowIfCanceled(progress, advanceProgress(), $"Applying import settings: {Path.GetFileName(file)}");
                await Task.Yield();
            }
        }

        private static void ApplyMaterialPreset(GameObject instance, PixelAnimationExportSettings settings)
        {
            if (!settings.ForceUnlitMaterials && settings.MaterialPreset == PixelAnimationMaterialPreset.SoftShade)
            {
                return;
            }

            if (!settings.ForceUnlitMaterials && settings.MaterialPreset == PixelAnimationMaterialPreset.Flat)
            {
                // Keep source materials intact so albedo colors/textures are preserved exactly.
                return;
            }

            var shader = settings.ForceUnlitMaterials
                ? GetUnlitShader()
                : GetPresetShader(settings.MaterialPreset);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var index = 0; index < materials.Length; index++)
                {
                    materials[index] = CreatePresetMaterial(materials[index], shader, settings.MaterialPreset);
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Shader GetPresetShader(PixelAnimationMaterialPreset preset)
        {
            var shaderName = preset switch
            {
                PixelAnimationMaterialPreset.HighContrast => "Universal Render Pipeline/Lit",
                PixelAnimationMaterialPreset.Flat => "Universal Render Pipeline/Unlit",
                _ => "Unlit/Color"
            };

            return Shader.Find(shaderName)
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Standard");
        }

        private static Shader GetUnlitShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
        }

        private static Material CreatePresetMaterial(Material source, Shader shader, PixelAnimationMaterialPreset preset)
        {
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var color = preset == PixelAnimationMaterialPreset.Silhouette
                ? Color.black
                : GetMaterialColor(source);
            var texture = preset == PixelAnimationMaterialPreset.Silhouette
                ? null
                : GetMaterialTexture(source);

            SetMaterialColor(material, color);
            if (texture != null)
            {
                SetMaterialTexture(material, texture);
            }

            if (preset == PixelAnimationMaterialPreset.HighContrast)
            {
                SetMaterialFloat(material, "_Smoothness", 0f);
                SetMaterialFloat(material, "_Metallic", 0f);
            }

            return material;
        }

        private static Color GetMaterialColor(Material material)
        {
            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            if (material.HasProperty("_Color"))
            {
                return material.GetColor("_Color");
            }

            return Color.white;
        }

        private static Texture GetMaterialTexture(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty("_BaseMap"))
            {
                return material.GetTexture("_BaseMap");
            }

            if (material.HasProperty("_MainTex"))
            {
                return material.GetTexture("_MainTex");
            }

            return null;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void SetMaterialTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void ApplyAlphaThreshold(Texture2D texture, float alphaThreshold)
        {
            var pixels = texture.GetPixels32();
            var threshold = Mathf.Clamp01(alphaThreshold) * 255f;

            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index].a = pixels[index].a >= threshold ? (byte)255 : (byte)0;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static void ApplyShadeSteps(Texture2D texture, PixelAnimationExportSettings settings)
        {
            var pixels = texture.GetPixels32();
            var steps = Mathf.Max(2, settings.ShadeSteps);

            for (var index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a <= 8)
                {
                    continue;
                }

                var source = pixels[index];
                var luminance = GetLuminance(source);
                var band = Mathf.Clamp(Mathf.RoundToInt(luminance * (steps - 1)), 0, steps - 1);
                var normalized = steps <= 1 ? 0f : band / (float)(steps - 1);
                var mapped = settings.UseFixedShadeColors
                    ? EvaluateShadeColor(settings, normalized)
                    : ScaleColorToLuminance(source, normalized);
                mapped.a = source.a;
                pixels[index] = mapped;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static Color32 EvaluateShadeColor(PixelAnimationExportSettings settings, float value)
        {
            if (value <= 0f)
            {
                return settings.ShadowColor;
            }

            if (value >= 1f)
            {
                return settings.HighlightColor;
            }

            if (value <= 0.5f)
            {
                return Color.Lerp(settings.ShadowColor, settings.MidColor, value * 2f);
            }

            return Color.Lerp(settings.MidColor, settings.HighlightColor, (value - 0.5f) * 2f);
        }

        private static Color32 ScaleColorToLuminance(Color32 source, float targetLuminance)
        {
            var luminance = Mathf.Max(0.001f, GetLuminance(source));
            var multiplier = targetLuminance / luminance;
            return new Color32(
                ClampByte(source.r * multiplier),
                ClampByte(source.g * multiplier),
                ClampByte(source.b * multiplier),
                source.a);
        }

        private static void ApplyOutline(Texture2D texture, PixelAnimationExportSettings settings)
        {
            var pixels = texture.GetPixels32();
            var output = (Color32[])pixels.Clone();
            var width = texture.width;
            var height = texture.height;
            var outline = (Color32)settings.OutlineColor;
            outline.a = 255;
            var thickness = Mathf.Clamp(settings.OutlineThickness, 1, 2);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    if (pixels[index].a <= 8
                        && (settings.OutlineMode is PixelOutlineMode.Outside or PixelOutlineMode.Both)
                        && HasOpaqueNeighbor(pixels, width, height, x, y, thickness))
                    {
                        output[index] = outline;
                    }

                    if (pixels[index].a > 8
                        && (settings.OutlineMode is PixelOutlineMode.Inside or PixelOutlineMode.Both)
                        && HasTransparentNeighbor(pixels, width, height, x, y, thickness))
                    {
                        output[index] = outline;
                    }
                }
            }

            texture.SetPixels32(output);
            texture.Apply(false);
        }

        private static bool HasOpaqueNeighbor(Color32[] pixels, int width, int height, int x, int y, int radius)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if ((offsetX == 0 && offsetY == 0) || Mathf.Abs(offsetX) + Mathf.Abs(offsetY) > radius + 1)
                    {
                        continue;
                    }

                    var sampleX = x + offsetX;
                    var sampleY = y + offsetY;
                    if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                    {
                        continue;
                    }

                    if (pixels[sampleY * width + sampleX].a > 8)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasTransparentNeighbor(Color32[] pixels, int width, int height, int x, int y, int radius)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if ((offsetX == 0 && offsetY == 0) || Mathf.Abs(offsetX) + Mathf.Abs(offsetY) > radius + 1)
                    {
                        continue;
                    }

                    var sampleX = x + offsetX;
                    var sampleY = y + offsetY;
                    if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                    {
                        return true;
                    }

                    if (pixels[sampleY * width + sampleX].a <= 8)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ApplyEdgeEnhancement(Texture2D texture, PixelAnimationExportSettings settings)
        {
            var pixels = texture.GetPixels32();
            var output = (Color32[])pixels.Clone();
            var width = texture.width;
            var height = texture.height;
            var edgeColor = (Color32)settings.EdgeColor;
            edgeColor.a = 255;
            var threshold = Mathf.Clamp01(settings.EdgeThreshold);

            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    if (pixels[index].a <= 8)
                    {
                        continue;
                    }

                    var center = GetLuminance(pixels[index]);
                    var horizontal = Mathf.Abs(center - GetLuminance(pixels[y * width + x - 1]))
                        + Mathf.Abs(center - GetLuminance(pixels[y * width + x + 1]));
                    var vertical = Mathf.Abs(center - GetLuminance(pixels[(y - 1) * width + x]))
                        + Mathf.Abs(center - GetLuminance(pixels[(y + 1) * width + x]));
                    var alphaEdge = HasTransparentNeighbor(pixels, width, height, x, y, 1);

                    if (alphaEdge || horizontal + vertical >= threshold)
                    {
                        output[index] = edgeColor;
                    }
                }
            }

            texture.SetPixels32(output);
            texture.Apply(false);
        }

        private static void ApplyColorReduction(Texture2D texture, PixelAnimationExportSettings settings)
        {
            var pixels = texture.GetPixels32();
            var palette = BuildPalette(pixels, settings);

            for (var index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a <= 8)
                {
                    continue;
                }

                var source = pixels[index];
                if (settings.UseDithering)
                {
                    var threshold = Bayer4(index % texture.width, index / texture.width) - 0.5f;
                    source.r = ClampByte(source.r + threshold * 24f);
                    source.g = ClampByte(source.g + threshold * 24f);
                    source.b = ClampByte(source.b + threshold * 24f);
                }

                var mapped = FindNearestColor(source, palette);
                mapped.a = pixels[index].a;
                pixels[index] = mapped;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static Color32[] BuildPalette(Color32[] pixels, PixelAnimationExportSettings settings)
        {
            var presetPalette = GetPresetPalette(settings.PalettePreset);
            if (presetPalette.Length > 0)
            {
                return presetPalette;
            }

            if (settings.PalettePreset == PixelPalettePreset.Custom
                && settings.CustomPalette != null
                && settings.CustomPalette.Length > 0)
            {
                return settings.CustomPalette.Select(color => (Color32)color).ToArray();
            }

            return pixels
                .Where(pixel => pixel.a > 8)
                .GroupBy(pixel => new Color32((byte)(pixel.r & 0xF0), (byte)(pixel.g & 0xF0), (byte)(pixel.b & 0xF0), 255))
                .OrderByDescending(group => group.Count())
                .Take(settings.EffectiveMaxColors)
                .Select(group => group.Key)
                .ToArray();
        }

        private static Color32[] GetPresetPalette(PixelPalettePreset preset)
        {
            return preset switch
            {
                PixelPalettePreset.GameBoy => new[]
                {
                    HexColor(0x0F, 0x38, 0x0F),
                    HexColor(0x30, 0x62, 0x30),
                    HexColor(0x8B, 0xAC, 0x0F),
                    HexColor(0x9B, 0xBC, 0x0F)
                },
                PixelPalettePreset.Pico8 => new[]
                {
                    HexColor(0x00, 0x00, 0x00), HexColor(0x1D, 0x2B, 0x53),
                    HexColor(0x7E, 0x25, 0x53), HexColor(0x00, 0x87, 0x51),
                    HexColor(0xAB, 0x52, 0x36), HexColor(0x5F, 0x57, 0x4F),
                    HexColor(0xC2, 0xC3, 0xC7), HexColor(0xFF, 0xF1, 0xE8),
                    HexColor(0xFF, 0x00, 0x4D), HexColor(0xFF, 0xA3, 0x00),
                    HexColor(0xFF, 0xEC, 0x27), HexColor(0x00, 0xE4, 0x36),
                    HexColor(0x29, 0xAD, 0xFF), HexColor(0x83, 0x76, 0x9C),
                    HexColor(0xFF, 0x77, 0xA8), HexColor(0xFF, 0xCC, 0xAA)
                },
                PixelPalettePreset.DawnBringer16 => new[]
                {
                    HexColor(0x14, 0x0C, 0x1C), HexColor(0x44, 0x24, 0x34),
                    HexColor(0x30, 0x34, 0x6D), HexColor(0x4E, 0x4A, 0x4E),
                    HexColor(0x85, 0x4C, 0x30), HexColor(0x34, 0x65, 0x24),
                    HexColor(0xD0, 0x46, 0x48), HexColor(0x75, 0x71, 0x61),
                    HexColor(0x59, 0x7D, 0xCE), HexColor(0xD2, 0x7D, 0x2C),
                    HexColor(0x85, 0x95, 0xA1), HexColor(0x6D, 0xAA, 0x2C),
                    HexColor(0xD2, 0xAA, 0x99), HexColor(0x6D, 0xC2, 0xCA),
                    HexColor(0xDA, 0xD4, 0x5E), HexColor(0xDE, 0xEE, 0xD6)
                },
                _ => Array.Empty<Color32>()
            };
        }

        private static Color32 HexColor(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }

        private static Color32 FindNearestColor(Color32 source, Color32[] palette)
        {
            if (palette.Length == 0)
            {
                return source;
            }

            var best = palette[0];
            var bestDistance = int.MaxValue;

            foreach (var color in palette)
            {
                var red = source.r - color.r;
                var green = source.g - color.g;
                var blue = source.b - color.b;
                var distance = red * red + green * green + blue * blue;
                if (distance < bestDistance)
                {
                    best = color;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static float Bayer4(int x, int y)
        {
            var matrix = new[,]
            {
                { 0, 8, 2, 10 },
                { 12, 4, 14, 6 },
                { 3, 11, 1, 9 },
                { 15, 7, 13, 5 }
            };
            return matrix[y & 3, x & 3] / 15f;
        }

        private static byte ClampByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
        }

        private static float GetLuminance(Color32 color)
        {
            return (0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b) / 255f;
        }

        private static string BuildOutputDirectory(PixelAnimationExportSettings settings, int directionIndex, int directionCount)
        {
            var basePath = settings.OutputFolderPath.Trim();
            var directory = Path.Combine(
                basePath,
                SanitizeFileName(settings.Prefab.name),
                SanitizeFileName(settings.AnimationClip.name));

            if (directionCount > 1)
            {
                directory = Path.Combine(directory, $"Direction_{directionIndex:D2}");
            }

            return directory.Replace('\\', '/');
        }

        private static float[] GetDirectionYaws(PixelAnimationExportSettings settings)
        {
            if (settings.DirectionYaws == null || settings.DirectionYaws.Length == 0)
            {
                return new[] { settings.CameraYaw };
            }

            return settings.DirectionYaws;
        }

        private static int GetEstimatedOutputStepCount(PixelAnimationExportSettings settings)
        {
            var steps = 0;
            if (settings.OutputMode is PixelAnimationOutputMode.PngSequence or PixelAnimationOutputMode.Both)
            {
                steps += settings.FrameCount;
            }

            if (settings.OutputMode is PixelAnimationOutputMode.SpriteSheet or PixelAnimationOutputMode.Both)
            {
                steps++;
            }

            if (settings.WriteGifPreview)
            {
                steps++;
            }

            if (settings.WriteMetadataJson)
            {
                steps++;
            }

            if (settings.ApplySpriteImportSettings)
            {
                steps += settings.OutputMode == PixelAnimationOutputMode.SpriteSheet ? 1 : settings.FrameCount;
            }

            return Mathf.Max(1, steps);
        }

        private static void ThrowIfCanceled(Func<float, string, bool> progress, float value, string message)
        {
            if (progress != null && !progress(Mathf.Clamp01(value), message))
            {
                throw new OperationCanceledException("Export canceled.");
            }
        }

        private static RenderTexture CreateRenderTexture(PixelAnimationExportSettings settings)
        {
            var renderTexture = new RenderTexture(settings.Resolution.x, settings.Resolution.y, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            renderTexture.Create();
            return renderTexture;
        }

        private static Texture2D CreateReadTexture(PixelAnimationExportSettings settings)
        {
            return new Texture2D(settings.Resolution.x, settings.Resolution.y, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Texture2D CloneTexture(Texture2D source)
        {
            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            clone.SetPixels32(source.GetPixels32());
            clone.Apply(false);
            return clone;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static Camera CreateCamera(PixelAnimationExportSettings settings)
        {
            var cameraObject = new GameObject("Pixel Animation Export Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            cameraObject.layer = ExportLayer;
            var camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = ExportLayerMask;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = settings.TransparentBackground
                ? new Color(settings.BackgroundColor.r, settings.BackgroundColor.g, settings.BackgroundColor.b, 0f)
                : new Color(settings.BackgroundColor.r, settings.BackgroundColor.g, settings.BackgroundColor.b, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            return camera;
        }

        private static Light CreateLight(PixelAnimationExportSettings settings)
        {
            if (!settings.UseLighting)
            {
                return null;
            }

            var lightObject = new GameObject("Pixel Animation Export Light")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            lightObject.layer = ExportLayer;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = ExportLayerMask;
            light.intensity = settings.MaterialPreset == PixelAnimationMaterialPreset.HighContrast ? 2.2f : settings.IncludeShadows ? 1.2f : 1f;
            light.shadows = settings.IncludeShadows ? LightShadows.Soft : LightShadows.None;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            return light;
        }

        private static Texture2D CaptureFrame(
            PixelAnimationExportSettings settings,
            GameObject instance,
            Camera camera,
            RenderTexture renderTexture,
            Texture2D readTexture,
            IEnumerable<SkinnedMeshBakeProxy> bakeProxies,
            int frameIndex)
        {
            var sourceFrameIndex = GetSourceFrameForOutputFrame(settings, frameIndex);
            settings.AnimationClip.SampleAnimation(instance, GetFrameTime(settings, sourceFrameIndex, settings.BaseFrameCount));
            SnapInstanceToPixelGrid(instance, camera, settings);
            UpdateSkinnedMeshBakeProxies(bakeProxies);

            camera.Render();
            RenderTexture.active = renderTexture;
            readTexture.ReadPixels(new Rect(0, 0, settings.Resolution.x, settings.Resolution.y), 0, 0);
            readTexture.Apply(false);

            var frame = CloneTexture(readTexture);
            ProcessFrame(frame, settings);
            return frame;
        }

        private static Bounds CalculateAnimatedBounds(
            GameObject instance,
            PixelAnimationExportSettings settings,
            IEnumerable<SkinnedMeshBakeProxy> bakeProxies)
        {
            var frameCount = settings.SampleFrameCount;
            var hasBounds = false;
            var bounds = new Bounds(instance.transform.position, Vector3.one);

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var sourceFrameIndex = GetSourceFrameForOutputFrame(settings, frameIndex * Mathf.Max(1, settings.FrameHold));
                settings.AnimationClip.SampleAnimation(instance, GetFrameTime(settings, sourceFrameIndex, settings.BaseFrameCount));
                UpdateSkinnedMeshBakeProxies(bakeProxies);
                if (TryGetRendererBounds(instance, bakeProxies, out var frameBounds))
                {
                    if (!hasBounds)
                    {
                        bounds = frameBounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(frameBounds);
                    }
                }
            }

            return hasBounds ? bounds : new Bounds(instance.transform.position, Vector3.one);
        }

        private static SkinnedMeshBakeProxy[] CreateSkinnedMeshBakeProxies(GameObject instance)
        {
            var skinnedRenderers = instance
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null && renderer.enabled && renderer.sharedMesh != null)
                .ToArray();
            if (skinnedRenderers.Length == 0)
            {
                return Array.Empty<SkinnedMeshBakeProxy>();
            }

            var proxies = new SkinnedMeshBakeProxy[skinnedRenderers.Length];
            for (var index = 0; index < skinnedRenderers.Length; index++)
            {
                proxies[index] = new SkinnedMeshBakeProxy(skinnedRenderers[index]);
            }

            return proxies;
        }

        private static void UpdateSkinnedMeshBakeProxies(IEnumerable<SkinnedMeshBakeProxy> bakeProxies)
        {
            if (bakeProxies == null)
            {
                return;
            }

            foreach (var proxy in bakeProxies)
            {
                proxy.Update();
            }
        }

        private static void DestroySkinnedMeshBakeProxies(IEnumerable<SkinnedMeshBakeProxy> bakeProxies)
        {
            if (bakeProxies == null)
            {
                return;
            }

            foreach (var proxy in bakeProxies)
            {
                proxy.Dispose();
            }
        }

        private static bool TryGetRendererBounds(
            GameObject target,
            IEnumerable<SkinnedMeshBakeProxy> bakeProxies,
            out Bounds bounds)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            bounds = new Bounds(target.transform.position, Vector3.one);
            var hasBounds = false;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                EncapsulateBounds(renderer.bounds, ref bounds, ref hasBounds);
            }

            if (bakeProxies == null)
            {
                return hasBounds;
            }

            foreach (var proxy in bakeProxies)
            {
                if (proxy.TryGetBounds(out var proxyBounds))
                {
                    EncapsulateBounds(proxyBounds, ref bounds, ref hasBounds);
                }
            }

            return hasBounds;
        }

        private static void EncapsulateBounds(Bounds nextBounds, ref Bounds bounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = nextBounds;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(nextBounds);
        }

        private static void ConfigureCamera(Camera camera, PixelAnimationExportSettings settings, Bounds bounds, float yaw)
        {
            if (settings.PixelSnap && settings.UseOrthographicCamera)
            {
                var pixelWorldSize = Mathf.Max(bounds.size.y, 1f) / Mathf.Max(1, settings.Resolution.y);
                bounds.center = new Vector3(
                    Mathf.Round(bounds.center.x / pixelWorldSize) * pixelWorldSize,
                    Mathf.Round(bounds.center.y / pixelWorldSize) * pixelWorldSize,
                    Mathf.Round(bounds.center.z / pixelWorldSize) * pixelWorldSize);
            }

            var maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, 0.5f);
            var pitch = Mathf.Clamp(settings.CameraPitch, -85f, 85f);
            var zoom = Mathf.Max(0.1f, settings.CameraZoom);
            var direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
            var distance = Mathf.Max(4f, maxExtent * 4f) * zoom;

            camera.transform.position = bounds.center + direction.normalized * distance;
            camera.transform.LookAt(bounds.center);
            camera.orthographic = settings.UseOrthographicCamera;

            if (settings.UseOrthographicCamera)
            {
                var aspect = settings.Resolution.x / (float)settings.Resolution.y;
                var halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(aspect, 0.001f));
                camera.orthographicSize = Mathf.Max(0.5f, halfHeight * zoom);
            }
            else
            {
                camera.fieldOfView = 35f;
            }
        }

        private static void SnapInstanceToPixelGrid(GameObject instance, Camera camera, PixelAnimationExportSettings settings)
        {
            if (!settings.SnapModelToPixelGrid || !settings.UseOrthographicCamera || camera == null)
            {
                return;
            }

            var pixelWorldSize = camera.orthographicSize * 2f / Mathf.Max(1, settings.Resolution.y);
            if (pixelWorldSize <= 0f)
            {
                return;
            }

            var position = instance.transform.position;
            instance.transform.position = new Vector3(
                Mathf.Round(position.x / pixelWorldSize) * pixelWorldSize,
                Mathf.Round(position.y / pixelWorldSize) * pixelWorldSize,
                Mathf.Round(position.z / pixelWorldSize) * pixelWorldSize);
        }

        private static float GetFrameTime(PixelAnimationExportSettings settings, int frameIndex, int frameCount)
        {
            if (frameCount <= 1)
            {
                return 0f;
            }

            return Mathf.Min(frameIndex / (float)settings.Fps, settings.ExportDuration);
        }

        private static int GetSourceFrameForOutputFrame(PixelAnimationExportSettings settings, int outputFrameIndex)
        {
            var firstFrame = GetFirstExportFrame(settings);
            var rangeFrameCount = Mathf.Max(1, settings.RangeFrameCount);
            var heldFrameIndex = Mathf.Max(0, outputFrameIndex) / Mathf.Max(1, settings.FrameHold);
            var decimatedFrameIndex = Mathf.Min(rangeFrameCount - 1, heldFrameIndex * Mathf.Max(1, settings.MotionDecimation));
            return firstFrame + decimatedFrameIndex;
        }

        private static int GetFirstExportFrame(PixelAnimationExportSettings settings)
        {
            if (!settings.UseFrameRange)
            {
                return 0;
            }

            return Mathf.Clamp(settings.StartFrame, 0, Mathf.Max(0, settings.BaseFrameCount - 1));
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "Unnamed" : value;
        }

        private static bool IsProjectRelativePath(string path)
        {
            return path.StartsWith("Assets/", StringComparison.Ordinal) || path == "Assets";
        }

        private static void DestroyImmediateSafe(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class FrameCaptureSession : IDisposable
        {
            private readonly PixelAnimationExportSettings settings;
            private readonly GameObject instance;
            private readonly Camera camera;
            private readonly Light light;
            private readonly RenderTexture renderTexture;
            private readonly Texture2D readTexture;
            private readonly SkinnedMeshBakeProxy[] bakeProxies;
            private readonly RenderTexture previousActive;
            private bool disposed;

            public FrameCaptureSession(PixelAnimationExportSettings settings, float yaw)
            {
                this.settings = settings;
                previousActive = RenderTexture.active;
                instance = UnityEngine.Object.Instantiate(settings.Prefab);
                instance.name = settings.Prefab.name;
                instance.hideFlags = HideFlags.HideAndDontSave;
                SetLayerRecursively(instance, ExportLayer);

                ApplyMaterialPreset(instance, settings);
                bakeProxies = CreateSkinnedMeshBakeProxies(instance);
                camera = CreateCamera(settings);
                light = CreateLight(settings);

                renderTexture = CreateRenderTexture(settings);
                readTexture = CreateReadTexture(settings);
                camera.targetTexture = renderTexture;

                var captureBounds = CalculateAnimatedBounds(instance, settings, bakeProxies);
                ConfigureCamera(camera, settings, captureBounds, yaw);
            }

            public Texture2D CaptureFrame(int frameIndex)
            {
                return PixelAnimationExporter.CaptureFrame(
                    settings,
                    instance,
                    camera,
                    renderTexture,
                    readTexture,
                    bakeProxies,
                    frameIndex);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                RenderTexture.active = previousActive;

                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                DestroyImmediateSafe(readTexture);
                DestroyImmediateSafe(renderTexture);
                DestroyImmediateSafe(light != null ? light.gameObject : null);
                DestroyImmediateSafe(camera != null ? camera.gameObject : null);
                DestroySkinnedMeshBakeProxies(bakeProxies);
                DestroyImmediateSafe(instance);
            }
        }

        private sealed class CapturedFrameSet : IDisposable
        {
            public CapturedFrameSet(IReadOnlyList<Texture2D> frames)
            {
                Frames = frames;
            }

            public IReadOnlyList<Texture2D> Frames { get; }

            public void Dispose()
            {
                foreach (var frame in Frames)
                {
                    DestroyImmediateSafe(frame);
                }
            }
        }

        private sealed class RenderOperationLock : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                RenderSemaphore.Release();
            }
        }

        private sealed class SkinnedMeshBakeProxy : IDisposable
        {
            private readonly SkinnedMeshRenderer source;
            private readonly bool originalEnabled;
            private readonly Mesh bakedMesh;
            private readonly GameObject proxyObject;
            private readonly MeshRenderer proxyRenderer;

            public SkinnedMeshBakeProxy(SkinnedMeshRenderer source)
            {
                this.source = source;
                originalEnabled = source.enabled;
                bakedMesh = new Mesh
                {
                    name = $"{source.name} Baked Mesh",
                    hideFlags = HideFlags.HideAndDontSave
                };
                proxyObject = new GameObject($"{source.name} Baked Proxy")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = source.gameObject.layer
                };
                var meshFilter = proxyObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = bakedMesh;
                proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
                proxyRenderer.sharedMaterials = source.sharedMaterials;
                proxyRenderer.shadowCastingMode = source.shadowCastingMode;
                proxyRenderer.receiveShadows = source.receiveShadows;
                proxyRenderer.lightProbeUsage = source.lightProbeUsage;
                proxyRenderer.reflectionProbeUsage = source.reflectionProbeUsage;
                source.enabled = false;
            }

            public void Update()
            {
                if (source == null || proxyObject == null)
                {
                    return;
                }

                source.BakeMesh(bakedMesh);
                proxyObject.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                proxyObject.transform.localScale = Vector3.one;
            }

            public bool TryGetBounds(out Bounds bounds)
            {
                if (proxyRenderer != null && proxyRenderer.enabled)
                {
                    bounds = proxyRenderer.bounds;
                    return true;
                }

                bounds = default;
                return false;
            }

            public void Dispose()
            {
                if (source != null)
                {
                    source.enabled = originalEnabled;
                }

                DestroyImmediateSafe(proxyObject);
                DestroyImmediateSafe(bakedMesh);
            }
        }

        [Serializable]
        private sealed class Metadata
        {
            public PixelAnimationCliConfig settings;
            public string prefabName;
            public string animationClipName;
            public int frameCount;
            public int startFrame;
            public int endFrame;
            public int baseFrameCount;
            public int rangeFrameCount;
            public int sampleFrameCount;
            public int effectiveMaxColors;
            public int directionIndex;
            public float directionYaw;
        }
    }
}
