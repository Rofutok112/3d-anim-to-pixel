using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AnimToPixel.Editor
{
    public static class PixelAnimationGifWriter
    {
        public static void Write(string path, IReadOnlyList<Texture2D> frames, int fps)
        {
            if (frames == null || frames.Count == 0)
            {
                throw new ArgumentException("At least one frame is required.", nameof(frames));
            }

            var width = 0;
            var height = 0;
            foreach (var frame in frames)
            {
                width = Mathf.Max(width, frame.width);
                height = Mathf.Max(height, frame.height);
            }

            var delays = BuildFrameDelays(frames.Count, fps);

            using var stream = File.Create(path);
            WriteAscii(stream, "GIF89a");
            WriteShort(stream, width);
            WriteShort(stream, height);
            stream.WriteByte(0xF7);
            stream.WriteByte(0);
            stream.WriteByte(0);
            WritePalette(stream);
            WriteLoopExtension(stream);

            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var frame = frames[frameIndex];
                WriteGraphicControlExtension(stream, delays[frameIndex]);
                stream.WriteByte(0x2C);
                WriteShort(stream, 0);
                WriteShort(stream, 0);
                WriteShort(stream, width);
                WriteShort(stream, height);
                stream.WriteByte(0);
                stream.WriteByte(8);
                WriteImageData(stream, ToIndexedPixels(frame, width, height));
            }

            stream.WriteByte(0x3B);
        }

        private static int[] BuildFrameDelays(int frameCount, int fps)
        {
            var delays = new int[frameCount];
            var centisecondsPerFrame = 100.0 / Math.Max(1, fps);
            var accumulated = 0.0;
            var emitted = 0;

            for (var index = 0; index < frameCount; index++)
            {
                accumulated += centisecondsPerFrame;
                var next = Math.Max(emitted + 1, (int)Math.Round(accumulated));
                delays[index] = Math.Max(1, next - emitted);
                emitted = next;
            }

            return delays;
        }

        private static byte[] ToIndexedPixels(Texture2D frame, int width, int height)
        {
            var source = frame.GetPixels32();
            var indexed = new byte[width * height];
            var offsetX = Mathf.Max(0, (width - frame.width) / 2);
            var offsetY = Mathf.Max(0, (height - frame.height) / 2);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var targetIndex = y * width + x;
                    var frameX = x - offsetX;
                    var frameYFromTop = y - offsetY;
                    if (frameX < 0 || frameYFromTop < 0 || frameX >= frame.width || frameYFromTop >= frame.height)
                    {
                        indexed[targetIndex] = 0;
                        continue;
                    }

                    var sourceY = frame.height - 1 - frameYFromTop;
                    var sourceIndex = sourceY * frame.width + frameX;
                    if (sourceIndex >= source.Length || source[sourceIndex].a <= 8)
                    {
                        indexed[targetIndex] = 0;
                        continue;
                    }

                    var red = source[sourceIndex].r >> 5;
                    var green = source[sourceIndex].g >> 5;
                    var blue = source[sourceIndex].b >> 6;
                    var rawIndex = (red << 5) | (green << 2) | blue;
                    indexed[targetIndex] = (byte)Mathf.Clamp(rawIndex + 1, 1, 255);
                }
            }

            return indexed;
        }

        private static void WritePalette(Stream stream)
        {
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.WriteByte(0);

            for (var paletteIndex = 1; paletteIndex < 256; paletteIndex++)
            {
                var rawIndex = paletteIndex - 1;
                var red = (rawIndex >> 5) & 0x07;
                var green = (rawIndex >> 2) & 0x07;
                var blue = rawIndex & 0x03;
                stream.WriteByte((byte)(red * 255 / 7));
                stream.WriteByte((byte)(green * 255 / 7));
                stream.WriteByte((byte)(blue * 255 / 3));
            }
        }

        private static void WriteLoopExtension(Stream stream)
        {
            stream.WriteByte(0x21);
            stream.WriteByte(0xFF);
            stream.WriteByte(11);
            WriteAscii(stream, "NETSCAPE2.0");
            stream.WriteByte(3);
            stream.WriteByte(1);
            WriteShort(stream, 0);
            stream.WriteByte(0);
        }

        private static void WriteGraphicControlExtension(Stream stream, int delay)
        {
            stream.WriteByte(0x21);
            stream.WriteByte(0xF9);
            stream.WriteByte(4);
            stream.WriteByte(0x09);
            WriteShort(stream, delay);
            stream.WriteByte(0);
            stream.WriteByte(0);
        }

        private static void WriteImageData(Stream stream, byte[] indices)
        {
            var packedCodes = PackLiteralCodes(indices);
            for (var offset = 0; offset < packedCodes.Count; offset += 255)
            {
                var count = Math.Min(255, packedCodes.Count - offset);
                stream.WriteByte((byte)count);
                stream.Write(packedCodes.GetRange(offset, count).ToArray(), 0, count);
            }

            stream.WriteByte(0);
        }

        private static List<byte> PackLiteralCodes(byte[] indices)
        {
            const int clearCode = 256;
            const int endCode = 257;
            var bytes = new List<byte>();
            var bitBuffer = 0;
            var bitCount = 0;
            var codeSize = 9;
            var dictionarySize = 258;
            var emittedPixels = 0;

            WriteCode(clearCode, codeSize, bytes, ref bitBuffer, ref bitCount);
            foreach (var index in indices)
            {
                if (dictionarySize >= 4095)
                {
                    WriteCode(clearCode, codeSize, bytes, ref bitBuffer, ref bitCount);
                    codeSize = 9;
                    dictionarySize = 258;
                    emittedPixels = 0;
                }

                WriteCode(index, codeSize, bytes, ref bitBuffer, ref bitCount);
                if (emittedPixels > 0)
                {
                    dictionarySize++;
                    if (dictionarySize == 1 << codeSize && codeSize < 12)
                    {
                        codeSize++;
                    }
                }

                emittedPixels++;
            }

            WriteCode(endCode, codeSize, bytes, ref bitBuffer, ref bitCount);
            if (bitCount > 0)
            {
                bytes.Add((byte)(bitBuffer & 0xFF));
            }

            return bytes;
        }

        private static void WriteCode(int code, int codeSize, List<byte> bytes, ref int bitBuffer, ref int bitCount)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                bytes.Add((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteShort(Stream stream, int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
