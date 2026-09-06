using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Domain.Identity;
using Marten;
using Weasel.Core;

namespace LupiraPhotoApi.Core.Data;

/// <summary>Configures the Marten store for the Photo API in the <c>photo</c> schema: plain documents only
/// (identity + photo assets). Bytes live in the object store, never in Postgres. Enums serialize as strings.
/// TakenAt/Latitude/Longitude/Status are duplicated columns — the map (bbox+time) and worker-claim queries
/// hit real indexes instead of JSONB scans.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraPhoto(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "photo";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Unique sub: without it, concurrent first-sight logins fork one login into two principals.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub, i => i.IsUnique = true).Index(x => x.Email);

        opts.Schema.For<PhotoAsset>()
            .Index(x => x.PrincipalId)
            .Duplicate(x => x.TakenAt)
            .Duplicate(x => x.Latitude!)
            .Duplicate(x => x.Longitude!)
            .Duplicate(x => x.Status)
            // Duplicated so place search is an indexed ILIKE rather than a JSONB extraction per row.
            .Duplicate(x => x.PlaceLabel!)
            // Duplicate detection: the declare-time surrogate joins TakenAt+SizeBytes, the worker's
            // exact check is a Sha256 lookup, and DuplicateOfId carries the delete cascade.
            .Duplicate(x => x.SizeBytes)
            .Duplicate(x => x.Sha256!)
            .Duplicate(x => x.DuplicateOfId!);

        return opts;
    }
}
