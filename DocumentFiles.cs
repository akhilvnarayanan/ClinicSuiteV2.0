using System.Diagnostics;
using System.ComponentModel;

static class DocumentFiles
{
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };

    public static bool IsPdf(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);

    static string ImageMagickExecutable()
    {
        var packagedPath = Path.Combine(AppContext.BaseDirectory, "ImageMagick", "magick.exe");
        if (File.Exists(packagedPath))
            return packagedPath;

        var configuredPath = Environment.GetEnvironmentVariable("CLINIC_IMAGEMAGICK_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        if (!OperatingSystem.IsWindows())
            return "magick";

        throw new InvalidOperationException(
            "Image conversion is unavailable because the bundled ImageMagick component is missing. Reinstall Clinic Suite.");
    }

    public static async Task SaveAsPdfAsync(IFormFile file, string outputPath)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!IsPdf(extension) && !IsImage(extension))
            throw new InvalidDataException("Upload a PDF, JPG, JPEG, or PNG file.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (IsPdf(extension))
        {
            await using var output = File.Create(outputPath);
            await file.CopyToAsync(output);
            return;
        }

        var temporaryInput = Path.Combine(Path.GetTempPath(), $"clinic-document-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var input = File.Create(temporaryInput))
                await file.CopyToAsync(input);

            var startInfo = new ProcessStartInfo
            {
                FileName = ImageMagickExecutable(),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(temporaryInput);
            startInfo.ArgumentList.Add("-auto-orient");
            startInfo.ArgumentList.Add(outputPath);

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Image conversion is unavailable.");
            }
            catch (Win32Exception)
            {
                throw new InvalidOperationException("Image conversion is unavailable. Install ImageMagick and try again.");
            }
            using (process)
            {
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidDataException(string.IsNullOrWhiteSpace(error)
                    ? "The image could not be converted to PDF."
                    : "The image could not be converted to PDF: " + error.Trim());
            }
        }
        finally
        {
            try { if (File.Exists(temporaryInput)) File.Delete(temporaryInput); } catch { }
        }
    }
}