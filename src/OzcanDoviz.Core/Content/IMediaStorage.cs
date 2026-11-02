namespace OzcanDoviz.Core.Content;

public sealed record MediaFile(string Name, string Url, long Size, DateTime CreatedUtc);

/// <summary>Panelden yüklenen fotoğraflar. Yalnızca JPEG/PNG/WebP, içerik imzası kontrol edilir.</summary>
public interface IMediaStorage
{
    Task<MediaFile> SaveAsync(Stream content, string fileName, long length, CancellationToken cancellationToken);
    IReadOnlyList<MediaFile> List();
    bool Delete(string name);
    /// <summary>Bilgi Toplum Hizmetleri dokümanı (PDF, Office, JPEG, PNG); içerik imzası kontrol edilir.</summary>
    Task<MediaFile> SaveDocumentAsync(Stream content, string fileName, long length, CancellationToken cancellationToken);
    bool DeleteDocument(string name);
}

public sealed class MediaValidationException(string message) : Exception(message);
