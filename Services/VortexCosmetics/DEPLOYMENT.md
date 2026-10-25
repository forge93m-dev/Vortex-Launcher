# Prepared, NOT yet deployed — animated capes (2026-10-25)

Code and tests are ready; nothing has been deployed. The change adds the cape ids `nebula`, `flow` and `pulse`
(shared list in `capes.mjs`, used by `worker.mjs` and `wardrobe.mjs`). No database change is needed: `cape` is a
plain TEXT column without a constraint. Older clients ignore ids they do not know and show no cape for them.

1. `node test.mjs` and the wardrobe/accounts tests (they need Node.js with `node:sqlite`; Node was not installed
   when this was written, so the new assertions have not been run yet).
2. Deploy both workers that use the list, from the owner's signed-in Cloudflare account:
   `npx wrangler deploy` (cosmetics, `wrangler.jsonc`) and `npx wrangler deploy -c wrangler.accounts.jsonc`
   (accounts/wardrobe). Use the staging configs first if you want a dry run.
3. Check `GET /v1/catalog` lists seven ids.
4. In the client, set `CapeCatalog.ANIMATED_ONLINE = true` so animated capes are published, then build a new client.

# Deployed beta service — 2026-10-17

- Worker: `vortex-cosmetics`
- Origin: https://vortex-cosmetics.forge93-m.workers.dev
- D1 database: `vortex-cosmetics`
- Binding: `DB`
- Database ID: `433866a6-82a1-4674-9522-3264cd04a0b2`
- Source: `worker.mjs`; schema: `schema.sql`
- Created through the owner's signed-in Cloudflare dashboard. No account API
  tokens, payment methods, paid plans, DNS or unrelated resources were changed.

Verified against the public deployment:

- `GET /health`: service Vortex Cosmetics, protocol 1, storage true.
- `GET /v1/catalog`: none, vortex, aurora, ember (reserved bundled texture IDs).
- `POST /v1/lookup` with a synthetic absent UUID: empty list, successful SQL query.

`node test.mjs` passes using an in-memory SQLite database: schema, single-use
activation, retry after a lost claim response, owner-only updates, UUID/body/batch
validation, catalogue-only selection, public-result privacy and rate bounds.
The first beta profile has been activated by its owner and a legacy custom cape
is visible through the public lookup API. Its keys were not read by the agent.
Actual two-player rendering remains unverified.

Client 0.1.26 / Launcher 0.1.29 add bundled 64x32 cape textures, opt-in UI,
invitation enrollment, bounded async lookup and an AbstractClientPlayer skin hook.
Fabric tests verify the actual hook, texture resources, cached skin replacement,
local secret storage and real HTTP bytes against an isolated fixture service.
Actual two-player live rendering is not yet verified. No credentials are stored
in this document or the repository.

To enroll a beta player, obtain Copy profile ID from their in-game CAPES screen
on the target server/world, then run `node issue-invite.mjs <uuid>` locally.
It writes a private code file and a hash-only SQL file under the administrator's
home `.vortex/cape-invites` directory (not in the repository). Execute the SQL in
this dedicated database console, ensure it inserted a row, then privately deliver
the code file. The helper never replaces or resets an existing owner's record.
The player enables capes, activates that code, selects a design and saves online.
Other opted-in Vortex clients refresh nearby player UUIDs about once per minute.

## Custom cape update — 2026-10-17

Client 0.1.28 / launcher 0.1.31 add a local PNG chooser, persistent local preview,
and explicit public upload. The existing D1 database now has `cape_textures` and
its hash index, created with `IF NOT EXISTS` without resetting existing profiles.
Worker version `17857d76` is active with owner-scoped raw RGBA upload and bounded
public downloads. No R2, account API tokens, billing or DNS changes were needed.

Verified public endpoints after deployment: health 200 with storage true; absent
texture returns the specific 404 `Texture not found` (the D1 query succeeded);
unauthenticated upload returns 401. Local SQLite tests cover image replacement,
cross-profile denial, bounded data, backward compatibility and raw byte downloads.
Java tests cover actual PNG decoding, RGBA order, dimension/size rejection,
atomic local preview storage, owner-authenticated upload, checksum/size rejection
on download, plus the existing skin hook and skin/elytra isolation tests.
No real player image was uploaded by the agent. Actual two-player GPU rendering
still requires an in-game check.

## File chooser correction — client 0.1.29 / launcher 0.1.32

Minecraft 26.3 Main's static initializer explicitly sets `java.awt.headless=true`.
The original AWT FileDialog could therefore throw before a file was selected;
the shared error handler incorrectly called this a connection failure. Replaced
AWT with SDL_ShowOpenFileDialog, attached to the actual game window, with one
reusable native callback/filter and main-thread dispatch of results. No launch
settings, credentials or deployed Worker changes are needed for this correction.
Tests under the real headless setting cover Unicode file-list decoding, error vs
cancel, one pending dialog, single completion, and local error classification.
Opening the OS dialog interactively still requires an in-game confirmation.

## Photo / HD update — client 0.1.30 / launcher 0.1.33

Worker version `55dc311b` is active with bounded HD envelope upload/download.
Applied `cape_hd_textures` and its hash index with IF NOT EXISTS; no profiles,
invitations or existing texture rows were reset or deleted. The former custom
cape on the first beta profile remains visible through the compatible lookup.
No billing, DNS, access permissions or account API tokens were changed.

Public checks: catalog advertises `hd:true`; absent v2 texture returns the specific
404 Texture not found (the HD table query succeeded); unauthenticated HD upload
returns 401; legacy custom lookup continues returning the existing custom ID.
SQLite tests cover owner isolation, valid HD upload/download, legacy fallback,
invalid dimensions, bounded exact decompression, truncation and trailing data.
Fabric tests cover PNG/JPEG decoding, fit/fill/zoom/pan, matching front/back UVs,
1024x512 persistence, >64KB actual HTTP transfer and checksums. The supplied cat
photo was also decoded and converted locally (69190 compressed bytes) without
publishing it or changing the original file. Crop geometry is tested down to
320x240. Source decoding uses headless ImageIO memory streams; GPU preview uses
a small texture updated only on crop changes rather than a full HD atlas/frame.
Interactive GPU crop rendering and two-player HD appearance remain unverified.

## Account staging preparation — 2026-10-17

Created the isolated Worker `vortex-accounts-staging` through the dashboard.
Initial active version: `82b8127d` (Cloudflare Hello World template only).
Public URL: https://vortex-accounts-staging.forge93-m.workers.dev/
No database bindings, account tables, real accounts, credentials, or connection
to the production cape DB were added. The cosmetics Worker was not changed.
No paid plan, billing, DNS or persistent API/OAuth credentials were configured.

Prepared `staging-probe.mjs` and `wrangler.staging.jsonc` locally. The probe uses
the same scrypt parameters with fixed synthetic inputs, accepts no password or
user data, and exposes `/health` GET and `/probe` POST. It passed in local Node;
this does NOT verify Workers runtime support or CPU-budget compatibility.

Dashboard code editing and file chooser activation could not be completed through
the available browser controls (Quick Edit nested editor). Consequently the probe
has NOT been deployed, the remote scrypt check has NOT run, and registration is
still NOT available. Handoff: upload `staging-probe.mjs` in Quick Edit Explorer,
set it as entrypoint, deploy to this staging Worker only, then verify `/health`
and POST `/probe`. Keep production accounts disabled until those checks and an
isolated account-DB end-to-end check pass. Do not treat Hello World as an auth API.

### Dashboard follow-up

The user uploaded `staging-probe.mjs`; its presence was verified in Quick Edit.
Both user-assisted and automated entrypoint/deploy attempts still left version
`82b8127d` active. Public GET `/health` and POST `/probe` both returned 200 with
the literal template response `Hello World!`, NOT probe JSON. The dashboard's
own developer log reported `Error: Entrypoint must not be empty`.

Tried a reversible draft-only filename workaround: original `worker.js` renamed
to `hello-template.js`, uploaded probe renamed to `worker.js`, followed by Set
Entrypoint and Deploy. This also failed with the same empty-entrypoint error.
Those draft files were left in the editor; no files were deleted. The original
probe is still available locally as `staging-probe.mjs`. No remote crypto test,
account DB/schema setup, account registration, or production cape change occurred.
Further deployment should use a working dashboard session or explicitly authorized
official CLI authentication; do not repeat the same failing entrypoint action.

### Official CLI authentication follow-up

Installed pinned Wrangler 4.151.1 in the separate user-local tooling directory
`%LOCALAPPDATA%/VortexLauncherTooling/cloudflare` (not included in launcher builds).
The Windows keyring dependency 1.3.0 is installed in Wrangler's native/keyring
directory. Metrics were disabled for commands. Staging dry-run bundles the probe
successfully with no DB bindings and account registration disabled.

The user completed OAuth; `whoami` confirmed the intended account and encrypted
credential storage with a key in Windows Credential Manager. No credential file
contents or keys were read. The initial reduced scope `workers:write` was NOT
sufficient: staging deployment was rejected with no access to the deployments
resource. No successful CLI deployment or remote scrypt test yet.

A replacement OAuth flow requesting `account:read user:read workers_scripts:write`
(plus Cloudflare's required background access) is waiting for the user's consent.
The consent page visibly names Workers Scripts Write. Do not request all default
Wrangler scopes; D1, routes, DNS, billing and other product write scopes are outside
this probe. OAuth URLs/codes and token material must not be copied into this file.

### Successful isolated probe deployment

The user completed the replacement OAuth grant. Official CLI deployment to
`vortex-accounts-staging` succeeded: probe version
`0af894f7-a1cf-40bf-8e9e-099cae8751dc`. Public `/health` returned the expected JSON
instead of Hello World; six POST `/probe` calls returned HTTP 200 with
`scrypt_supported:true` and `bytes:32`. GET `/probe` was rejected with 404.
No real passwords, accounts, profile IDs or private keys were used.

The reported in-request `elapsed_ms:0` is NOT a CPU benchmark. Runtime support is
verified; account-DB behavior, sustained limits/load and production readiness are
not. No database was created or bound and registration remains disabled.

After the bounded check, deployed `ac25cb84-f683-40fa-8718-656136efc60e` with
`PROBE_ENABLED=false`, `VORTEX_ACCOUNTS_ENABLED=false`, and preview URLs disabled.
The public health endpoint remains available, while the costly POST probe is gated
off to avoid leaving a public hashing endpoint running unnecessarily. Local tests
verify both flag states. The production cape service was not deployed or modified.

## Private account staging — 2026-10-18

The user authorized the D1 scope in Cloudflare. Created ONLY the dedicated new
`vortex-accounts-staging` D1 database (`3d65d6bf-4f48-4208-845d-da2e63ebbba9`, EEUR).
Applied `schema.accounts.sql`: three account tables, their indexes, no legacy cape
tables. The production database `433866a6-82a1-4674-9522-3264cd04a0b2` was not queried
for data, migrated or modified.

Private account staging version `930d98ad-bde2-4ee9-83a1-97ea3642799a` was deployed
through official Wrangler with `wrangler.accounts-staging.jsonc`. Every account
route requires a separate random staging key, held in a Cloudflare secret; only
health is public. Preview URLs are disabled. The live test generated its key and
fixture passwords in memory, never printed/saved them, and passed the staging key
only through stdin to `wrangler secret put`, not as a command argument.

Both the actual local workerd + D1 test and real Cloudflare HTTPS + D1 test passed:
private gate, register, case-insensitive uniqueness, login, wrong-password denial,
stable ID across nickname change, logout, password rotation and session revocation,
old-password denial, request bounds. All fixture sessions were revoked and the
exact synthetic account was removed with an ID+login-constrained DELETE from the
dedicated staging database. The now-absent test account is disposable, not a user
profile; no real user account, local player profile, world or cape was removed.

Generated `accounts-env.d.ts` from Wrangler config. Node/SQLite security tests and
existing cosmetics regression tests also pass. The service remains private staging;
production registration, launcher endpoint rollout and the cosmetics-account bridge
are not activated. No paid plan, DNS change or production cape deployment occurred.
