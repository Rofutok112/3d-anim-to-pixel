using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public static class PixelAnimationBatchExporter
    {
        public static PixelAnimationExportResult Export(PixelAnimationBatchSettings batchSettings)
        {
            if (batchSettings == null)
            {
                throw new ArgumentNullException(nameof(batchSettings));
            }

            if (batchSettings.Prefab == null)
            {
                throw new ArgumentException("Batch Prefab is required.", nameof(batchSettings));
            }

            var clips = GetClips(batchSettings);
            if (clips.Count == 0)
            {
                throw new ArgumentException("At least one AnimationClip or AnimatorController clip is required.", nameof(batchSettings));
            }

            var generatedFiles = new List<string>();
            var firstOutputDirectory = string.Empty;

            foreach (var clip in clips)
            {
                var result = PixelAnimationExporter.Export(batchSettings.CreateSettings(clip));
                if (string.IsNullOrEmpty(firstOutputDirectory))
                {
                    firstOutputDirectory = result.OutputDirectory;
                }

                generatedFiles.AddRange(result.GeneratedFiles);
            }

            if (generatedFiles.Count == 0)
            {
                throw new ArgumentException("At least one non-null AnimationClip is required.", nameof(batchSettings));
            }

            return new PixelAnimationExportResult(firstOutputDirectory, generatedFiles);
        }

        private static IReadOnlyList<AnimationClip> GetClips(PixelAnimationBatchSettings batchSettings)
        {
            var clips = new List<AnimationClip>();
            if (batchSettings.AnimationClips != null)
            {
                clips.AddRange(batchSettings.AnimationClips.Where(clip => clip != null));
            }

            if (batchSettings.AnimatorController != null)
            {
                clips.AddRange(batchSettings.AnimatorController.animationClips.Where(clip => clip != null));
            }

            return clips.Distinct().ToArray();
        }
    }
}
