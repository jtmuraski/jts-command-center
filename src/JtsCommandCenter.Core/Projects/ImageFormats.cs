namespace JtsCommandCenter.Core.Projects;

/// <summary>
/// Identifies allowed image types from a file's first bytes, so the browser's claimed type and the file
/// name don't matter. SVG is deliberately not allowed: it can carry script.
/// </summary>
public static class ImageFormats
{
    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string Gif = "image/gif";
    public const string WebP = "image/webp";

    /// <summary>The image's content type, or null when it isn't a PNG, JPEG, GIF, or WebP.</summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP;
        }

        return null;
    }

    /// <summary>A file name safe to store and show: no folders or control characters, at most 255 characters.</summary>
    public static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName?.Replace('\\', '/') ?? "");
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return "image";
        }

        return name.Length > 255 ? name[..255] : name;
    }

    /// <summary>Markdown for showing an uploaded image, with the file name (minus extension) as alt text.</summary>
    public static string MarkdownFor(Attachment attachment)
    {
        var alt = Path.GetFileNameWithoutExtension(attachment.FileName);
        alt = new string(alt.Select(c => c is '[' or ']' or '\\' ? ' ' : c).ToArray()).Trim();
        return $"![{(alt.Length == 0 ? "image" : alt)}](/attachments/{attachment.Id})";
    }
}
