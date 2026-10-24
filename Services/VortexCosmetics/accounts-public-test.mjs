import {spawnSync} from "node:child_process";
import {readFileSync} from "node:fs";
import {fileURLToPath} from "node:url";
import assert from "node:assert/strict";
import {checkAccountHttp} from "./accounts-http-checks.mjs";

if(!process.argv.includes("--live"))throw new Error("Explicit --live required");
const toolPackage=process.env.VORTEX_CLOUDFLARE_TOOLS;
if(!toolPackage)throw new Error("Missing isolated Cloudflare toolchain path");
const cli=fileURLToPath(new URL("node_modules/wrangler/bin/wrangler.js","file:///"+toolPackage.replaceAll("\\","/")));
const config=fileURLToPath(new URL("wrangler.accounts.jsonc",import.meta.url));
const settings=JSON.parse(readFileSync(config,"utf8"));
assert.equal(settings.name,"vortex-accounts");
assert.equal(settings.d1_databases[0].database_id,"12549530-ca39-48d5-a345-be88c196276f");
function cleanup(fixture){
  assert.ok(/^[a-f0-9-]{36}$/.test(fixture.id) && /^qa_[a-f0-9]{10}$/.test(fixture.login));
  const sql=`DELETE FROM vortex_accounts WHERE id='${fixture.id}' AND login='${fixture.login}'; SELECT COUNT(*) AS n FROM vortex_sessions WHERE account_id='${fixture.id}';`;
  const result=spawnSync(process.execPath,[cli,"d1","execute","vortex-accounts","--remote","--json","--command",sql,"--config",config],{encoding:"utf8",timeout:60000,windowsHide:true,env:{...process.env,WRANGLER_SEND_METRICS:"false",CLOUDFLARE_ACCOUNT_ID:settings.account_id}});
  assert.equal(result.status,0,"exact synthetic cleanup succeeded");const rows=JSON.parse(result.stdout);
  assert.equal(rows[0].meta.changes,1);assert.equal(rows[1].results[0].n,0);
}
const origin="https://vortex-accounts.forge93-m.workers.dev";
const fixture=await checkAccountHttp((path,init)=>fetch(origin+path,{...init,signal:AbortSignal.timeout(20000)}),undefined,{publicService:true});
cleanup(fixture);
const launcher=spawnSync("dotnet",["run","--project","Tests/SidebarSmoke/SidebarSmoke.csproj","-p:UseSharedCompilation=false","--","--vortex-accounts-live"],{encoding:"utf8",timeout:120000,windowsHide:true,maxBuffer:1024*1024});
// Output contains assertions and synthetic cleanup coordinates only, never credentials.
const coordinates=launcher.stdout?.split(/\r?\n/).find(line=>line.startsWith("ACCOUNT_FIXTURE:"));
try{console.log(launcher.stdout?.split(/\r?\n/).filter(line=>!line.startsWith("ACCOUNT_FIXTURE:")).join("\n"));assert.equal(launcher.status,0,"live .NET account checks");}
finally{if(coordinates)cleanup(JSON.parse(coordinates.slice("ACCOUNT_FIXTURE:".length)));}
console.log("ACCOUNTS_PUBLIC_LIVE_OK: HTTPS, D1, launcher login/restore/logout; exact synthetic accounts removed");
