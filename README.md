# LupiraPhotoApi

A self-hosted **photo/video library API** for a family. Phones auto-upload camera-roll originals via
**presigned S3 URLs** (the bytes never proxy through the API), a background pipeline thumbnails and
**geotags** every asset, and clients query by time window or map viewport — the primary consumer is a
clustered photos layer on a map.

Per-user libraries: every asset belongs to the principal that declared it, and all reads are
owner-scoped. Bytes live in an S3-compatible object store (Garage); Postgres holds only asset metadata.

## What it does

- **Identity** — a local `Principal` is provisioned just-in-time from the caller's OIDC token. The OIDC
  `sub` is the durable anchor; email is a mutable lookup key. There is no shared user table.
- **Declare → upload → complete** — the client declares an asset with its MediaStore metadata
  (content type, size, taken-at, EXIF GPS if present) and receives a **presigned PUT** against the store's
  public host. Identity is deterministic over (principal, device, MediaStore id), so retries and
  re-installs upsert instead of duplicating. `complete` verifies the bytes (HEAD + size) and queues
  processing.
- **Processing** — a worker claims Uploaded assets (leased; crash-recoverable), renders a 512 px WebP
  thumbnail (Magick.NET for stills incl. HEIC; ffmpeg poster frames for video), and geotags: client EXIF
  GPS → reverse-geocode label via lupira-geo-api; no GPS → timestamp match against the owner's location
  history via lupira-location-api's internal seam (~100 m quantized). A hand-set location outranks all of
  these; a fix the GPS sweep rejected is skipped. Geotag lookups are soft — an asset becomes Ready without a label rather than failing. Failed
  attempts retry with exponential backoff.
- **Location corrections** — `PUT /photos/{id}/location` hand-sets coordinates (and optionally a label);
  `POST /photos/relocate` does it for every asset a selector matches (ids, or a time window narrowed by
  camera model and current coordinate — the shape of a camera's stale GPS fix; `dryRun` previews). The
  override is stored apart from the resolved geotag (`geotagSource: Manual`), so reprocessing re-applies
  it. `DELETE /photos/{id}/location` drops it and re-queues the asset to re-derive. Manual geotags show on
  the map but not in the measured-location density.
- **Capture-time corrections** — `PUT /photos/{id}/taken-at` hand-sets the time; `POST /photos/retime` shifts
  (`shiftBy`) or sets (`setTo`) it on every asset a selector matches (ids, or a time window narrowed by camera
  model and device; `dryRun` lists before/after). The derived time is kept beneath the override
  (`takenAtSource: Manual`), so an import re-run refreshes only that, and duplicate detection still matches a
  copy on the file's own time. `DELETE /photos/{id}/taken-at` restores the derived time. A photo geotagged from
  location history is re-queued on any time change, so its location follows the new time. For a whole camera,
  the import map's `clock <camera> = utc | ±HH:MM | <IANA zone>` line reads its EXIF in that zone instead
  (an EXIF offset still wins).
- **GPS sweep** — `POST /photos/gps-sweep` checks Ready photos with exact times, per phone or imported camera:
  a spike (over 1000 km/h to and from both neighbours within 6 h, which sit close to each other) and a repeat
  (one coordinate on two or more days — a stale fix, or a home). It reports counts and samples; `apply` rejects
  every spike plus the repeats listed in `rejectCoordinates`, re-queues them and returns their ids. Processing
  skips a rejected fix on every run, so the photo falls back to location history, else no location; the asset
  carries `gpsRejection` (coordinate and reason). `DELETE /photos/{id}/gps-rejection` undoes it.
- **Query** — keyset-paged list (time window, bbox, kind, status, located, place, source album, trash)
  with presigned thumbnail URLs, id lookup, a GeoJSON map layer clustered server-side (a point per photo
  when the viewport is sparse or at street level, else per-cell counts on a Web Mercator grid three levels
  below the viewport's zoom, so the response stays bounded), measured-location photo counts per ~100 m cell, stats, imported albums, place-label suggestions,
  and single-asset reads with a short-lived presigned original URL. A presigned GET is reused for half its
  expiry, so clients' URL-keyed image caches hit across refetches.
- **Trash** — `POST /photos/{id}/trash` / `restore` soft-delete without touching status or bytes. Trashed
  assets are left out of every read except `GET /photos?trashed=true` and `GET /photos/{id}`, and are
  purged after `Photos:TrashRetentionDays` (default 30; each DTO carries `purgesAt`). `DELETE /photos/trash`
  empties it now; `DELETE /photos/{id}` deletes permanently, trashed or not. A copy declared later still
  collapses onto a trashed canonical as a hidden duplicate and is purged with it.

## Surfaces

| Surface | Base path | Auth | Notes |
|---|---|---|---|
| REST (owner) | `/photos`, `/me` | OIDC JWT (`ApiPolicy`) | Declare/complete, list/lookup/map/density/stats/albums/places, get/update, set/clear location, relocate, set/clear capture time, retime, GPS sweep/restore, trash/restore/empty-trash, delete, reprocess. |
| MCP (agent) | `/mcp` | OIDC JWT (`ApiPolicy`) | Streamable HTTP. Reads: `list_photos`, `search_photos`, `photo_stats`; location corrections: `relocate_photos`, `clear_photo_location`; time corrections: `retime_photos`, `clear_photo_time`; GPS sweep: `sweep_photo_gps` (dry run unless `apply`), `restore_photo_gps`. |
| Health | `/livez`, `/readyz` | none | Liveness / readiness (Postgres + object store reachable). |
| OpenAPI | `/openapi/v1.json` | none | Generated at build time into `openapi/`. |
| API reference | `/scalar/v1` | none | Scalar interactive UI. |

The **MCP surface is intended to be LAN/WireGuard-only**: a defence-in-depth backstop 404s any `/mcp`
request that arrives bearing reverse-proxy edge headers (`CF-Ray` / `CF-Connecting-IP`). Keep it off the
public internet at your ingress. Upload/download bytes flow client ↔ object store via the presigned URLs —
route the store's public host through an ingress **without a request-body cap** (video originals upload as
a single PUT).

## Asset lifecycle

`Declared` →(complete: HEAD ok)→ `Uploaded` →(worker claim)→ `Processing` → `Ready` | `Failed`.
Retryable failures return to `Uploaded` with backoff; `Failed` is terminal until `POST /photos/{id}/reprocess`.
Stale `Declared` assets (never uploaded) are expired after 7 days. Trash is orthogonal to status: a
`TrashPurgeWorker` sweeps hourly and permanently deletes assets trashed longer than the retention — objects,
document, and any duplicates pointing at it, the same purge as `DELETE /photos/{id}`.

## Stack

.NET 10 minimal API · Marten (plain documents, `photo` schema) · AWSSDK.S3 against Garage
(path-style, dual internal/public client for presigning) · Magick.NET + ffmpeg · MCP over Streamable HTTP.

## Tests

- `dotnet test LupiraPhotoApi.slnx` — unit tests only (no infrastructure).
- `dotnet test tests/LupiraPhotoApi.IntegrationTests` — full REST + processing flow against an ephemeral
  Postgres (Testcontainers) and an in-process fake S3; requires Docker.

## Deploy

`deploy/compose.yaml` is a genericized sample (every value `${VAR:-default}`-overridable);
`deploy/db/grants.sql` provisions the role + database. Schema apply is a deliberate one-shot:
`dotnet LupiraPhotoApi.dll --apply-schema` (Marten schema + bucket existence check).
