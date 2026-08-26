using System.Diagnostics;
using LupiraPhotoApi.Core.Application.Processing;

namespace LupiraPhotoApi.Media;

/// <summary>WebP poster frame via the container's ffmpeg. Seeks 1 s in (falls back implicitly for
/// shorter clips — ffmpeg clamps), takes one frame, caps the long edge at 512.</summary>
public sealed class FfmpegVideoThumbnailer : IVideoThumbnailer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default)
    {
        var outputPath = $"{sourcePath}.thumb.webp";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add("scale='min(512,iw)':-2");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("webp");
            psi.ArgumentList.Add(outputPath);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"ffmpeg exceeded {Timeout.TotalSeconds:0}s on '{sourcePath}'.");
            }

            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask;
                throw new InvalidOperationException($"ffmpeg exited {process.ExitCode}: {Tail(stderr)}");
            }

            return new ThumbnailResult { WebpBytes = await File.ReadAllBytesAsync(outputPath, ct) };
        }
        finally
        {
            try
            {
                File.Delete(outputPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Tail(string text) => text.Length <= 500 ? text : text[^500..];
}
