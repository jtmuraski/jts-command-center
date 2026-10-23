using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Tests.Projects;

public class ImageFormatsTests
{
    internal static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    [Fact]
    public void Detect_Png() => Assert.Equal(ImageFormats.Png, ImageFormats.Detect(PngHeader));

    [Fact]
    public void Detect_Jpeg() => Assert.Equal(ImageFormats.Jpeg, ImageFormats.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]));

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Detect_Gif(string signature) =>
        Assert.Equal(ImageFormats.Gif, ImageFormats.Detect(System.Text.Encoding.ASCII.GetBytes(signature + "\x01\x00")));

    [Fact]
    public void Detect_WebP() =>
        Assert.Equal(ImageFormats.WebP, ImageFormats.Detect("RIFF\x24\x00\x00\x00WEBPVP8 "u8.ToArray()));

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><body>hi</body></html>")]
    [InlineData("RIFF\x24\x00\x00\x00WAVEfmt ")]
    [InlineData("")]
    public void Detect_RejectsEverythingElse(string content) =>
        Assert.Null(ImageFormats.Detect(System.Text.Encoding.Latin1.GetBytes(content)));

    [Theory]
    [InlineData("screenshot.png", "screenshot.png")]
    [InlineData(@"C:\Users\me\Desktop\bug.png", "bug.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("bad\nname.png", "badname.png")]
    [InlineData("", "image")]
    [InlineData(null, "image")]
    public void CleanFileName_StripsFoldersAndControlCharacters(string? input, string expected) =>
        Assert.Equal(expected, ImageFormats.CleanFileName(input));

    [Fact]
    public void MarkdownFor_UsesTheNameAsAltText()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal($"![bug [1] report](/attachments/{id})".Replace("[1]", " 1 "),
            ImageFormats.MarkdownFor(new Attachment { Id = id, FileName = "bug [1] report.png" }));
        Assert.Equal($"![image](/attachments/{id})", ImageFormats.MarkdownFor(new Attachment { Id = id, FileName = ".png" }));
    }
}
