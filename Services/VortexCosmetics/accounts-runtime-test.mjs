import {createRequire} from "node:module";
import {readFileSync} from "node:fs";
import {fileURLToPath} from "node:url";
import {randomBytes} from "node:crypto";
import assert from "node:assert/strict";
import {checkAccountHttp} from "./accounts-http-checks.mjs";
import {checkWardrobeHttp} from "./wardrobe-http-checks.mjs";

// Resolve the separately installed official Workers toolchain, not launcher dependencies.
const toolPackage=process.env.VORTEX_CLOUDFLARE_TOOLS;
if(!toolPackage)throw new Error("Set VORTEX_CLOUDFLARE_TOOLS to the isolated tooling package.json path.");
const {Miniflare}=createRequire(toolPackage)("miniflare");
const key=randomBytes(32).toString("hex");
const publicService=process.argv.includes("--public");
const entry=publicService?"accounts-public-worker.mjs":"accounts-worker.mjs";
const modules=Object.fromEntries([entry,"accounts.mjs",...(publicService?["wardrobe.mjs"]:[])].map(name=>[name,{type:"esm",contents:readFileSync(new URL(name,import.meta.url),"utf8")}]));
const mf=new Miniflare({workers:[{config:{name:"accounts-fixture",
  compatibilityDate:"2026-10-12",compatibilityFlags:["nodejs_compat"],
  manifest:{mainModule:entry,modulesRoot:fileURLToPath(new URL(".",import.meta.url)),modules},
  env:{DB:{type:"d1",id:"vortex-accounts-fixture",dev:{remote:false}},LEGACY_DB:{type:"d1",id:"legacy-fixture",dev:{remote:false}},STAGING_KEY:{type:"json",value:key},VORTEX_ACCOUNTS_ENABLED:{type:"json",value:"true"}}}}]});
try{
  const db=await mf.getD1Database("DB");
  // D1 exec expects one complete statement per line; no user data in this schema.
  const schema=readFileSync(new URL("schema.accounts.sql",import.meta.url),"utf8").replace(/^--.*$/gm,"");
  await db.batch(schema.split(";").map(s=>s.trim()).filter(Boolean).map(s=>db.prepare(s)));
  if(publicService){
    const wardrobe=readFileSync(new URL("schema.wardrobe.sql",import.meta.url),"utf8").replace(/^--.*$/gm,"");
    await db.batch(wardrobe.split(";").map(s=>s.trim()).filter(Boolean).map(s=>db.prepare(s)));
    await (await mf.getD1Database("LEGACY_DB")).prepare("CREATE TABLE cape_profiles(uuid TEXT PRIMARY KEY,owner_hash TEXT,cape TEXT NOT NULL DEFAULT 'none')").run();
  }
  await checkAccountHttp((path,init)=>mf.dispatchFetch("https://fixture.test"+path,init),publicService?undefined:key,{publicService});
  if(publicService)await checkWardrobeHttp((path,init)=>mf.dispatchFetch("https://fixture.test"+path,init));
  const tables=await db.prepare("SELECT name FROM sqlite_master WHERE type='table'").all();
  assert.ok(!tables.results.some(row=>row.name.startsWith("cape_")),"no legacy cape tables");
  assert.equal((await db.prepare("SELECT COUNT(*) AS n FROM vortex_sessions").first()).n,0,"all fixture sessions revoked");
  const account=await db.prepare("SELECT password_hash,password_salt FROM vortex_accounts").first();
  assert.match(account.password_hash,/^[a-f0-9]{64}$/);assert.match(account.password_salt,/^[a-f0-9]{32}$/);
  console.log("ACCOUNTS_WORKER_RUNTIME_OK: actual workerd + isolated D1, hash-only storage, no cape tables, no remaining sessions");
}finally{await mf.dispose();}
