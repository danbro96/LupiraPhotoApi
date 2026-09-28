namespace LupiraPhotoApi.Core.Application.Import;

public sealed class PersonRef
{
    public required PersonRefKind Kind { get; set; }

    public Guid? ContactId { get; set; }

    /// <summary>The value as written, for reports.</summary>
    public required string Text { get; set; }
}
