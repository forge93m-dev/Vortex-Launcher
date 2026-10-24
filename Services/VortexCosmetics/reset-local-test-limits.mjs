import {createHash} from "node:crypto";
import {spawnSync} from "node:child_process";
import {fileURLToPath} from "node:url";
import {readFileSync} from "node:fs";
import assert from "node:assert/strict";
import {isIP} from "node:net";

// Manual support action only: reset the three IP gates for this machine's connection.
// No account/name gates, passwords, sessions or other connections are targets.
if(!process.argv.includes("--confirm-local-reset"))throw new Error("Explicit local reset confirmation required");
const config=fileURLToPath(new URL("wrangler.accounts.jsonc",import.meta.url));
const settings=JSON.parse(readFileSync(config,"utf8"));
assert.equal(settings.name,"vortex-accounts");
assert.equal(settings.d1_databases[0].database_id,"12549530-ca39-48d5-a345-be88c196276f");
const response=await fetch("https://www.cloudflare.com/cdn-cgi/trace",{signal:AbortSignal.timeout(15000)});
assert.equal(response.status,200);
const ip=(await response.text()).split("\n").find(line=>line.startsWith("ip="))?.slice(3).trim();
assert.ok(ip && isIP(ip),"valid current connection address");
const keys=["all:","login-ip:","register:"].map(prefix=>createHash("sha256").update(prefix+ip).digest("hex"));
const toolPackage=process.env.VORTEX_CLOUDFLARE_TOOLS;
assert.ok(toolPackage);
const cli=fileURLToPath(new URL("node_modules/wrangler/bin/wrangler.js","file:///"+toolPackage.replaceAll("\\","/")));
function run(sql){
  const result=spawnSync(process.execPath,[cli,"d1","execute","vortex-accounts","--remote","--json","--config",config,"--command",sql],{windowsHide:true,encoding:"utf8",timeout:60000,env:{...process.env,WRANGLER_SEND_METRICS:"false",CLOUDFLARE_ACCOUNT_ID:settings.account_id}});
  assert.equal(result.status,0,"support query succeeded");return JSON.parse(result.stdout);
}
const targets=keys.map(key=>"'"+key+"'").join(",");
const before=run(`SELECT COUNT(*) AS n FROM vortex_auth_limits WHERE key IN (${targets});`)[0].results[0].n;
assert.ok(before>0 && before<=3,"only existing current-connection gates");
const removed=run(`DELETE FROM vortex_auth_limits WHERE key IN (${targets});`)[0].meta.changes;
assert.equal(removed,before);
assert.equal(run(`SELECT COUNT(*) AS n FROM vortex_auth_limits WHERE key IN (${targets});`)[0].results[0].n,0);
console.log(`LOCAL_CONNECTION_RESET_OK: ${removed} temporary IP counters removed; account data and other connection gates unchanged`);
