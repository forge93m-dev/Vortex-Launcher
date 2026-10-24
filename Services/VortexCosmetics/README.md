# Vortex Cosmetics (Cloudflare Workers + D1)

Free-plan beta service; no R2, payments, skin uploads, Microsoft tokens or shared
admin secrets. The legacy cape service remains unchanged; the new account module
is **disabled by default**. See [ACCOUNTS.md](ACCOUNTS.md) before any deployment.
Deploy `worker.mjs` together with its imported `accounts.mjs` using a module-aware
bundle; do not paste just the entry file into a single-file dashboard deployment. Bind the dedicated
D1 database as `DB`, then apply `schema.sql` in its console. Existing databases and
other Workers are not used. `/health` reports whether a binding exists; `/v1/catalog`
is public. Clients explicitly opt in and batch only currently relevant UUIDs to
`/v1/lookup`; no server addresses, chat, worlds or coordinates are required.

Offline nicknames cannot prove Minecraft account ownership. Beta enrollment is
therefore invitation-only: the administrator provisions an exact in-game UUID with
a random 256-bit activation code hash in D1. `POST /v1/claim` atomically exchanges the
one-use code for a random client-owned 256-bit key hash. Never include an admin key
in a client JAR. `PUT /v1/cape` needs that profile's key; lookup exposes only UUID and
catalogue/custom content ID, never code/key hashes. Changing a nickname/UUID needs a new enrollment.
Codes/owner keys must be stored only locally and excluded from exports and logs.

Local enrollment helper: `node issue-invite.mjs <in-game-uuid>`. It creates a private
activation file and a hash-only INSERT statement under the administrator's home
`.vortex/cape-invites` folder, not inside the repository. Run that SQL in the dedicated
D1 console and privately give the one-use code file to the intended player. Existing
profiles are not overwritten. Verify a row was actually inserted before delivering
the code; lost owner keys require an explicit administrator recovery procedure.

Bundled catalogue IDs, legacy custom 64x32 capes and HD capes are supported; arbitrary external
texture URLs and paid features are not supported. Unknown/missing IDs mean no Vortex override. Use the
ordinary vanilla cape settings, invisibility/spectator checks and cape animation.

Custom capes (client 0.1.30 / launcher 0.1.33): Choose image opens an asynchronous
SDL native chooser attached to Minecraft's window. No AWT windows or changes to
Minecraft's `java.awt.headless=true` setting are required. Cancel/error/selection
are separate outcomes; reading a file never displays a network failure message.
PNG/JPEG photos up to 8 MiB, 4096 pixels per side and 8 megapixels are accepted;
dimensions are checked before decoding. ImageIO runs headless in a background
task with an explicit memory stream, not a disk cache or an AWT dialog.
The crop screen has fit/fill, zoom/pan, reset/cancel and a large preview. Photo
mode produces a 1024x512 atlas with 160x256 front/back art. The default Fit mode
preserves the entire image and adds dark bars instead of stretching. Ready-made
64x32 to 1024x512 power-of-two textures can use Cape texture mode. Exact 64x32
inputs default to texture mode for compatibility; other pictures default to Photo.
The preview is local until Save online is clicked. Only converted RGBA pixels,
not the original filename/path/image metadata, reach the fixed TLS service.
`PUT /v1/custom` requires the existing profile owner key. D1 stores at most one
custom image per enrolled UUID; replacement updates that row and the public cape
ID atomically. `GET /v1/textures/<sha256>` exposes exactly those public pixels.
Clients verify size and SHA-256, use at most four parallel texture requests, retain
at most 34 GPU textures, and never fetch arbitrary image URLs. Old clients that
omit `custom:true` in lookup receive `none` for uploaded designs. Selecting Vanilla
and saving hides the override; it does not erase the stored public image. Beta
profile activation is still required once, not on every image change.

HD protocol: `hd:<sha256>` identifies the complete envelope (VCP2 magic, big-endian
width/height, zlib RGBA pixels). `PUT /v2/custom` requires the same owner key and
validates dimensions, compressed size, exact bounded decompression and encoding
before its atomic profile/image update. `/v2/textures/<hash>` serves that envelope.
Maximum envelope size is 640 KiB; maximum decoded atlas is 1024x512 (2 MiB).
D1 stores one HD image per UUID in `cape_hd_textures`; the independent old 64x32
row is retained for compatibility, without resets or deletions. Lookup requires
`hd:true` to return HD IDs; older clients get `none` rather than a broken response.
Both client and server reject truncation, extra decompressed bytes and trailing
compressed data. New images are never decoded from arbitrary remote PNG/JPEG files.
The larger HTTP body allowance is used only for cape downloads, not Party requests.
At most 34 textures (under 68 MiB RGBA at maximum size) are retained.

Storage/runtime constraints checked against the official
[D1 limits](https://developers.cloudflare.com/d1/platform/limits/) and
[Workers compression stream API](https://developers.cloudflare.com/workers/runtime-apis/web-standards/).

Bodies are bounded at 4 KiB (12 KiB legacy upload, 900000 bytes HD upload), batches at 32 UUIDs, and per-isolate/IP traffic at 120
requests/minute with a bounded in-memory map. This is a best-effort abuse limit,
not a global DDoS quota guarantee. Workers/D1 free limits still apply. There are no
request-body logs in application code. Cloudflare platform logging is independent.
