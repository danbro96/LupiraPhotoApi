namespace LupiraPhotoApi.Core.Application.Import;

public enum PersonRefKind
{
    /// <summary>The importing user (<c>--me-contact</c>).</summary>
    Me,

    Contact,

    /// <summary>Deliberately no photographer (a folder of photos <i>of</i> someone).</summary>
    None,

    /// <summary>A name not yet swapped for a contact id — blocks a real run.</summary>
    Unresolved,
}
