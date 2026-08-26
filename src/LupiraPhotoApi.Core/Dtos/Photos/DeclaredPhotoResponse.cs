using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Declare outcome. UploadUrl is present only while the asset still needs bytes (status
/// Declared); an already-uploaded asset returns its status so the client skips the transfer.</summary>
public sealed class DeclaredPhotoResponse
{
    public required Guid AssetId { get; set; }

    public required AssetStatus Status { get; set; }

    public string? UploadUrl { get; set; }

    public DateTimeOffset? UploadExpiresAt { get; set; }

    /// <summary>Headers the PUT must echo — they are part of the presigned signature.</summary>
    public required Dictionary<string, string> RequiredHeaders { get; set; }
}
