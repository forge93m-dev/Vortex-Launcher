import {spawnSync} from "node:child_process";
import {readFileSync} from "node:fs";
import {fileURLToPath} from "node:url";
import {randomBytes,randomUUID,createHash} from "node:crypto";
import assert from "node:assert/strict";
import {offlineIds} from "./wardrobe.mjs";

// Seed an exact short-lived synthetic identity directly: never use shared login/register IP quotas.
if(!process.argv.includes("--live-synthetic"))throw new Error("Explicit --live-synthetic required");
const config=fileURLToPath(new URL("wrangler.accounts.jsonc",import.meta.url));const settings=JSON.parse(readFileSync(config,"utf8"));
assert.equal(settings.d1_databases[0].database_id,"12549530-ca39-48d5-a345-be88c196276f");assert.equal(settings.name,"vortex-accounts");
const tooling=process.env.VORTEX_CLOUDFLARE_TOOLS;assert.ok(tooling);
const cli=fileURLToPath(new URL("node_modules/wrangler/bin/wrangler.js","file:///"+tooling.replaceAll("\\","/")));
const hash=v=>createHash("sha256").update(v).digest("hex");
function sql(command){const result=spawnSync(process.execPath,[cli,"d1","execute","vortex-accounts","--remote","--json","--config",config,"--command",command],{windowsHide:true,encoding:"utf8",timeout:60000,env:{...process.env,WRANGLER_SEND_METRICS:"false",CLOUDFLARE_ACCOUNT_ID:settings.account_id}});assert.equal(result.status,0,"synthetic fixture SQL succeeded");return JSON.parse(result.stdout);}
const suffix=randomBytes(5).toString("hex"),login="qa_wg_"+suffix,nickname="QW_"+suffix,id=randomUUID(),session=randomBytes(32).toString("hex"),parent=hash(session);
const uuid=offlineIds(nickname)[1],now=Date.now();
sql(`INSERT INTO vortex_accounts(id,login,nickname,nickname_key,password_salt,password_hash,created) VALUES('${id}','${login}','${nickname}','${nickname.toLowerCase()}','${randomBytes(16).toString("hex")}','${randomBytes(32).toString("hex")}',${now}); INSERT INTO vortex_sessions(token_hash,account_id,created,expires) VALUES('${parent}','${id}',${now},${now+600000});`);
async function call(path,method="GET",value,token){const response=await fetch("https://vortex-accounts.forge93-m.workers.dev"+path,{method,headers:{"Content-Type":"application/json",...(token?{Authorization:"Bearer "+token}:{})},body:value===undefined?undefined:JSON.stringify(value),signal:AbortSignal.timeout(20000)});return {status:response.status,data:response.headers.get("Content-Type")?.includes("json")?await response.json():Buffer.from(await response.arrayBuffer())};}
try{
  const access=await call("/v3/accounts/wardrobe-session","POST",{uuid},session);assert.equal(access.status,200);const token=access.data.token;assert.ok(typeof token==="string" && /^[a-f0-9]{64}$/.test(token));
  assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"ember"},token)).status,200);
  const pixels=Buffer.alloc(16384,255),skin=hash(pixels);
  assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:true,slim:false,pixels:pixels.toString("base64")},token)).status,200);
  const found=await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]});assert.equal(found.data.profiles[0].cape,"ember");assert.equal(found.data.profiles[0].skin_hash,skin);
  assert.deepEqual((await call("/v3/wardrobe/textures/"+skin)).data,pixels);
  sql(`DELETE FROM vortex_sessions WHERE token_hash='${parent}' AND account_id='${id}';`);
  assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"none"},token)).status,401);
  console.log("WARDROBE_LIVE_OK: Cloudflare HTTPS + D1, scoped game capability, skin/cape sharing and revocation; no login/register quota used");
}finally{
  const removed=sql(`DELETE FROM vortex_accounts WHERE id='${id}' AND login='${login}'; SELECT COUNT(*) AS n FROM vortex_wardrobe WHERE account_id='${id}'; SELECT COUNT(*) AS n FROM vortex_wardrobe_sessions WHERE account_id='${id}';`);
  // D1 includes cascading cosmetic/session removals in meta.changes.
  assert.ok(removed[0].meta.changes>=1);assert.equal(removed[1].results[0].n,0);assert.equal(removed[2].results[0].n,0);
  assert.equal(sql(`SELECT COUNT(*) AS n FROM vortex_accounts WHERE id='${id}' AND login='${login}';`)[0].results[0].n,0);
  console.log("WARDROBE_FIXTURE_REMOVED: exact synthetic account, cosmetics and credentials deleted; real user data unchanged");
}
