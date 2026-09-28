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
  history via lupira-location-api's internal seam (~100 m quantized). Geotag lookups are soft — an asset
  becomes Ready without a label rather than failing. Failed attempts retry with exponential backoff.
- **Query** — keyset-paged list (time window, bbox, kind, status, located, place, source album, trash)
  with presigned thumbnail URLs, id lookup, a GeoJSON `FeatureCollection` endpoint shaped for a MapLibre
  source, stats, imported albums, place-label suggestions, and single-asset reads with a short-lived
  presigned original URL. A presigned GET is reused for half its expiry, so clients' URL-keyed image caches
  hit across refetches.
- **Trash** — `POST /photos/{id}/trash` / `restore` soft-delete without touching status or bytes. Trashed
  assets are left out of every read except `GET /photos?trashed=true` and `GET /photos/{id}`, and are
  purged after `Photos:TrashRetentionDays` (default 30; each DTO carries `purgesAt`). `DELETE /photos/trash`
  empties it now; `DELETE /photos/{id}` deletes permanently, trashed or not. A copy declared later still
  collapses onto a trashed canonical as a hidden duplicate and is purged with it.

## Surfaces

| Surface | Base path | Auth | Notes |
|---|---|---|---|
| REST (owner) | `/photos`, `/me` | OIDC JWT (`ApiPolicy`) | Declare/complete, list/lookup/map/stats/albums/places, get/update, trash/restore/empty-trash, delete, reprocess. |
| MCP (agent) | `/mcp` | OIDC JWT (`ApiPolicy`) | Streamable HTTP. Read-only: `list_photos`, `search_photos`, `photo_stats`. |
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
