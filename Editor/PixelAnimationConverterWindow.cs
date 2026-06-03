using System;
using UnityEditor;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public sealed class PixelAnimationConverterWindow : EditorWindow
    {
        private static readonly Vector2 FixedWindowSize = new Vector2(960f, 700f);
        private const float LeftPaneWidth = 540f;
        private const float LeftContentWidth = 516f;
        private const float LeftContentBaseHeight = 1060f;
        private const float PaneGap = 6f;
        private const float ToolbarReservedHeight = 42f;
        private const float PreviewReservedHeight = 315f;
        private const float CompactNumberWidth = 50f;
        private const float CompactLabelWidth = 12f;
        private const float ShortSliderWidth = 110f;
        private const float ButtonHeight = 28f;

        private static GUIContent Content(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        private readonly PixelAnimationExportSettings settings = new PixelAnimationExportSettings();
        private PixelAnimationBatchSettings batchSettings;
        private DefaultAsset outputFolderAsset;
        private string lastOutputDirectory;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;
        private Texture2D previewTexture;
        private PixelPreviewScaleMode previewScaleMode = PixelPreviewScaleMode.Fit;
        private PixelPreviewBackgroundMode previewBackgroundMode = PixelPreviewBackgroundMode.Checker;
        private int previewFrameIndex;
        private bool pendingAutoPreview;
        private bool renderingPreview;
        private bool exporting;
        private bool playingPreview;
        private double nextPreviewFrameTime;
        private string lastPreviewSignature;
        private bool exportMultipleDirections;
        private int directionCount = 4;
        private Vector2 scrollPosition;
        private GUIStyle previewBoxStyle;
        private GUIStyle leftPaneStyle;

        [MenuItem("Tools/3D Anim To Pixel/Converter")]
        public static void Open()
        {
            var window = GetWindowWithRect<PixelAnimationConverterWindow>(
                new Rect(100f, 100f, FixedWindowSize.x, FixedWindowSize.y),
                true,
                "3D Anim To Pixel",
                true);
            window.minSize = FixedWindowSize;
            window.maxSize = FixedWindowSize;
            window.position = new Rect(window.position.x, window.position.y, FixedWindowSize.x, FixedWindowSize.y);
            window.Show();
        }

        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(settings.OutputFolderPath))
            {
                settings.OutputFolderPath = "Assets/Exports";
            }

            EditorApplication.update -= UpdatePreviewPlayback;
            EditorApplication.update += UpdatePreviewPlayback;
        }

        private void OnGUI()
        {
            EnsureStyles();
            var previewSignatureBefore = BuildPreviewSignature();
            EditorGUI.BeginChangeCheck();

            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPaneWidth)))
                {
                    DrawLeftPane();
                }

                GUILayout.Space(PaneGap);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    DrawPreviewPanel();
                }
            }

            EditorGUI.EndChangeCheck();
            var previewSignatureAfter = BuildPreviewSignature();
            var previewSettingsChanged = previewSignatureBefore != previewSignatureAfter || lastPreviewSignature != previewSignatureAfter;
            if (previewSettingsChanged
                && CanExport()
                && !renderingPreview
                && !exporting)
            {
                lastPreviewSignature = previewSignatureAfter;
                playingPreview = false;
                DestroyPreviewTexture();
                SetStatus("Preview updating...", MessageType.Info);
                QueueAutoPreview();
            }
        }

        private void EnsureStyles()
        {
            leftPaneStyle ??= new GUIStyle
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 8, 0, 0)
            };

            previewBoxStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(4, 4, 4, 4)
            };
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("3D Anim To Pixel", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(CanExport() ? $"{settings.FrameCount} frames" : "Select prefab and clip");
            }
        }

        private void DrawLeftPane()
        {
            var viewRect = GUILayoutUtility.GetRect(
                LeftPaneWidth,
                position.height - ToolbarReservedHeight,
                GUILayout.Width(LeftPaneWidth),
                GUILayout.ExpandHeight(true));
            var contentRect = new Rect(0f, 0f, LeftContentWidth, CalculateLeftContentHeight());

            scrollPosition = GUI.BeginScrollView(viewRect, scrollPosition, contentRect, false, true);
            GUILayout.BeginArea(contentRect, leftPaneStyle);
            DrawSettingsPanel();
            GUILayout.EndArea();
            GUI.EndScrollView();
        }

        private float CalculateLeftContentHeight()
        {
            var paletteRows = settings.CustomPalette == null ? 0 : settings.CustomPalette.Length;
            return LeftContentBaseHeight + Mathf.Max(0, paletteRows - 4) * EditorGUIUtility.singleLineHeight;
        }

        private void DrawSettingsPanel()
        {
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                settings.Prefab = (GameObject)EditorGUILayout.ObjectField(Content("Prefab", "ドット絵化したい3DモデルのPrefabを指定します。"), settings.Prefab, typeof(GameObject), false);
                settings.AnimationClip = (AnimationClip)EditorGUILayout.ObjectField(Content("Animation Clip", "書き出したいアニメーションClipを指定します。"), settings.AnimationClip, typeof(AnimationClip), false);
            }

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    outputFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(Content("Folder", "Project内の出力先フォルダを指定します。右のUseでPathへ反映します。"), outputFolderAsset, typeof(DefaultAsset), false);
                    if (GUILayout.Button(Content("Use", "選択したFolderを出力Pathに設定します。"), GUILayout.Width(48f)))
                    {
                        ApplySelectedOutputFolder();
                    }
                }

                settings.OutputFolderPath = EditorGUILayout.TextField(Content("Path", "PNG連番やSpriteSheetを書き出す保存先です。Assets配下ならUnityに自動取り込みされます。"), settings.OutputFolderPath);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Content("Resolution", "1フレームの出力解像度です。小さいほどドット絵らしくなります。"), GUILayout.Width(EditorGUIUtility.labelWidth));
                    EditorGUILayout.LabelField(Content("X", "横幅ピクセル数です。"), GUILayout.Width(CompactLabelWidth));
                    settings.Resolution.x = EditorGUILayout.IntField(settings.Resolution.x, GUILayout.Width(CompactNumberWidth));
                    EditorGUILayout.LabelField(Content("Y", "縦幅ピクセル数です。"), GUILayout.Width(CompactLabelWidth));
                    settings.Resolution.y = EditorGUILayout.IntField(settings.Resolution.y, GUILayout.Width(CompactNumberWidth));
                    GUILayout.Space(12f);
                    EditorGUILayout.LabelField(Content("FPS", "1秒あたりの書き出しフレーム数です。Preview再生速度にも使います。"), GUILayout.Width(28f));
                    settings.Fps = EditorGUILayout.IntField(settings.Fps, GUILayout.Width(CompactNumberWidth));
                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.UseFullClipLength = EditorGUILayout.Toggle(Content("Use Full Clip", "ONならAnimation Clip全体を対象にします。OFFならDuration秒だけ書き出します。"), settings.UseFullClipLength);
                    using (new EditorGUI.DisabledScope(settings.UseFullClipLength))
                    {
                        settings.DurationSeconds = EditorGUILayout.FloatField(Content("Duration", "Use Full ClipがOFFのときに使う書き出し秒数です。"), settings.DurationSeconds);
                    }
                }

                settings.OutputMode = (PixelAnimationOutputMode)EditorGUILayout.EnumPopup(Content("Output Mode", "PNG連番、SpriteSheet、または両方を書き出します。"), settings.OutputMode);
                settings.WriteMetadataJson = EditorGUILayout.Toggle(Content("Write JSON", "解像度、FPS、フレーム範囲などのメタデータJSONも書き出します。"), settings.WriteMetadataJson);
                settings.WriteGifPreview = EditorGUILayout.Toggle(Content("Write GIF Preview", "確認・共有用のGIFプレビューも一緒に書き出します。"), settings.WriteGifPreview);
                settings.ApplySpriteImportSettings = EditorGUILayout.Toggle(Content("Apply Sprite Import", "書き出したPNGをSprite、Point Filter、圧縮なしとしてUnityに取り込みます。"), settings.ApplySpriteImportSettings);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Content("Frame Range", "ONならStartからEndまでのフレームだけを書き出します。"), GUILayout.Width(EditorGUIUtility.labelWidth));
                    settings.UseFrameRange = EditorGUILayout.Toggle(settings.UseFrameRange, GUILayout.Width(18f));
                    using (new EditorGUI.DisabledScope(!settings.UseFrameRange))
                    {
                        EditorGUILayout.LabelField(Content("Start", "書き出し開始フレームです。0始まりです。"), GUILayout.Width(36f));
                        settings.StartFrame = EditorGUILayout.IntField(settings.StartFrame, GUILayout.Width(CompactNumberWidth));
                        EditorGUILayout.LabelField(Content("End", "書き出し終了フレームです。このフレームも含みます。"), GUILayout.Width(28f));
                        settings.EndFrame = EditorGUILayout.IntField(settings.EndFrame, GUILayout.Width(CompactNumberWidth));
                    }

                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.MotionDecimation = EditorGUILayout.IntSlider(Content("Motion Step", "ポーズのサンプリング間隔です。2なら2フレームごとに拾い、コマ撮り感を強めます。"), settings.MotionDecimation, 1, 4);
                    settings.FrameHold = EditorGUILayout.IntSlider(Content("Frame Hold", "同じ絵を何フレーム保持するかです。2や3にすると昔のゲーム風の間になります。"), settings.FrameHold, 1, 3);
                }
            }

            EditorGUILayout.LabelField("Camera & Render", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawCameraPresetButtons();

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.TransparentBackground = EditorGUILayout.Toggle(Content("Transparent", "ONなら背景を透明にして書き出します。"), settings.TransparentBackground);
                    using (new EditorGUI.DisabledScope(settings.TransparentBackground))
                    {
                        settings.BackgroundColor = EditorGUILayout.ColorField(Content("Background", "TransparentがOFFのときに使う背景色です。"), settings.BackgroundColor);
                    }
                }

                settings.CameraYaw = EditorGUILayout.Slider(Content("Yaw", "カメラの左右回転角度です。正面、横、背面の調整に使います。"), settings.CameraYaw, -180f, 180f);
                settings.CameraPitch = EditorGUILayout.Slider(Content("Pitch", "カメラの上下角度です。見下ろしや見上げの調整に使います。"), settings.CameraPitch, -60f, 60f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Content("Zoom", "カメラの寄り引きです。値が大きいほど被写体が小さく写ります。"), GUILayout.Width(EditorGUIUtility.labelWidth));
                    settings.CameraZoom = GUILayout.HorizontalSlider(settings.CameraZoom, 0.5f, 3f, GUILayout.Width(ShortSliderWidth));
                    settings.CameraZoom = EditorGUILayout.FloatField(settings.CameraZoom, GUILayout.Width(CompactNumberWidth));
                    settings.UseOrthographicCamera = EditorGUILayout.Toggle(Content("Orthographic", "ONなら遠近感のない正射影で描画します。ドット絵素材では通常ON推奨です。"), settings.UseOrthographicCamera, GUILayout.Width(135f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    exportMultipleDirections = EditorGUILayout.Toggle(Content("Multiple Directions", "ONなら複数方向のカメラ角度をまとめて書き出します。"), exportMultipleDirections);
                    using (new EditorGUI.DisabledScope(!exportMultipleDirections))
                    {
                        directionCount = EditorGUILayout.IntSlider(Content("Direction Count", "一周360度を何方向に分けて書き出すかを指定します。"), directionCount, 2, 16);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.UseLighting = EditorGUILayout.Toggle(Content("Lighting", "ONなら書き出し用のライトを使います。FlatやSilhouetteではOFFも有効です。"), settings.UseLighting);
                    using (new EditorGUI.DisabledScope(!settings.UseLighting))
                    {
                        settings.IncludeShadows = EditorGUILayout.Toggle(Content("Shadows", "ONならライトによる影を有効にします。輪郭や立体感を出したいときに使います。"), settings.IncludeShadows);
                    }
                }

                settings.ForceUnlitMaterials = EditorGUILayout.Toggle(Content("Force Unlit", "書き出し時だけマテリアルをUnlitに差し替え、ライトや環境光の影響を受けにくくします。Prefab本体は変更しません。"), settings.ForceUnlitMaterials);
                settings.MaterialPreset = (PixelAnimationMaterialPreset)EditorGUILayout.EnumPopup(Content("Material Preset", "描画時の見た目プリセットです。Flat、HighContrast、Silhouetteなどを選べます。"), settings.MaterialPreset);
            }

            EditorGUILayout.LabelField("Pixel Processing", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.AutoTrim = EditorGUILayout.Toggle(Content("Auto Trim", "透明な余白を自動で切り詰めます。素材として使いやすくなります。"), settings.AutoTrim);
                    using (new EditorGUI.DisabledScope(!settings.AutoTrim))
                    {
                        settings.TrimPadding = EditorGUILayout.IntField(Content("Padding", "Auto Trim後に残す余白ピクセル数です。"), settings.TrimPadding);
                    }
                }

                settings.PixelSnap = EditorGUILayout.Toggle(Content("Pixel Snap", "カメラ中心をピクセル単位に寄せ、アニメ中の微妙な揺れを抑えます。"), settings.PixelSnap);
                settings.SnapModelToPixelGrid = EditorGUILayout.Toggle(Content("Snap Model", "モデルの位置も出力ピクセルのグリッドへ寄せます。移動アニメのサブピクセル揺れ対策です。"), settings.SnapModelToPixelGrid);

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.RemoveAntiAliasing = EditorGUILayout.Toggle(Content("Remove AA", "半透明の縁を透明/不透明に丸め、ぼやけたアンチエイリアスを減らします。"), settings.RemoveAntiAliasing);
                    using (new EditorGUI.DisabledScope(!settings.RemoveAntiAliasing))
                    {
                        settings.AlphaThreshold = EditorGUILayout.Slider(Content("Alpha", "この値以上の透明度を不透明として扱います。"), settings.AlphaThreshold, 0.05f, 0.95f);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.ShadeSteps = EditorGUILayout.IntSlider(Content("Shade Steps", "0でOFF、2〜32で陰影の段数です。少ないほど強いドット絵感、多いほど元の立体感を残します。"), settings.ShadeSteps, 0, 32);
                    if (settings.ShadeSteps == 1)
                    {
                        settings.ShadeSteps = 2;
                    }

                    using (new EditorGUI.DisabledScope(settings.ShadeSteps < 2))
                    {
                        settings.UseFixedShadeColors = EditorGUILayout.Toggle(Content("Fixed Shades", "陰影を指定した3色に置き換えます。色ブレを抑えたいときに使います。"), settings.UseFixedShadeColors);
                    }
                }

                using (new EditorGUI.DisabledScope(settings.ShadeSteps < 2 || !settings.UseFixedShadeColors))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        settings.ShadowColor = EditorGUILayout.ColorField(Content("Shadow", "暗部に使う固定色です。"), settings.ShadowColor);
                        settings.MidColor = EditorGUILayout.ColorField(Content("Mid", "中間調に使う固定色です。"), settings.MidColor);
                        settings.HighlightColor = EditorGUILayout.ColorField(Content("Highlight", "明部に使う固定色です。"), settings.HighlightColor);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.ApplyOutline = EditorGUILayout.Toggle(Content("Outline", "透明部分との境界に簡易アウトラインを追加します。"), settings.ApplyOutline);
                    using (new EditorGUI.DisabledScope(!settings.ApplyOutline))
                    {
                        settings.OutlineColor = EditorGUILayout.ColorField(Content("Outline Color", "アウトラインに使う色です。"), settings.OutlineColor);
                    }
                }
                using (new EditorGUI.DisabledScope(!settings.ApplyOutline))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        settings.OutlineThickness = EditorGUILayout.IntSlider(Content("Thickness", "アウトラインの太さです。1pxか2pxを選べます。"), settings.OutlineThickness, 1, 2);
                        settings.OutlineMode = (PixelOutlineMode)EditorGUILayout.EnumPopup(Content("Mode", "外側、内側、両方のどこに輪郭を入れるかです。"), settings.OutlineMode);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.EnhanceEdges = EditorGUILayout.Toggle(Content("Edge Enhance", "明暗差や透明境界を検出し、細部の読みやすさを補強します。"), settings.EnhanceEdges);
                    using (new EditorGUI.DisabledScope(!settings.EnhanceEdges))
                    {
                        settings.EdgeColor = EditorGUILayout.ColorField(Content("Edge Color", "補強線に使う色です。"), settings.EdgeColor);
                    }
                }

                using (new EditorGUI.DisabledScope(!settings.EnhanceEdges))
                {
                    settings.EdgeThreshold = EditorGUILayout.Slider(Content("Edge Threshold", "明暗差をエッジとして扱う強さです。小さいほど線が増えます。"), settings.EdgeThreshold, 0.05f, 1f);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.ReduceColors = EditorGUILayout.Toggle(Content("Reduce Colors", "色数を制限して、よりドット絵らしい見た目にします。"), settings.ReduceColors);
                    using (new EditorGUI.DisabledScope(!settings.ReduceColors))
                    {
                        settings.ColorCountPreset = (PixelColorCountPreset)EditorGUILayout.EnumPopup(Content("Colors", "4/8/16/32色のプリセットです。CustomならMax Colorsを直接指定できます。"), settings.ColorCountPreset);
                        settings.UseDithering = EditorGUILayout.Toggle(Content("Dithering", "色の境目に細かなパターンを入れて階調を表現します。"), settings.UseDithering);
                    }
                }

                using (new EditorGUI.DisabledScope(!settings.ReduceColors))
                {
                    settings.PalettePreset = (PixelPalettePreset)EditorGUILayout.EnumPopup(Content("Palette", "固定パレットを使うとフレームごとの色ブレを抑えられます。Autoはフレームごとに自動抽出します。"), settings.PalettePreset);
                    using (new EditorGUI.DisabledScope(settings.ColorCountPreset != PixelColorCountPreset.Custom || settings.PalettePreset != PixelPalettePreset.Auto))
                    {
                        settings.MaxColors = EditorGUILayout.IntSlider(Content("Max Colors", "CustomかつAutoパレットのときに使う最大色数です。"), settings.MaxColors, 2, 256);
                    }
                    DrawPaletteControls();
                }
            }

            EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                batchSettings = (PixelAnimationBatchSettings)EditorGUILayout.ObjectField(Content("Batch Asset", "複数Animation ClipやAnimatorControllerをまとめて書き出す設定アセットです。"), batchSettings, typeof(PixelAnimationBatchSettings), false);
                using (new EditorGUI.DisabledScope(batchSettings == null))
                {
                    if (GUILayout.Button(Content("Run Batch Asset", "Batch Settingsに登録した複数ClipやAnimatorControllerをまとめて書き出します。"), GUILayout.Height(24f)))
                    {
                        ExportBatch();
                    }
                }
            }
        }

        private void DrawPreviewPanel()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(previewBoxStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                previewScaleMode = DrawPreviewScaleToolbar(previewScaleMode);
                previewBackgroundMode = (PixelPreviewBackgroundMode)EditorGUILayout.EnumPopup(Content("Background", "プレビューの背景表示です。透明確認にはCheckerが便利です。"), previewBackgroundMode);
                var maxFrame = Mathf.Max(0, settings.FrameCount - 1);
                var previousFrameIndex = previewFrameIndex;
                if (playingPreview)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.IntSlider(Content("Frame", "プレビューするフレーム番号です。Play中はFPSに合わせて進みます。"), Mathf.Clamp(previewFrameIndex, 0, maxFrame), 0, maxFrame);
                    }
                }
                else
                {
                    previewFrameIndex = EditorGUILayout.IntSlider(Content("Frame", "プレビューするフレーム番号です。Play中はFPSに合わせて進みます。"), Mathf.Clamp(previewFrameIndex, 0, maxFrame), 0, maxFrame);
                }

                if (!playingPreview && previewFrameIndex != previousFrameIndex && CanExport() && !renderingPreview && !exporting)
                {
                    RefreshPreviewFrame();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!CanExport()))
                    {
                        if (GUILayout.Button(Content(playingPreview ? "Stop" : "Play", "PreviewをFPSに合わせて再生/停止します。"), GUILayout.Height(ButtonHeight)))
                        {
                            TogglePreviewPlayback();
                        }
                    }
                }

                DrawPreviewTexture();

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!CanExport() || exporting))
                {
                    if (GUILayout.Button(Content(exporting ? "Exporting..." : "Export", "現在の設定でPNG連番などを書き出します。"), GUILayout.Height(34f)))
                    {
                        Export();
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastOutputDirectory)))
                {
                    if (GUILayout.Button(Content("Open Output Folder", "最後に書き出したフォルダをOSのファイルビューアで開きます。"), GUILayout.Height(24f)))
                    {
                        EditorUtility.RevealInFinder(lastOutputDirectory);
                    }
                }

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    EditorGUILayout.HelpBox(statusMessage, statusType);
                }
            }
        }

        private void DrawPreviewTexture()
        {
            var rect = GUILayoutUtility.GetRect(
                0f,
                position.height - PreviewReservedHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            rect.xMin += 1f;
            rect.xMax -= 1f;
            DrawPreviewBackground(rect);

            if (previewTexture != null)
            {
                GUI.DrawTexture(GetPreviewTextureRect(rect, previewTexture), previewTexture, ScaleMode.ScaleToFit, true);
                return;
            }

            var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(rect, "No Preview", labelStyle);
        }

        private void DrawCameraPresetButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Content("Front", "正面から見るカメラ角度にします。"))) SetCameraPreset(0f, 15f);
                if (GUILayout.Button(Content("Back", "背面から見るカメラ角度にします。"))) SetCameraPreset(180f, 15f);
                if (GUILayout.Button(Content("Left", "左側面から見るカメラ角度にします。"))) SetCameraPreset(-90f, 15f);
                if (GUILayout.Button(Content("Right", "右側面から見るカメラ角度にします。"))) SetCameraPreset(90f, 15f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Content("3/4", "斜め前から見る、キャラクター素材で使いやすい角度です。"))) SetCameraPreset(45f, 15f);
                if (GUILayout.Button(Content("Top-ish", "少し上から見下ろす角度です。"))) SetCameraPreset(45f, 45f);
            }
        }

        private static PixelPreviewScaleMode DrawPreviewScaleToolbar(PixelPreviewScaleMode current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(Content("Scale", "プレビュー表示倍率です。1x/2x/4xで実ピクセルの潰れ方を確認できます。"), GUILayout.Width(EditorGUIUtility.labelWidth));
                var selected = GUILayout.Toolbar((int)current, new[]
                {
                    Content("Fit", "枠に合わせて拡大表示します。"),
                    Content("1x", "画像を実寸ピクセルで表示します。"),
                    Content("2x", "画像を2倍で表示します。"),
                    Content("4x", "画像を4倍で表示します。")
                });
                return (PixelPreviewScaleMode)selected;
            }
        }

        private void SetCameraPreset(float yaw, float pitch)
        {
            settings.CameraYaw = yaw;
            settings.CameraPitch = pitch;
            QueueAutoPreview();
        }

        private Rect GetPreviewTextureRect(Rect bounds, Texture2D texture)
        {
            if (texture == null)
            {
                return bounds;
            }

            if (previewScaleMode == PixelPreviewScaleMode.Fit)
            {
                var textureAspect = texture.width / (float)Mathf.Max(1, texture.height);
                var boundsAspect = bounds.width / Mathf.Max(1f, bounds.height);
                var fittedWidth = bounds.width;
                var fittedHeight = bounds.height;
                if (boundsAspect > textureAspect)
                {
                    fittedWidth = fittedHeight * textureAspect;
                }
                else
                {
                    fittedHeight = fittedWidth / textureAspect;
                }

                return new Rect(
                    bounds.x + (bounds.width - fittedWidth) * 0.5f,
                    bounds.y + (bounds.height - fittedHeight) * 0.5f,
                    fittedWidth,
                    fittedHeight);
            }

            var scale = previewScaleMode switch
            {
                PixelPreviewScaleMode.OneX => 1f,
                PixelPreviewScaleMode.TwoX => 2f,
                PixelPreviewScaleMode.FourX => 4f,
                _ => 1f
            };
            var width = Mathf.Min(bounds.width, texture.width * scale);
            var height = Mathf.Min(bounds.height, texture.height * scale);
            return new Rect(
                bounds.x + (bounds.width - width) * 0.5f,
                bounds.y + (bounds.height - height) * 0.5f,
                width,
                height);
        }

        private void DrawPreviewBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
            if (previewBackgroundMode == PixelPreviewBackgroundMode.Solid)
            {
                return;
            }

            var cellSize = previewBackgroundMode == PixelPreviewBackgroundMode.Grid ? 16f : 12f;
            var light = new Color(0.28f, 0.28f, 0.28f);
            var dark = new Color(0.18f, 0.18f, 0.18f);

            for (var y = rect.yMin; y < rect.yMax; y += cellSize)
            {
                for (var x = rect.xMin; x < rect.xMax; x += cellSize)
                {
                    var cell = new Rect(x, y, Mathf.Min(cellSize, rect.xMax - x), Mathf.Min(cellSize, rect.yMax - y));
                    if (previewBackgroundMode == PixelPreviewBackgroundMode.Checker)
                    {
                        var even = ((int)((x - rect.xMin) / cellSize) + (int)((y - rect.yMin) / cellSize)) % 2 == 0;
                        EditorGUI.DrawRect(cell, even ? light : dark);
                    }
                    else
                    {
                        EditorGUI.DrawRect(new Rect(cell.x, cell.y, cell.width, 1f), light);
                        EditorGUI.DrawRect(new Rect(cell.x, cell.y, 1f, cell.height), light);
                    }
                }
            }
        }

        private bool CanExport()
        {
            return settings.Prefab != null
                && settings.AnimationClip != null
                && !string.IsNullOrWhiteSpace(settings.OutputFolderPath);
        }

        private void ApplySelectedOutputFolder()
        {
            if (outputFolderAsset == null)
            {
                SetStatus("Select a folder asset first.", MessageType.Warning);
                return;
            }

            var path = AssetDatabase.GetAssetPath(outputFolderAsset);
            if (!AssetDatabase.IsValidFolder(path))
            {
                SetStatus("Selected asset is not a folder.", MessageType.Warning);
                return;
            }

            settings.OutputFolderPath = path;
            SetStatus($"Output folder set to {path}.", MessageType.Info);
        }

        private async void Export()
        {
            if (exporting)
            {
                return;
            }

            try
            {
                exporting = true;
                playingPreview = false;
                ApplyDirectionSettings();
                var result = await PixelAnimationExporter.ExportAsync(settings, (progress, message) =>
                    !EditorUtility.DisplayCancelableProgressBar("3D Anim To Pixel Export", message, progress));
                lastOutputDirectory = result.OutputDirectory;
                SetStatus($"Exported {result.GeneratedFiles.Count} PNG files to {result.OutputDirectory}.", MessageType.Info);
                Debug.Log($"3D Anim To Pixel exported {result.GeneratedFiles.Count} frames at {settings.Resolution.x}x{settings.Resolution.y}, {settings.Fps} FPS: {result.OutputDirectory}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Export canceled.", MessageType.Warning);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                exporting = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void ExportBatch()
        {
            try
            {
                if (batchSettings.Prefab == null)
                {
                    throw new ArgumentException("Batch Prefab is required.");
                }

                var result = PixelAnimationBatchExporter.Export(batchSettings);
                SetStatus($"Batch exported {result.GeneratedFiles.Count} files.", MessageType.Info);
                Debug.Log($"3D Anim To Pixel batch exported {result.GeneratedFiles.Count} files.");
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void RenderPreview()
        {
            try
            {
                renderingPreview = true;
                ApplyDirectionSettings();
                DestroyPreviewTexture();
                previewTexture = PixelAnimationExporter.RenderPreview(settings, previewFrameIndex);

                lastPreviewSignature = BuildPreviewSignature();
                SetStatus("Preview rendered.", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                renderingPreview = false;
            }
        }

        private void RefreshPreviewFrame()
        {
            RenderPreview();
        }

        private void ApplyDirectionSettings()
        {
            if (!exportMultipleDirections)
            {
                settings.DirectionYaws = new[] { settings.CameraYaw };
                return;
            }

            settings.DirectionYaws = new float[directionCount];
            for (var index = 0; index < directionCount; index++)
            {
                settings.DirectionYaws[index] = settings.CameraYaw + 360f * index / directionCount;
            }
        }

        private void DrawPaletteControls()
        {
            if (settings.PalettePreset != PixelPalettePreset.Custom)
            {
                return;
            }

            var palette = settings.CustomPalette ?? Array.Empty<Color>();
            var newCount = EditorGUILayout.IntField(Content("Palette Size", "Customパレットに登録する色数です。"), palette.Length);
            newCount = Mathf.Clamp(newCount, 0, 256);
            if (newCount != palette.Length)
            {
                Array.Resize(ref palette, newCount);
                settings.CustomPalette = palette;
            }

            for (var index = 0; index < palette.Length; index++)
            {
                palette[index] = EditorGUILayout.ColorField(Content($"Palette {index}", "固定パレットとして使う色です。"), palette[index]);
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (CanExport() && previewTexture == null && !renderingPreview && !exporting)
            {
                RenderPreview();
            }
        }

        private void TogglePreviewPlayback()
        {
            playingPreview = !playingPreview;

            if (playingPreview)
            {
                nextPreviewFrameTime = EditorApplication.timeSinceStartup + 1.0 / Mathf.Max(1, settings.Fps);
                RenderPreview();
            }
        }

        private void UpdatePreviewPlayback()
        {
            if (!playingPreview || !CanExport() || renderingPreview)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var interval = 1.0 / Mathf.Max(1, settings.Fps);
            if (now < nextPreviewFrameTime)
            {
                return;
            }

            if (nextPreviewFrameTime <= 0)
            {
                nextPreviewFrameTime = now;
            }

            while (nextPreviewFrameTime <= now)
            {
                nextPreviewFrameTime += interval;
            }

            var frameCount = Mathf.Max(1, settings.FrameCount);
            previewFrameIndex = (previewFrameIndex + 1) % frameCount;
            RenderPreview();

            Repaint();
        }

        private void QueueAutoPreview()
        {
            if (pendingAutoPreview)
            {
                return;
            }

            pendingAutoPreview = true;
            EditorApplication.delayCall += () =>
            {
                pendingAutoPreview = false;
                if (this != null && CanExport() && !exporting)
                {
                    RenderPreview();
                }
            };
        }

        private string BuildPreviewSignature()
        {
            var prefabId = GetObjectSignature(settings.Prefab);
            var clipId = GetObjectSignature(settings.AnimationClip);
            return string.Join("|",
                prefabId,
                clipId,
                settings.Resolution.x,
                settings.Resolution.y,
                settings.Fps,
                settings.UseFullClipLength,
                settings.DurationSeconds,
                settings.UseFrameRange,
                settings.StartFrame,
                settings.EndFrame,
                settings.MotionDecimation,
                settings.FrameHold,
                settings.TransparentBackground,
                settings.BackgroundColor,
                settings.CameraYaw,
                settings.CameraPitch,
                settings.CameraZoom,
                settings.UseOrthographicCamera,
                exportMultipleDirections,
                directionCount,
                settings.UseLighting,
                settings.IncludeShadows,
                settings.ForceUnlitMaterials,
                settings.MaterialPreset,
                settings.AutoTrim,
                settings.TrimPadding,
                settings.PixelSnap,
                settings.SnapModelToPixelGrid,
                settings.RemoveAntiAliasing,
                settings.AlphaThreshold,
                settings.ShadeSteps,
                settings.UseFixedShadeColors,
                settings.ShadowColor,
                settings.MidColor,
                settings.HighlightColor,
                settings.ApplyOutline,
                settings.OutlineColor,
                settings.OutlineThickness,
                settings.OutlineMode,
                settings.EnhanceEdges,
                settings.EdgeColor,
                settings.EdgeThreshold,
                settings.ReduceColors,
                settings.ColorCountPreset,
                settings.PalettePreset,
                settings.MaxColors,
                settings.UseDithering,
                BuildPaletteSignature(settings.CustomPalette));
        }

        private static string BuildPaletteSignature(Color[] palette)
        {
            if (palette == null || palette.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(",", Array.ConvertAll(palette, color => ColorUtility.ToHtmlStringRGBA(color)));
        }

        private static string GetObjectSignature(UnityEngine.Object target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(target);
            return string.IsNullOrEmpty(path) ? target.name : path;
        }

        private void OnDisable()
        {
            playingPreview = false;
            EditorApplication.update -= UpdatePreviewPlayback;
            DestroyPreviewTexture();
        }

        private void DestroyPreviewTexture()
        {
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

    }
}
