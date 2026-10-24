# Vortex accounts: public beta

## Client 0.1.46 / launcher 0.1.51: main-menu sync

Auto-sync no longer requires a loaded world or player. Both queue dispatch and
cape publication use the launch User UUID in the main menu, switching to the player
UUID in-world. The server-side account/profile ownership checks are unchanged.
A headless Minecraft fixture with null level/player verifies actual tick dispatch
for both cosmetics exactly once; no network, real credentials or game window.

## Client 0.1.45 / launcher 0.1.50: automatic Apply sync

Wardrobe Apply now queues online skin/cape publication for a signed-in account
after the local save succeeds. Preview, import and Cancel do not publish. Confirmed
applies coalesce per cosmetic, wait for image/network work, and continue after the
wardrobe closes. Changed selections/profiles cancel stale queued work. Failures
are not retried in a loop; Retry sync remains available. Vanilla Apply publishes
removal too. No account credentials, server schema or existing cosmetics changed.
Legacy/manual cape UI remains available for users without account-based access.
The historical Save online instructions below describe versions before 0.1.45.

Status (2026-10-18): public beta registration is deployed after explicit user
approval, isolated runtime tests and real HTTPS + D1 checks. Launcher 0.1.48 connects
to this dedicated service, not private staging. These checks are not a security
audit or a load/CPU-budget benchmark. No paid plan was enabled.
Launcher 0.1.48 / Client 0.1.43 add account-owned online skins and capes. Sign-in
does not publish a selection automatically: use Apply, then Save online in Wardrobe.
The legacy cape service and ownership records are preserved.

## Account wardrobe (0.1.48 / client 0.1.43)

- Profiles > Vortex account > Use nickname for Minecraft, then close Minecraft,
  update the instance using Update Vortex Client and launch from this signed-in launcher.
  Join a world/server, open Wardrobe, choose Skin/Capes, Apply, then Save online.
- No invitation code is required for a new account nickname. Existing legacy cape
  UUIDs require proof from their already saved owner key, supplied automatically
  by the client. Neither a login nor a matching nickname can take a legacy profile.
  Arbitrary local names and Microsoft UUIDs are not automatically account-owned.
- POST /v3/accounts/wardrobe-session takes the launch UUID under launcher bearer
  authentication. Returns a separate random 256-bit capability with an eight-hour
  maximum lifetime, scoped to cosmetics for that launch profile. Only its hash is
  stored. The launcher passes it through the child process environment, never
  game arguments, java_args.txt, exports or logs. The full account session and
  password never enter Minecraft. Relaunch after expiry; logout/password rotation
  revokes dependent capabilities through parent session checks/cascading deletes.
- Canonical Java and historical .NET offline UUIDs are derived server-side from
  the account nickname. No existing Minecraft UUID generation algorithm was changed.
  Account nickname ownership remains separate from Minecraft/server authentication.
- New tables in ACCOUNT DB: vortex_wardrobe_sessions, vortex_wardrobe. The added
  LEGACY_DB binding is used only for SELECT owner proof and cape inheritance.
  No schema, credentials, profiles or textures in the legacy cape DB were mutated.
  First online skin save inherits a proven legacy cape instead of hiding it.
- PUT /v3/wardrobe/cape or /skin requires the scoped capability; updates preserve
  the other cosmetic. Public bounded lookup (32 UUIDs) and checksum-addressed pixel
  downloads allow other updated clients to refresh in about a minute. Legacy clients
  do not query account cosmetics. Cosmetics are visible only to compatible Vortex clients.
- Classic/slim 64x64 skins, built-in capes and bounded custom/HD cape envelopes
  are supported. No arbitrary remote image URLs. Explicit local preview/apply is
  separate from publication. Network failure does not remove local selections.
- Node fixture tests and actual workerd+D1 HTTP tests verify scoped capabilities,
  offline UUID aliases, skin/cape bytes, legacy proof, limits, logout revocation.
  Fabric headless transformation/UI regressions pass. Real Cloudflare HTTPS+D1
  was verified using a directly seeded, short-lived synthetic account, avoiding
  shared login/register quotas. That exact synthetic account and its cosmetics
  were removed. No two-computer multiplayer/GPU visual test was performed.
- Run wardrobe-test.mjs and accounts-runtime-test.mjs --public locally. The explicit
  wardrobe-live-test.mjs --live-synthetic validates the exact account DB before
  seeding and deleting its temporary fixture. Never read real stored credentials.

## Public beta deployment and verification

- Worker `vortex-accounts`, entrypoint `accounts-public-worker.mjs`, config
  `wrangler.accounts.jsonc`; URL `https://vortex-accounts.forge93-m.workers.dev`.
- Dedicated account-only D1 `vortex-accounts`, ID
  `12549530-ca39-48d5-a345-be88c196276f`. Legacy cape DB is a read-only-use binding.
- Deployed version `9167df62-8652-45a3-a8d9-81d0caf5439b` (account wardrobe).
- Real HTTPS registration, uniqueness, wrong-password denial, stable ID on rename,
  password rotation, logout, request bounds passed. The actual .NET launcher API
  and view model passed sign-in, protected session persistence, new-launcher
  restoration with server verification, logout revocation and wrong-password checks.
  Both exact synthetic accounts were removed, with no remaining fixture sessions.
- Windows: Profiles > Vortex account > Sign in / Create account. Existing Minecraft
  local profiles are not replaced by sign-in. Remembered sessions use per-user DPAPI.
- Recovery is not implemented; registration warns users before submitting. Save a
  unique password safely. macOS/Linux currently use memory-only sign-in.
- Public account service does NOT use or require the private staging key.
- Launcher 0.1.47 displays the server's bounded retry interval in minutes/seconds.
  HTTP 429 includes matching Retry-After/retry_after values. Blocked retries never
  extend the fixed cooldown. Invalid registration fields no longer consume the
  hourly registration quota (the inexpensive request throttle still applies).
- After test traffic exhausted this machine's shared IP quota, the user authorized
  a one-time reset: exactly three current-connection hashed IP counters removed.
  Account/name limits and other connections were not reset. Subsequent verification
  was local only, so it did not consume the user's newly restored quota.
- Run `accounts-runtime-test.mjs --public` for isolated workerd checks; the explicit
  `accounts-public-test.mjs --live` performs public synthetic checks and deletes
  only its exact temporary identities. Do not run repeatedly: durable limits apply.

## Verified account staging (2026-10-18)

- Worker: `vortex-accounts-staging`; dedicated account-only D1 database:
  `vortex-accounts-staging` (`3d65d6bf-4f48-4208-845d-da2e63ebbba9`).
- Active tested version: `930d98ad-bde2-4ee9-83a1-97ea3642799a`.
- Entrypoint `accounts-worker.mjs` imports only the account module, not cape code.
  All account endpoints, including capabilities, require `X-Vortex-Staging-Key`.
  Missing/invalid key returns 401; missing server secret fails closed with 503.
  Public health explicitly reports `public_registration:false`.
- Registration, case-insensitive uniqueness, wrong-password denial, authenticated
  login, stable ID across rename, logout/revocation, password rotation, old-password
  denial and bounded requests passed against real D1. Fixture sessions were revoked
  and the exact disposable account was removed. No real user's password was used.
- The compatibility date is pinned to 2026-10-12, supported by the installed
  workerd build; local and remote tests use the same date plus explicit nodejs_compat.
  Types are generated from config, and structured error logging excludes bodies,
  passwords, session tokens and the staging key.
- This staging service remains private and separate from the public beta service;
  never put the staging key into a client build.

Verification: set `VORTEX_CLOUDFLARE_TOOLS` to the separate tooling package.json;
run `node Services/VortexCosmetics/accounts-runtime-test.mjs` for isolated workerd
+ D1 tests. The opt-in `accounts-live-test.mjs --deploy-staging` validates exact
Worker/account/DB targets, generates a fresh gate key in memory, sends it to the
official secret command via stdin, deploys, tests and removes its exact synthetic
account. It must only be run intentionally with the staging deployment authorized.
No secret values are saved in files or command arguments. Re-running rotates this
test-only gate and needs a fresh non-throttled test window if many runs were made.

The actual Cloudflare probe described below was an earlier runtime-support check;
the account-only staging entrypoint now replaces it. `/probe` is not exposed.

Staging runtime update: the isolated scrypt probe was deployed through official
Wrangler and returned HTTP 200 / 32-byte success on six actual Cloudflare requests.
This confirms runtime support only; it is not an account-DB end-to-end test, load
test, security audit, or CPU-duration benchmark. The observed in-request clock
delta was zero, so no CPU timing conclusion was drawn. The public probe has been
disabled after checking; `/health` remains public with accounts disabled. Re-enable
`PROBE_ENABLED` only for an intentional bounded runtime check, then disable again.

## Namespace and privacy

Each account has a random permanent UUID, unique case-insensitive login, and unique
case-insensitive nickname. Renaming the nickname does not change the account ID.
This is a Vortex identity, NOT proof of ownership of any Microsoft account,
Minecraft UUID, or offline-server name. Routes never read or modify legacy cape
ownership. A future cosmetics bridge must explicitly verify a legacy owner key or
an appropriate server/Microsoft identity proof; matching nicknames is insufficient.
Never send launcher bearer tokens to Minecraft servers or in game command lines.

## API

Prefix `/v3/accounts/`: GET capabilities, POST register/login, GET me,
PUT nickname, POST logout, PUT password (requires current password).
Passwords are 15+ Unicode code points, at most 128 UTF-16 code units / 512 UTF-8
bytes. Login is ASCII letters/digits/underscore (3–24); nickname is 3–16.
No password recovery, email verification, account deletion UI or MFA in this beta.

Passwords use native scrypt N=16384, r=8, p=5, random 16-byte salt, 32-byte output.
Only salted hashes are stored. Sessions expire after 30 days; up to eight sessions
per account, revocable on logout and all revoked on password rotation. Only
SHA-256 hashes of random 256-bit bearer tokens are stored server-side.
JSON request/response bounds, durable per-IP/per-login throttles, fixed safe error
codes and constant-time password comparisons are included. Expired throttle rows
are pruned in bounded indexed batches. These controls are not a security audit.

Launcher passwords are masked and cleared before network work, never saved.
On Windows, session/profile data is protected by per-user DPAPI in
`%USERPROFILE%/.vortex/launcher-session.dpapi`, outside instance/export folders.
Unreadable encrypted files are preserved. Session diagnostics redact the token.
On macOS/Linux, sign-in is memory-only until native protected storage is added.
The server session is checked before restored sign-in is shown as authenticated.

## Deployment gate

1. Review the account code and test runtime support for `node:crypto` scrypt with
   the pinned compatibility date. Local Node tests do NOT prove Workers support
   or CPU-budget compatibility. Workers Free has a 10 ms CPU budget; do NOT lower
   password hashing strength to fit it and do NOT enable a paid plan implicitly.
2. Benchmark native scrypt in an isolated Worker staging environment, with no real
   passwords, and verify throttles, memory use, failures and runtime CPU limits.
   If the free budget is insufficient, choose an appropriate auth service/runtime
   with the user. Never enable registration with an unverified hashing runtime.
3. Apply `schema.accounts.sql` to the dedicated account-only DB. Never use the cape DB. Deploy
   both modules with a module-aware bundler; existing dashboard single-file upload
   is insufficient. Enable only after explicit confirmation of public exposure.
4. Smoke-test actual HTTPS register/login/rename/logout using disposable synthetic
   accounts before announcing availability. Add recovery before a general release.
5. Preserve explicit proof checks in the wardrobe bridge. Never use a Vortex
   nickname alone to overwrite a legacy Minecraft UUID's cosmetics.

References: [OWASP password storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html),
[Workers Node crypto](https://developers.cloudflare.com/workers/runtime-apis/nodejs/crypto/),
[Workers limits](https://developers.cloudflare.com/workers/platform/limits/).

## Fixture verification

- `node Services/VortexCosmetics/accounts-test.mjs` (in-memory SQLite; real scrypt).
- `node Services/VortexCosmetics/test.mjs` (existing cosmetics regression tests).
- `dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj -p:UseSharedCompilation=false -- --vortex-accounts`
  (injected HTTP fixture; disposable DPAPI file; headless UI captures).

Default fixture tests do not read real saved credentials, contact production or
claim an existing Minecraft profile. Only explicitly requested `--live` tests contact
the public account service with synthetic identities. Headless rendering is not a
live Minecraft visual test.
