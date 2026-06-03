using System.Collections.Generic;

namespace AnimToPixel.Editor
{
    public sealed class PixelAnimationExportResult
    {
        public PixelAnimationExportResult(string outputDirectory, IReadOnlyList<string> generatedFiles)
        {
            OutputDirectory = outputDirectory;
            GeneratedFiles = generatedFiles;
        }

        public string OutputDirectory { get; }
        public IReadOnlyList<string> GeneratedFiles { get; }
    }
}
