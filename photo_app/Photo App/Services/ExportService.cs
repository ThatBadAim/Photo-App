using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SocialPrepTool.Models;
using System.Text.Json;
using System.Collections.Generic;

namespace SocialPrepTool.Services;

public class ExportService
{
    public async Task ExportJobAsync(EditRecord record, string destFolder, string originalFileName)
    {
        await Task.Run(() =>
        {
            using var sourceImage = Image.Load<Rgba32>(record.SourceFilePath);

            // Apply manual rotation if specified
            if (record.ManualRotationDegrees != 0)
            {
                var rotateMode = record.ManualRotationDegrees switch
                {
                    90 => RotateMode.Rotate90,
                    180 => RotateMode.Rotate180,
                    270 => RotateMode.Rotate270,
                    _ => RotateMode.None
                };
                if (rotateMode != RotateMode.None)
                {
                    sourceImage.Mutate(x => x.Rotate(rotateMode));
                }
            }

            // Calculate output resolution
            int maxSourceEdge = Math.Max(sourceImage.Width, sourceImage.Height);
            int targetLongEdge = Math.Min(maxSourceEdge, 4096);

            int targetWidth, targetHeight;
            if (record.TargetRatio == "1:1") { targetWidth = targetLongEdge; targetHeight = targetLongEdge; }
            else if (record.TargetRatio == "4:5") { targetHeight = targetLongEdge; targetWidth = (int)(targetLongEdge * 0.8); }
            else if (record.TargetRatio == "9:16") { targetHeight = targetLongEdge; targetWidth = (int)(targetLongEdge * 9.0 / 16.0); } // 9:16
            else { targetWidth = targetLongEdge; targetHeight = (int)(targetLongEdge * 9.0 / 16.0); } // 16:9

            Color bgColor = Color.Black;
            if (record.BackgroundMode == "solid")
            {
                if (Color.TryParse(record.BackgroundColor, out var parsed))
                {
                    bgColor = parsed;
                }
            }
            else
            {
                bgColor = Color.Transparent; // Background drawn later
            }

            using var finalOutput = new Image<Rgba32>(targetWidth, targetHeight);
            finalOutput.Mutate(x => x.BackgroundColor(bgColor));

            // Calculate foreground dimensions and position
            float fgScale = Math.Min((float)targetWidth / sourceImage.Width, (float)targetHeight / sourceImage.Height);
            int fgW = (int)(sourceImage.Width * fgScale);
            int fgH = (int)(sourceImage.Height * fgScale);

            int offsetX = (int)((targetWidth - fgW) * record.CropAnchorX);
            int offsetY = (int)((targetHeight - fgH) * record.CropAnchorY);

            // Background Fill
            finalOutput.Mutate(ctx =>
            {
                if (record.BackgroundMode != "solid")
                {
                    // Blur optimization: downscale, blur, upscale
                    using var bgImage = sourceImage.Clone(x =>
                    {
                        int downscaleFactor = 4;
                        int smallW = Math.Max(1, targetWidth / downscaleFactor);
                        int smallH = Math.Max(1, targetHeight / downscaleFactor);
                        float smallBlur = record.BlurStrength / (float)downscaleFactor;

                        x.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Crop,
                            Size = new Size(smallW, smallH)
                        });
                        if (smallBlur > 0)
                        {
                            x.GaussianBlur(smallBlur);
                        }
                        x.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Stretch,
                            Size = new Size(targetWidth, targetHeight)
                        });
                    });
                    ctx.DrawImage(bgImage, new Point(0, 0), 1f);
                }

                // Foreground
                using var fgImage = sourceImage.Clone(x => x.Resize(fgW, fgH));

                if (record.DropShadow)
                {
                    // Drop shadow optimization: generate a smaller shadow, blur, scale up
                    int dsFactor = 4;
                    int smallFgW = Math.Max(1, fgW / dsFactor);
                    int smallFgH = Math.Max(1, fgH / dsFactor);

                    using var shadowImage = new Image<Rgba32>(smallFgW, smallFgH);
                    shadowImage.Mutate(s => s.BackgroundColor(Color.Black));

                    int padding = 60 / dsFactor;
                    int offset = 30 / dsFactor;

                    using var paddedShadow = new Image<Rgba32>(smallFgW + padding, smallFgH + padding);
                    paddedShadow.Mutate(s =>
                    {
                        s.DrawImage(shadowImage, new Point(offset, offset), 0.6f);
                        s.GaussianBlur(12f / dsFactor);
                        s.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Stretch,
                            Size = new Size(fgW + 60, fgH + 60)
                        });
                    });
                    ctx.DrawImage(paddedShadow, new Point(offsetX - 30 + 4, offsetY - 30 + 4), 1f);
                }

                ctx.DrawImage(fgImage, new Point(offsetX, offsetY), 1f);
            });

            string baseName = Path.GetFileNameWithoutExtension(originalFileName) + "_" + record.TargetRatio.Replace(":", "x");
            string outPath = Path.Combine(destFolder, baseName + ".jpg");
            int counter = 1;
            FileStream fs = null;
            while (fs == null)
            {
                try
                {
                    fs = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException)
                {
                    outPath = Path.Combine(destFolder, $"{baseName}_{counter}.jpg");
                    counter++;
                    if (counter > 1000)
                    {
                        throw new IOException("Could not generate a unique filename for export after 1000 attempts.");
                    }
                }
            }

            using (fs)
            {
                finalOutput.SaveAsJpeg(fs, new JpegEncoder { Quality = 92 });
            }

            // Generate job log
            var log = new
            {
                exportedAt = DateTime.UtcNow,
                jobs = new[]
                {
                    new
                    {
                        source = originalFileName,
                        output = Path.GetFileName(outPath),
                        ratio = record.TargetRatio,
                        backgroundMode = record.BackgroundMode,
                        outputResolution = $"{targetWidth}x{targetHeight}",
                        warnings = new List<string>()
                    }
                }
            };

            string uniqueLogName = $"export_log_{Path.GetFileNameWithoutExtension(outPath)}.json";
            string logPath = Path.Combine(destFolder, uniqueLogName);
            using (var logFs = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(logFs))
            {
                writer.Write(JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
            }
        });
    }
}
