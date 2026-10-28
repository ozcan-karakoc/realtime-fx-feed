using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Tests;

public class SiteContentRulesTests
{
    [Theory]
    [InlineData("ozcan-doviz", true)]
    [InlineData("kuyumcu42", true)]
    [InlineData("Ozcan", false)]
    [InlineData("oz", false)]
    [InlineData("ozcan doviz", false)]
    [InlineData("../etc", false)]
    public void BrokerId_format(string id, bool ok) => Assert.Equal(ok, SiteContentRules.IsValidBrokerId(id));

    [Fact]
    public void Rejects_foreign_map_and_external_images()
    {
        var c = new SiteContent
        {
            Contact = new ContactSection { MapEmbedUrl = "https://evil.example/maps" },
            Slides = [new Slide { Title = "x", ImageUrl = "https://evil.example/a.png" }],
            About = new AboutSection { ImageUrl = "/media/../appsettings.json" },
        };
        var errors = SiteContentRules.Validate(c);

        Assert.Contains(errors, e => e.Contains("Harita"));
        Assert.Contains(errors, e => e.Contains("Slayt"));
        Assert.Contains(errors, e => e.Contains("Hakkımızda fotoğrafı"));
    }

    [Fact]
    public void Default_content_is_valid() => Assert.Empty(SiteContentRules.Validate(new SiteContent()));
}

public sealed class MediaStorageTests : IDisposable
{
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ozcan-media-" + Guid.NewGuid().ToString("N"));
    private readonly MediaStorage _storage;

    public MediaStorageTests()
    {
        _storage = new MediaStorage(Options.Create(new MediaOptions { Directory = _dir, MaxBytes = 1024 }), new FakeEnv());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<MediaFile> Save(byte[] bytes, string name) => _storage.SaveAsync(new MemoryStream(bytes), name, bytes.Length, default);

    [Fact]
    public async Task Saves_png_with_random_name()
    {
        var file = await Save(Png, "../../logo.PNG");

        Assert.StartsWith("/media/", file.Url);
        Assert.EndsWith(".png", file.Name);
        Assert.DoesNotContain("logo", file.Name);
        Assert.True(File.Exists(Path.Combine(_dir, file.Name)));
        Assert.Single(_storage.List());
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("sayfa.html")]
    [InlineData("resim.svg")]
    public async Task Rejects_non_image_extensions(string name) =>
        await Assert.ThrowsAsync<MediaValidationException>(() => Save(Png, name));

    [Fact]
    public async Task Rejects_content_that_does_not_match_extension()
    {
        var html = "<script>alert(1)</script>"u8.ToArray();
        await Assert.ThrowsAsync<MediaValidationException>(() => Save(html, "foto.jpg"));
        await Assert.ThrowsAsync<MediaValidationException>(() => Save(Png, "foto.jpg")); // PNG içerik, JPG uzantı
    }

    [Fact]
    public async Task Rejects_too_large()
    {
        var big = new byte[2048];
        Png.CopyTo(big, 0);
        await Assert.ThrowsAsync<MediaValidationException>(() => Save(big, "buyuk.png"));
        Assert.Empty(_storage.List());
    }

    [Fact]
    public async Task Delete_cannot_escape_folder()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_dir)!, "ozcan-outside-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(outside, Png);
        try
        {
            Assert.False(_storage.Delete("../" + Path.GetFileName(outside)));
            Assert.True(File.Exists(outside));

            var file = await Save(Png, "a.png");
            Assert.True(_storage.Delete(file.Name));
            Assert.Empty(_storage.List());
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
