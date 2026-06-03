using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnimToPixel.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimToPixel.Editor.Tests
{
    public sealed class PixelAnimationExporterTests
    {
        private const string TempRoot = "Assets/Temp/AnimToPixelExporterTests";

        [SetUp]
        public void SetUp()
        {
            DeleteTempRoot();
            Directory.CreateDirectory(TempRoot);
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempRoot();
        }

        [Test]
        public void Validate_RequiresPrefab()
        {
            var settings = CreateValidSettings();
            settings.Prefab = null;

            Assert.Throws<ArgumentException>(() => PixelAnimationExporter.Validate(settings));
        }

        [Test]
        public void Validate_RequiresAnimationClip()
        {
            var settings = CreateValidSettings();
            settings.AnimationClip = null;

            Assert.Throws<ArgumentException>(() => PixelAnimationExporter.Validate(settings));
        }

        [Test]
        public void Export_WritesExpectedPngSequence()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/Exports",
                Resolution = new Vector2Int(32, 32),
                Fps = 4,
                TransparentBackground = true
            };

            var result = PixelAnimationExporter.Export(settings);

            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(4));
            foreach (var generatedFile in result.GeneratedFiles)
            {
                Assert.That(File.Exists(generatedFile), Is.True, generatedFile);
                Assert.That(Path.GetExtension(generatedFile), Is.EqualTo(".png"));

                var pngBytes = File.ReadAllBytes(generatedFile);
                Assert.That(pngBytes.Length, Is.GreaterThan(8));
                Assert.That(pngBytes[0], Is.EqualTo(0x89));
                Assert.That(pngBytes[1], Is.EqualTo(0x50));
                Assert.That(pngBytes[2], Is.EqualTo(0x4E));
                Assert.That(pngBytes[3], Is.EqualTo(0x47));
            }
        }

        [Test]
        public void Export_BothModeWithDirections_WritesSheetsAndMetadata()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/Exports",
                Resolution = new Vector2Int(32, 32),
                Fps = 4,
                TransparentBackground = true,
                DirectionYaws = new[] { 0f, 90f },
                OutputMode = PixelAnimationOutputMode.Both,
                WriteMetadataJson = true,
                ApplyOutline = true,
                ReduceColors = true,
                ColorCountPreset = PixelColorCountPreset.Custom,
                MaxColors = 4,
                UseDithering = true
            };

            var result = PixelAnimationExporter.Export(settings);

            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(12));
            Assert.That(result.GeneratedFiles, Has.Some.Matches<string>(path => path.EndsWith("_sheet.png")));
            Assert.That(result.GeneratedFiles, Has.Some.Matches<string>(path => path.EndsWith(".json")));

            var metadataPath = result.GeneratedFiles[5];
            var metadataJson = File.ReadAllText(metadataPath);
            Assert.That(metadataJson, Does.Contain("\"settings\""));
            Assert.That(metadataJson, Does.Contain("\"applyOutline\": true"));
            Assert.That(metadataJson, Does.Contain("\"reduceColors\": true"));
            Assert.That(metadataJson, Does.Contain("\"frameCount\": 4"));
            Assert.That(metadataJson, Does.Contain("\"startFrame\": 0"));
            Assert.That(metadataJson, Does.Contain("\"effectiveMaxColors\": 4"));
            Assert.That(File.Exists($"{TempRoot}/Exports/PixelTestCube/Move/Direction_00/Move_0000.png"), Is.True);
            Assert.That(File.Exists($"{TempRoot}/Exports/PixelTestCube/Move/Direction_01/Move_sheet.png"), Is.True);
        }

        [Test]
        public void Export_FrameRangeTrimGifAndSpriteImportSettings_AreApplied()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/AdvancedExports",
                Resolution = new Vector2Int(64, 64),
                Fps = 4,
                TransparentBackground = true,
                UseFrameRange = true,
                StartFrame = 1,
                EndFrame = 2,
                AutoTrim = true,
                TrimPadding = 0,
                WriteGifPreview = true,
                ApplySpriteImportSettings = true
            };

            var result = PixelAnimationExporter.Export(settings);

            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(3));
            Assert.That(File.Exists($"{TempRoot}/AdvancedExports/PixelTestCube/Move/Move_0000.png"), Is.True);
            Assert.That(File.Exists($"{TempRoot}/AdvancedExports/PixelTestCube/Move/Move_0001.png"), Is.True);
            Assert.That(File.Exists($"{TempRoot}/AdvancedExports/PixelTestCube/Move/Move_preview.gif"), Is.True);

            var gifBytes = File.ReadAllBytes($"{TempRoot}/AdvancedExports/PixelTestCube/Move/Move_preview.gif");
            Assert.That(System.Text.Encoding.ASCII.GetString(gifBytes, 0, 6), Is.EqualTo("GIF89a"));

            var pngPath = $"{TempRoot}/AdvancedExports/PixelTestCube/Move/Move_0000.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
        }

        [Test]
        public void Export_AutoTrimPreservesVisibleMotionBetweenFrames()
        {
            var prefab = CreateCubePrefab();
            var clip = new AnimationClip
            {
                name = "WideMove",
                frameRate = 4f
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "localPosition.x",
                AnimationCurve.Linear(0f, -1.5f, 1f, 1.5f));
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/AutoTrimMotionExports",
                Resolution = new Vector2Int(64, 64),
                Fps = 4,
                TransparentBackground = true,
                AutoTrim = true,
                TrimPadding = 0,
                UseLighting = false,
                ForceUnlitMaterials = true
            };

            PixelAnimationExporter.Export(settings);

            var first = LoadTexture($"{TempRoot}/AutoTrimMotionExports/PixelTestCube/WideMove/WideMove_0000.png");
            var last = LoadTexture($"{TempRoot}/AutoTrimMotionExports/PixelTestCube/WideMove/WideMove_0003.png");
            try
            {
                Assert.That(GetOpaqueCenterX(first), Is.LessThan(GetOpaqueCenterX(last)));
                Assert.That(PixelsEqual(first, last), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(last);
            }
        }

        [Test]
        public void Export_MotionDecimationAndFrameHold_ControlOutputFrameCount()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/TimingExports",
                Resolution = new Vector2Int(32, 32),
                Fps = 4,
                TransparentBackground = true,
                MotionDecimation = 2,
                FrameHold = 3
            };

            var result = PixelAnimationExporter.Export(settings);

            Assert.That(settings.RangeFrameCount, Is.EqualTo(4));
            Assert.That(settings.SampleFrameCount, Is.EqualTo(2));
            Assert.That(settings.FrameCount, Is.EqualTo(6));
            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(6));
            Assert.That(File.Exists($"{TempRoot}/TimingExports/PixelTestCube/Move/Move_0005.png"), Is.True);
        }

        [Test]
        public void Export_FixedPaletteLimitsWrittenColors()
        {
            var prefab = CreateCubePrefab(new Color(0.8f, 0.25f, 0.1f, 1f));
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/PaletteExports",
                Resolution = new Vector2Int(32, 32),
                Fps = 1,
                TransparentBackground = true,
                ReduceColors = true,
                PalettePreset = PixelPalettePreset.GameBoy,
                ColorCountPreset = PixelColorCountPreset.Four,
                UseFullClipLength = false,
                DurationSeconds = 1f
            };

            PixelAnimationExporter.Export(settings);

            var texture = LoadTexture($"{TempRoot}/PaletteExports/PixelTestCube/Move/Move_0000.png");
            try
            {
                var opaqueColors = new HashSet<string>(
                    texture.GetPixels32()
                        .Where(pixel => pixel.a > 8)
                        .Select(pixel => $"{pixel.r:X2}{pixel.g:X2}{pixel.b:X2}"));

                Assert.That(opaqueColors.Count, Is.LessThanOrEqualTo(4));
                Assert.That(opaqueColors, Is.SubsetOf(new[]
                {
                    "0F380F",
                    "306230",
                    "8BAC0F",
                    "9BBC0F"
                }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public async Task ExportAsync_ReportsProgressAndWritesFiles()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var progressCalls = 0;
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/AsyncExports",
                Resolution = new Vector2Int(32, 32),
                Fps = 4,
                TransparentBackground = true
            };

            var result = await PixelAnimationExporter.ExportAsync(settings, (progress, message) =>
            {
                progressCalls++;
                Assert.That(progress, Is.InRange(0f, 1f));
                Assert.That(message, Is.Not.Empty);
                return true;
            });

            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(4));
            Assert.That(progressCalls, Is.GreaterThan(1));
            Assert.That(File.Exists($"{TempRoot}/AsyncExports/PixelTestCube/Move/Move_0000.png"), Is.True);
        }

        [Test]
        public void GifWriter_DoesNotFlipFrameVertically()
        {
            var texture = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.blue);
            texture.SetPixel(0, 1, Color.red);
            texture.Apply(false);
            var path = $"{TempRoot}/orientation.gif";

            try
            {
                PixelAnimationGifWriter.Write(path, new[] { texture }, 1);
                var bytes = File.ReadAllBytes(path);
                var imageDataIndex = Array.IndexOf(bytes, (byte)0x2C);
                Assert.That(imageDataIndex, Is.GreaterThan(0));
                Assert.That(bytes[imageDataIndex + 10], Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GifWriter_UsesMaxFrameSizeAndTransparentDisposal()
        {
            var small = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var large = new Texture2D(3, 2, TextureFormat.RGBA32, false);
            small.SetPixel(0, 0, Color.red);
            large.SetPixels(new[] { Color.clear, Color.green, Color.clear, Color.clear, Color.green, Color.clear });
            small.Apply(false);
            large.Apply(false);
            var path = $"{TempRoot}/variable-size.gif";

            try
            {
                PixelAnimationGifWriter.Write(path, new[] { small, large }, 12);
                var bytes = File.ReadAllBytes(path);

                Assert.That(ReadLittleEndianShort(bytes, 6), Is.EqualTo(3));
                Assert.That(ReadLittleEndianShort(bytes, 8), Is.EqualTo(2));
                var gceIndex = Array.IndexOf(bytes, (byte)0xF9);
                Assert.That(gceIndex, Is.GreaterThan(0));
                Assert.That(bytes[gceIndex + 2], Is.EqualTo(0x09));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(large);
            }
        }

        [Test]
        public void GifWriter_DistributesDelayToMatchRequestedFps()
        {
            var frames = new Texture2D[6];
            for (var index = 0; index < frames.Length; index++)
            {
                frames[index] = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                frames[index].SetPixel(0, 0, Color.red);
                frames[index].Apply(false);
            }

            var path = $"{TempRoot}/delay.gif";

            try
            {
                PixelAnimationGifWriter.Write(path, frames, 12);
                var bytes = File.ReadAllBytes(path);
                var delays = ReadGifDelays(bytes);

                Assert.That(delays, Is.EqualTo(new[] { 8, 9, 8, 8, 9, 8 }));
            }
            finally
            {
                foreach (var frame in frames)
                {
                    UnityEngine.Object.DestroyImmediate(frame);
                }
            }
        }

        [Test]
        public void NormalizeFrameCanvases_PadsFramesToMaxSizeCentered()
        {
            var small = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var large = new Texture2D(3, 3, TextureFormat.RGBA32, false);
            small.SetPixel(0, 0, Color.red);
            large.SetPixel(1, 1, Color.green);
            small.Apply(false);
            large.Apply(false);
            var frames = new System.Collections.Generic.List<Texture2D> { small, large };

            try
            {
                InvokeNormalizeFrameCanvases(frames);

                Assert.That(frames[0].width, Is.EqualTo(3));
                Assert.That(frames[0].height, Is.EqualTo(3));
                Assert.That(frames[1].width, Is.EqualTo(3));
                Assert.That(frames[1].height, Is.EqualTo(3));
                Assert.That(frames[0].GetPixel(1, 1).r, Is.GreaterThan(0.9f));
            }
            finally
            {
                foreach (var frame in frames)
                {
                    UnityEngine.Object.DestroyImmediate(frame);
                }
            }
        }

        [Test]
        public void SharedAutoTrim_PreservesFrameRelativeMotion()
        {
            var left = new Texture2D(7, 1, TextureFormat.RGBA32, false);
            var right = new Texture2D(7, 1, TextureFormat.RGBA32, false);
            left.SetPixels(Enumerable.Repeat(Color.clear, 7).ToArray());
            right.SetPixels(Enumerable.Repeat(Color.clear, 7).ToArray());
            left.SetPixel(1, 0, Color.red);
            right.SetPixel(5, 0, Color.blue);
            left.Apply(false);
            right.Apply(false);
            var frames = new System.Collections.Generic.List<Texture2D> { left, right };

            try
            {
                InvokeTrimFramesToSharedBounds(frames, 0, true);

                Assert.That(frames[0].width, Is.EqualTo(5));
                Assert.That(frames[1].width, Is.EqualTo(5));
                Assert.That(frames[0].GetPixel(0, 0).r, Is.GreaterThan(0.9f));
                Assert.That(frames[1].GetPixel(4, 0).b, Is.GreaterThan(0.9f));
            }
            finally
            {
                foreach (var frame in frames)
                {
                    UnityEngine.Object.DestroyImmediate(frame);
                }
            }
        }

        [Test]
        public void FlatMaterialPreset_KeepsSourceMaterials()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var originalMaterial = cube.GetComponent<Renderer>().sharedMaterial;
            var settings = new PixelAnimationExportSettings
            {
                MaterialPreset = PixelAnimationMaterialPreset.Flat
            };

            try
            {
                InvokeApplyMaterialPreset(cube, settings);

                Assert.That(cube.GetComponent<Renderer>().sharedMaterial, Is.SameAs(originalMaterial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void ForceUnlitMaterials_ReplacesSourceMaterialsForExportOnly()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<Renderer>();
            var originalMaterial = renderer.sharedMaterial;
            var settings = new PixelAnimationExportSettings
            {
                MaterialPreset = PixelAnimationMaterialPreset.Flat,
                ForceUnlitMaterials = true
            };

            try
            {
                InvokeApplyMaterialPreset(cube, settings);

                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(originalMaterial));
                Assert.That(renderer.sharedMaterial.shader.name, Does.Contain("Unlit").Or.EqualTo("Standard"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void ForceUnlitMaterials_StillAppliesMaterialPreset()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<Renderer>();
            var settings = new PixelAnimationExportSettings
            {
                MaterialPreset = PixelAnimationMaterialPreset.Silhouette,
                ForceUnlitMaterials = true
            };

            try
            {
                InvokeApplyMaterialPreset(cube, settings);
                var material = renderer.sharedMaterial;
                var color = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.HasProperty("_Color")
                        ? material.GetColor("_Color")
                        : Color.white;

                Assert.That(color.r, Is.LessThan(0.01f));
                Assert.That(color.g, Is.LessThan(0.01f));
                Assert.That(color.b, Is.LessThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void HighContrastMaterialPreset_CopiesSourceMaterialColor()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial.color = Color.green;
            if (renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                renderer.sharedMaterial.SetColor("_BaseColor", Color.green);
            }

            var settings = new PixelAnimationExportSettings
            {
                MaterialPreset = PixelAnimationMaterialPreset.HighContrast
            };

            try
            {
                InvokeApplyMaterialPreset(cube, settings);
                var material = renderer.sharedMaterial;
                var color = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.HasProperty("_Color")
                        ? material.GetColor("_Color")
                        : Color.white;

                Assert.That(color.g, Is.GreaterThan(color.r));
                Assert.That(color.g, Is.GreaterThan(color.b));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void RenderPreview_ReturnsTextureAtRequestedResolution()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/Exports",
                Resolution = new Vector2Int(32, 32),
                Fps = 4,
                TransparentBackground = true
            };

            var preview = PixelAnimationExporter.RenderPreview(settings, 0);

            try
            {
                Assert.That(preview.width, Is.EqualTo(32));
                Assert.That(preview.height, Is.EqualTo(32));
                Assert.That(preview.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(CountOpaquePixels(preview), Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void RenderPreview_WithoutAutoTrimUsesRequestedFrame()
        {
            var prefab = CreateCubePrefab();
            var clip = new AnimationClip
            {
                name = "SinglePreviewMove",
                frameRate = 4f
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "localPosition.x",
                AnimationCurve.Linear(0f, -1.5f, 1f, 1.5f));
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/SinglePreviewExports",
                Resolution = new Vector2Int(64, 64),
                Fps = 4,
                TransparentBackground = true,
                AutoTrim = false,
                UseLighting = false,
                ForceUnlitMaterials = true
            };

            var first = PixelAnimationExporter.RenderPreview(settings, 0);
            var last = PixelAnimationExporter.RenderPreview(settings, 3);

            try
            {
                Assert.That(PixelsEqual(first, last), Is.False);
                Assert.That(GetOpaqueCenterX(first), Is.LessThan(GetOpaqueCenterX(last)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(last);
            }
        }

        [Test]
        public void ExportCamera_UsesDedicatedLayerOnly()
        {
            var settings = CreateValidSettings();
            var camera = InvokeCreateCamera(settings);

            try
            {
                Assert.That(camera.cullingMask, Is.EqualTo(1 << 31));
                Assert.That(camera.gameObject.layer, Is.EqualTo(31));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(settings.Prefab);
            }
        }

        [Test]
        public void ExportLayer_IsAppliedToPrefabHierarchyOnly()
        {
            var root = new GameObject("LayerRoot");
            var child = new GameObject("LayerChild");
            child.transform.SetParent(root.transform);

            try
            {
                InvokeSetLayerRecursively(root, 31);

                Assert.That(root.layer, Is.EqualTo(31));
                Assert.That(child.layer, Is.EqualTo(31));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Cli_CreateSettings_LoadsAssetsFromConfig()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            AssetDatabase.CreateAsset(clip, $"{TempRoot}/CliMove.anim");
            var config = new PixelAnimationCliConfig
            {
                prefabPath = AssetDatabase.GetAssetPath(prefab),
                animationClipPath = $"{TempRoot}/CliMove.anim",
                outputFolderPath = $"{TempRoot}/CliExports",
                resolutionX = 48,
                resolutionY = 40,
                fps = 10,
                shadeSteps = 12,
                forceUnlitMaterials = true,
                reduceColors = true,
                palettePreset = PixelPalettePreset.Pico8
            };

            var settings = PixelAnimationCli.CreateSettings(config);

            Assert.That(settings.Prefab, Is.SameAs(prefab));
            Assert.That(settings.AnimationClip, Is.SameAs(clip));
            Assert.That(settings.Resolution, Is.EqualTo(new Vector2Int(48, 40)));
            Assert.That(settings.Fps, Is.EqualTo(10));
            Assert.That(settings.ShadeSteps, Is.EqualTo(12));
            Assert.That(settings.ForceUnlitMaterials, Is.True);
            Assert.That(settings.PalettePreset, Is.EqualTo(PixelPalettePreset.Pico8));
        }

        [Test]
        public void Cli_CreateConfig_ContainsAllExportSettings()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            AssetDatabase.CreateAsset(clip, $"{TempRoot}/CliRoundTrip.anim");
            var settings = new PixelAnimationExportSettings
            {
                Prefab = prefab,
                AnimationClip = clip,
                OutputFolderPath = $"{TempRoot}/AllSettingsExports",
                Resolution = new Vector2Int(96, 80),
                Fps = 18,
                UseFullClipLength = false,
                DurationSeconds = 2.5f,
                UseFrameRange = true,
                StartFrame = 2,
                EndFrame = 9,
                BackgroundColor = new Color(0.1f, 0.2f, 0.3f, 0.4f),
                TransparentBackground = false,
                CameraYaw = 33f,
                CameraPitch = -12f,
                CameraZoom = 2.25f,
                DirectionYaws = new[] { 0f, 90f, 180f },
                UseOrthographicCamera = false,
                UseLighting = false,
                IncludeShadows = false,
                ForceUnlitMaterials = true,
                MaterialPreset = PixelAnimationMaterialPreset.HighContrast,
                OutputMode = PixelAnimationOutputMode.Both,
                WriteMetadataJson = true,
                ApplySpriteImportSettings = false,
                WriteGifPreview = true,
                AutoTrim = true,
                TrimPadding = 3,
                PixelSnap = false,
                SnapModelToPixelGrid = true,
                ApplyOutline = true,
                OutlineColor = Color.red,
                OutlineThickness = 2,
                OutlineMode = PixelOutlineMode.Both,
                EnhanceEdges = true,
                EdgeColor = Color.blue,
                EdgeThreshold = 0.6f,
                RemoveAntiAliasing = true,
                AlphaThreshold = 0.7f,
                ShadeSteps = 24,
                UseFixedShadeColors = true,
                ShadowColor = Color.black,
                MidColor = Color.gray,
                HighlightColor = Color.white,
                ReduceColors = true,
                ColorCountPreset = PixelColorCountPreset.Custom,
                PalettePreset = PixelPalettePreset.Custom,
                MaxColors = 7,
                UseDithering = true,
                CustomPalette = new[] { Color.black, Color.white },
                MotionDecimation = 3,
                FrameHold = 4
            };

            var config = PixelAnimationCli.CreateConfig(settings);

            Assert.That(config.prefabPath, Is.EqualTo(AssetDatabase.GetAssetPath(prefab)));
            Assert.That(config.animationClipPath, Is.EqualTo($"{TempRoot}/CliRoundTrip.anim"));
            Assert.That(config.outputFolderPath, Is.EqualTo(settings.OutputFolderPath));
            Assert.That(config.resolutionX, Is.EqualTo(96));
            Assert.That(config.resolutionY, Is.EqualTo(80));
            Assert.That(config.fps, Is.EqualTo(18));
            Assert.That(config.useFullClipLength, Is.False);
            Assert.That(config.durationSeconds, Is.EqualTo(2.5f));
            Assert.That(config.useFrameRange, Is.True);
            Assert.That(config.startFrame, Is.EqualTo(2));
            Assert.That(config.endFrame, Is.EqualTo(9));
            Assert.That(config.backgroundColor, Is.EqualTo(settings.BackgroundColor));
            Assert.That(config.transparentBackground, Is.False);
            Assert.That(config.cameraYaw, Is.EqualTo(33f));
            Assert.That(config.cameraPitch, Is.EqualTo(-12f));
            Assert.That(config.cameraZoom, Is.EqualTo(2.25f));
            Assert.That(config.directionYaws, Is.EqualTo(new[] { 0f, 90f, 180f }));
            Assert.That(config.useOrthographicCamera, Is.False);
            Assert.That(config.useLighting, Is.False);
            Assert.That(config.includeShadows, Is.False);
            Assert.That(config.forceUnlitMaterials, Is.True);
            Assert.That(config.materialPreset, Is.EqualTo(PixelAnimationMaterialPreset.HighContrast));
            Assert.That(config.outputMode, Is.EqualTo(PixelAnimationOutputMode.Both));
            Assert.That(config.writeMetadataJson, Is.True);
            Assert.That(config.applySpriteImportSettings, Is.False);
            Assert.That(config.writeGifPreview, Is.True);
            Assert.That(config.autoTrim, Is.True);
            Assert.That(config.trimPadding, Is.EqualTo(3));
            Assert.That(config.pixelSnap, Is.False);
            Assert.That(config.snapModelToPixelGrid, Is.True);
            Assert.That(config.applyOutline, Is.True);
            Assert.That(config.outlineColor, Is.EqualTo(Color.red));
            Assert.That(config.outlineThickness, Is.EqualTo(2));
            Assert.That(config.outlineMode, Is.EqualTo(PixelOutlineMode.Both));
            Assert.That(config.enhanceEdges, Is.True);
            Assert.That(config.edgeColor, Is.EqualTo(Color.blue));
            Assert.That(config.edgeThreshold, Is.EqualTo(0.6f));
            Assert.That(config.removeAntiAliasing, Is.True);
            Assert.That(config.alphaThreshold, Is.EqualTo(0.7f));
            Assert.That(config.shadeSteps, Is.EqualTo(24));
            Assert.That(config.useFixedShadeColors, Is.True);
            Assert.That(config.shadowColor, Is.EqualTo(Color.black));
            Assert.That(config.midColor, Is.EqualTo(Color.gray));
            Assert.That(config.highlightColor, Is.EqualTo(Color.white));
            Assert.That(config.reduceColors, Is.True);
            Assert.That(config.colorCountPreset, Is.EqualTo(PixelColorCountPreset.Custom));
            Assert.That(config.palettePreset, Is.EqualTo(PixelPalettePreset.Custom));
            Assert.That(config.maxColors, Is.EqualTo(7));
            Assert.That(config.useDithering, Is.True);
            Assert.That(config.customPalette, Has.Length.EqualTo(2));
            Assert.That(config.motionDecimation, Is.EqualTo(3));
            Assert.That(config.frameHold, Is.EqualTo(4));
        }

        [Test]
        public void Cli_ExportFromConfig_WritesExpectedFiles()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            AssetDatabase.CreateAsset(clip, $"{TempRoot}/CliExportMove.anim");
            var configPath = $"{TempRoot}/cli-config.json";
            var config = new PixelAnimationCliConfig
            {
                prefabPath = AssetDatabase.GetAssetPath(prefab),
                animationClipPath = $"{TempRoot}/CliExportMove.anim",
                outputFolderPath = $"{TempRoot}/CliExports",
                resolutionX = 32,
                resolutionY = 32,
                fps = 2,
                useFullClipLength = false,
                durationSeconds = 1f,
                transparentBackground = true
            };
            File.WriteAllText(configPath, JsonUtility.ToJson(config, true));

            var result = PixelAnimationCli.ExportFromConfig(configPath);

            Assert.That(result.GeneratedFiles, Has.Count.EqualTo(2));
            Assert.That(File.Exists($"{TempRoot}/CliExports/PixelTestCube/CliExportMove/CliExportMove_0000.png"), Is.True);
        }

        [Test]
        public void BatchExporter_WritesAllNonNullClips()
        {
            var prefab = CreateCubePrefab();
            var firstClip = CreateAnimationClip();
            firstClip.name = "MoveA";
            var secondClip = CreateAnimationClip();
            secondClip.name = "MoveB";
            var batch = ScriptableObject.CreateInstance<PixelAnimationBatchSettings>();
            batch.Prefab = prefab;
            batch.AnimationClips = new[] { firstClip, null, secondClip };
            batch.OutputFolderPath = $"{TempRoot}/BatchExports";
            batch.Resolution = new Vector2Int(32, 32);
            batch.Fps = 4;

            try
            {
                var result = PixelAnimationBatchExporter.Export(batch);

                Assert.That(result.GeneratedFiles, Has.Count.EqualTo(8));
                Assert.That(File.Exists($"{TempRoot}/BatchExports/PixelTestCube/MoveA/MoveA_0000.png"), Is.True);
                Assert.That(File.Exists($"{TempRoot}/BatchExports/PixelTestCube/MoveB/MoveB_0000.png"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(batch);
            }
        }

        [Test]
        public void BatchExporter_UsesAnimatorControllerClips()
        {
            var prefab = CreateCubePrefab();
            var clip = CreateAnimationClip();
            clip.name = "ControllerMove";
            AssetDatabase.CreateAsset(clip, $"{TempRoot}/ControllerMove.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath($"{TempRoot}/Controller.controller");
            controller.AddMotion(clip);

            var batch = ScriptableObject.CreateInstance<PixelAnimationBatchSettings>();
            batch.Prefab = prefab;
            batch.AnimatorController = controller;
            batch.AnimationClips = Array.Empty<AnimationClip>();
            batch.OutputFolderPath = $"{TempRoot}/ControllerExports";
            batch.Resolution = new Vector2Int(32, 32);
            batch.Fps = 4;

            try
            {
                var result = PixelAnimationBatchExporter.Export(batch);

                Assert.That(result.GeneratedFiles, Has.Count.EqualTo(4));
                Assert.That(File.Exists($"{TempRoot}/ControllerExports/PixelTestCube/ControllerMove/ControllerMove_0000.png"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(batch);
            }
        }

        private static PixelAnimationExportSettings CreateValidSettings()
        {
            return new PixelAnimationExportSettings
            {
                Prefab = new GameObject("Validation Prefab"),
                AnimationClip = new AnimationClip(),
                OutputFolderPath = TempRoot,
                Resolution = new Vector2Int(64, 64),
                Fps = 12
            };
        }

        private static GameObject CreateCubePrefab()
        {
            return CreateCubePrefab(Color.red);
        }

        private static GameObject CreateCubePrefab(Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PixelTestCube";
            var renderer = cube.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            renderer.sharedMaterial = new Material(shader)
            {
                color = color
            };
            if (renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                renderer.sharedMaterial.SetColor("_BaseColor", color);
            }

            if (renderer.sharedMaterial.HasProperty("_Color"))
            {
                renderer.sharedMaterial.SetColor("_Color", color);
            }

            var prefabPath = $"{TempRoot}/PixelTestCube.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(cube, prefabPath);
            UnityEngine.Object.DestroyImmediate(cube);
            return prefab;
        }

        private static void InvokeApplyMaterialPreset(GameObject target, PixelAnimationExportSettings settings)
        {
            var method = typeof(PixelAnimationExporter).GetMethod(
                "ApplyMaterialPreset",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { target, settings });
        }

        private static Camera InvokeCreateCamera(PixelAnimationExportSettings settings)
        {
            var method = typeof(PixelAnimationExporter).GetMethod(
                "CreateCamera",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (Camera)method.Invoke(null, new object[] { settings });
        }

        private static void InvokeSetLayerRecursively(GameObject target, int layer)
        {
            var method = typeof(PixelAnimationExporter).GetMethod(
                "SetLayerRecursively",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { target, layer });
        }

        private static void InvokeNormalizeFrameCanvases(System.Collections.Generic.IList<Texture2D> frames)
        {
            var method = typeof(PixelAnimationExporter).GetMethod(
                "NormalizeFrameCanvases",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { frames });
        }

        private static void InvokeTrimFramesToSharedBounds(System.Collections.Generic.IList<Texture2D> frames, int padding, bool trim)
        {
            var method = typeof(PixelAnimationExporter).GetMethod(
                "TrimFramesToSharedBounds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { frames, padding, trim });
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path)), Is.True);
            return texture;
        }

        private static int CountOpaquePixels(Texture2D texture)
        {
            return texture.GetPixels32().Count(pixel => pixel.a > 8);
        }

        private static float GetOpaqueCenterX(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var total = 0f;
            var count = 0;
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a <= 8)
                    {
                        continue;
                    }

                    total += x;
                    count++;
                }
            }

            return count == 0 ? -1f : total / count;
        }

        private static bool PixelsEqual(Texture2D first, Texture2D second)
        {
            if (first.width != second.width || first.height != second.height)
            {
                return false;
            }

            return first.GetPixels32().SequenceEqual(second.GetPixels32());
        }

        private static int ReadLittleEndianShort(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8);
        }

        private static int[] ReadGifDelays(byte[] bytes)
        {
            var delays = new System.Collections.Generic.List<int>();
            for (var index = 0; index < bytes.Length - 7; index++)
            {
                if (bytes[index] == 0x21 && bytes[index + 1] == 0xF9)
                {
                    delays.Add(ReadLittleEndianShort(bytes, index + 4));
                }
            }

            return delays.ToArray();
        }

        private static AnimationClip CreateAnimationClip()
        {
            var clip = new AnimationClip
            {
                name = "Move",
                frameRate = 4f
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "localPosition.x",
                AnimationCurve.Linear(0f, -0.25f, 1f, 0.25f));
            return clip;
        }

        private static void DeleteTempRoot()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }

            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, true);
            }

            var tempParent = "Assets/Temp";
            if (AssetDatabase.IsValidFolder(tempParent) && Directory.GetFileSystemEntries(tempParent).Length == 0)
            {
                AssetDatabase.DeleteAsset(tempParent);
            }

            AssetDatabase.Refresh();
        }
    }
}
