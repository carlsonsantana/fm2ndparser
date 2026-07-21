using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Fm2ndParser.Tests
{
    public class PlayerExportTests
    {
        // Fixture files live in Fm2ndParser.Tests/Fixtures and are copied next to the
        // test binary. Drop your sample .player and the reference BMPs there.
        private const string PlayerFixture = "character.player";
        private static readonly string[] ReferenceImages = { "0000.bmp", "0001.bmp" };

        [Fact]
        public void Player_Exports_Exactly_Two_Images_Matching_References_Except_Transparent()
        {
            var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            var playerPath = Path.Combine(fixturesDir, PlayerFixture);
            Assert.True(File.Exists(playerPath), $"Missing fixture: {playerPath}");

            // Run the CLI end-to-end in an isolated working directory, since the tool
            // writes output relative to the current working directory.
            var workDir = Path.Combine(Path.GetTempPath(), "fm2nd-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            try
            {
                RunExport(playerPath, workDir);

                var baseName = Path.GetFileNameWithoutExtension(PlayerFixture);
                var imgDir = Path.Combine(workDir, baseName, "img");
                Assert.True(Directory.Exists(imgDir), $"Export produced no img/ folder at {imgDir}");

                var exported = Directory.GetFiles(imgDir, "*.bmp").OrderBy(p => p).ToArray();
                Assert.Equal(2, exported.Length);

                for (int i = 0; i < exported.Length; i++)
                {
                    var reference = Bmp.Load(Path.Combine(fixturesDir, ReferenceImages[i]));
                    var actual = Bmp.Load(exported[i]);
                    AssertMatchesExceptTransparent(reference, actual, Path.GetFileName(exported[i]));
                }
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
            }
        }

        // Max allowed per-channel difference for non-transparent pixels.
        //
        // 2D Fighter Maker 2nd stores palette colors as 5 bits per channel: the value
        // lives in the top 5 bits of each byte and the low 3 bits are always 0. The
        // exporter writes those bytes verbatim, so exported channels are multiples of 8
        // (e.g. white is 248, not 255). An image editor that re-saves the reference BMP
        // expands 5-bit to full 8-bit range (and re-quantizes mid-tones), so the same
        // logical color can shift by up to one 5-bit step. We therefore compare within
        // +/-8 instead of exact match. See docs/output-formats.md ("5-bit color precision").
        private const int ColorTolerance = 8;

        /// <summary>
        /// The exporter renders the transparent color with a different value than the
        /// reference uses. The transparent color is identified by the reference's
        /// top-right corner pixel. Everywhere the reference is NOT transparent the two
        /// images must match within <see cref="ColorTolerance"/>; everywhere it IS
        /// transparent the exported color must differ clearly (be remapped).
        /// </summary>
        private static void AssertMatchesExceptTransparent(Bmp reference, Bmp actual, string name)
        {
            Assert.True(reference.Width == actual.Width && reference.Height == actual.Height,
                $"{name}: size mismatch — reference {reference.Width}x{reference.Height}, " +
                $"exported {actual.Width}x{actual.Height}");

            var transparent = reference[reference.Width - 1, 0]; // top-right corner

            for (int y = 0; y < reference.Height; y++)
            {
                for (int x = 0; x < reference.Width; x++)
                {
                    var expected = reference[x, y];
                    var got = actual[x, y];

                    if (expected == transparent)
                    {
                        Assert.True(!Close(transparent, got),
                            $"{name}: transparent pixel at ({x},{y}) was not remapped — " +
                            $"expected a color clearly different from {transparent}, got {got}");
                    }
                    else
                    {
                        Assert.True(Close(expected, got),
                            $"{name}: pixel mismatch at ({x},{y}) — expected {expected} " +
                            $"(±{ColorTolerance}), got {got}");
                    }
                }
            }
        }

        private static bool Close(Rgb a, Rgb b) =>
            Math.Abs(a.R - b.R) <= ColorTolerance &&
            Math.Abs(a.G - b.G) <= ColorTolerance &&
            Math.Abs(a.B - b.B) <= ColorTolerance;

        private static void RunExport(string playerPath, string workDir)
        {
            // The main project is referenced, so its dll sits next to the test binary.
            var dll = Path.Combine(AppContext.BaseDirectory, "Fm2ndParser.dll");
            Assert.True(File.Exists(dll), $"Missing built exporter: {dll}");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add(playerPath);
            psi.ArgumentList.Add("-x");

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "Export process timed out");
            Assert.True(process.ExitCode == 0,
                $"Export failed (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }
    }
}
