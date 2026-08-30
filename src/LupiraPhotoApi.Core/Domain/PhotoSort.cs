namespace LupiraPhotoApi.Core.Domain;

/// <summary>List ordering. Both directions ride the same `(taken_at, id)` index, which is why they are
/// the only two offered — any other key needs its own duplicated column and keyset predicate.</summary>
public enum PhotoSort
{
    TakenAtDesc,
    TakenAtAsc,
}
