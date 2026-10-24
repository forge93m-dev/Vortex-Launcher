import {spawnSync} from "node:child_process";
import {readFileSync} from "node:fs";
import {fileURLToPath} from "node:url";
import {randomBytes} from "node:crypto";
import assert from "node:assert/strict";
import {checkAccountHttp} from "./accounts-http-checks.mjs";

// Explicit opt-in. Deploys ONLY the isolated staging Worker, never the cape service.
if(!process.argv.includes("--deploy-staging"))throw new Error("Explicit --deploy-staging is required.");
const toolPackage=process.env.VORTEX_CLOUDFLARE_TOOLS;
if(!toolPackage)throw new Error("Set VORTEX_CLOUDFLARE_TOOLS to the isolated tooling package.json path.");
const cli=fileURLToPath(new URL("node_modules/wrangler/bin/wrangler.js","file:///"+toolPackage.replaceAll("\\","/")));
const config=fileURLToPath(new URL("wrangler.accounts-staging.jsonc",import.meta.url));
const settings=JSON.parse(readFileSync(config,"utf8"));
assert.equal(settings.name,"vortex-accounts-staging");
assert.equal(settings.account_id,"77c6bcb1c4b618a04ffee7da8c4f3de7");
assert.equal(settings.main,"accounts-worker.mjs");
assert.equal(settings.d1_databases.length,1);
assert.equal(settings.d1_databases[0].database_name,"vortex-accounts-staging");
assert.equal(settings.d1_databases[0].database_id,"3d65d6bf-4f48-4208-845d-da2e63ebbba9");
const key=randomBytes(32).toString("hex");
function run(args,input){
  const result=spawnSync(process.execPath,[cli,...args,"--config",config],{
    env:{...process.env,WRANGLER_SEND_METRICS:"false",CLOUDFLARE_ACCOUNT_ID:settings.account_id},
    input,encoding:"utf8",timeout:120000,maxBuffer:1024*1024,windowsHide:true});
  // The staging key is sent only on stdin and is never printed, saved or put in arguments.
  const safe=(result.stdout+result.stderr).replaceAll(key,"[redacted]");
  if(result.status!==0)throw new Error("Staging CLI failed: "+safe.slice(-2000));
  return safe;
}
run(["secret","put","STAGING_KEY"],key+"\n");console.log("STAGING_KEY_STORED: protected API gate configured (value not displayed)");
const deploy=run(["deploy"]);
console.log(deploy.split("\n").filter(line=>line.includes("Current Version ID:")||line.includes("Deployed vortex-accounts-staging")).join("\n"));
const origin="https://vortex-accounts-staging.forge93-m.workers.dev";
const fixture=await checkAccountHttp((path,init)=>fetch(origin+path,{...init,signal:AbortSignal.timeout(20000)}),key);
// Only remove the exact disposable identity created by this run. No other rows are targets.
assert.ok(/^[a-f0-9-]{36}$/.test(fixture.id) && /^qa_[a-f0-9]{10}$/.test(fixture.login));
const cleanup=run(["d1","execute","vortex-accounts-staging","--remote","--json","--command",
  `DELETE FROM vortex_accounts WHERE id='${fixture.id}' AND login='${fixture.login}'; SELECT COUNT(*) AS sessions FROM vortex_sessions WHERE account_id='${fixture.id}'; SELECT COUNT(*) AS accounts FROM vortex_accounts WHERE id='${fixture.id}';`]);
const results=JSON.parse(cleanup);
assert.equal(results[0].meta.changes,1,"exact disposable account removed");
assert.equal(results[1].results[0].sessions,0);assert.equal(results[2].results[0].accounts,0);
console.log("ACCOUNTS_LIVE_OK: Cloudflare HTTPS + dedicated D1; disposable account removed; sessions revoked; public registration remains blocked");
