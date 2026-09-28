namespace LupiraPhotoApi.Core.Application.Import;

public sealed class FolderInfo
{
    public required string Name { get; set; }

    public required string Title { get; set; }

    public DateOnly? Date { get; set; }

    public required FolderDatePrecision Precision { get; set; }
}
