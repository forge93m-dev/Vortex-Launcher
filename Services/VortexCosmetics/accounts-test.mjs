import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {createHash} from "node:crypto";
import {handleAccounts,derivePassword} from "./accounts.mjs";

// Disposable in-memory database only. Never contacts the deployed service.
const sql=new DatabaseSync(":memory:");
sql.exec(readFileSync(new URL("schema.sql",import.meta.url),"utf8"));
const DB={prepare(query){return {bind(...args){return {
  async all(){return {results:sql.prepare(query).all(...args)};},
  async run(){return {meta:{changes:Number(sql.prepare(query).run(...args).changes)}};}
};}};},async batch(statements){
  sql.exec("BEGIN");try{const result=[];for(const statement of statements)result.push(await statement.run());sql.exec("COMMIT");return result;}
  catch(error){sql.exec("ROLLBACK");throw error;}
}};
const env={DB,VORTEX_ACCOUNTS_ENABLED:"true"};
const password="Fixture password only 481!",nextPassword="Replacement fixture only 762!";
async function call(path,method="GET",value,token,ip="fixture-main",settings=env){
  const headers={"CF-Connecting-IP":ip};if(value!==undefined)headers["Content-Type"]="application/json";
  if(token)headers.Authorization="Bearer "+token;
  const response=await handleAccounts(new Request("https://fixture.test/v3/accounts/"+path,{method,headers,body:value===undefined?undefined:JSON.stringify(value)}),settings);
  assert.ok(response);assert.equal(response.headers.get("Cache-Control"),"no-store");
  const retry=response.headers.get("Retry-After");
  const valueResult=await response.json();
  if(response.status===429){assert.equal(Number(retry),valueResult.retry_after);assert.ok(valueResult.retry_after>0);}
  return {status:response.status,value:valueResult};
}
assert.equal(await handleAccounts(new Request("https://fixture.test/v1/catalog"),env),null);
assert.equal((await call("capabilities","GET",undefined,null,"gate",{DB})).value.enabled,false);
assert.equal((await call("register","POST",{login:"Owner",nickname:"Owner",password},null,"gate",{DB})).status,503);
assert.equal((await call("register","POST",{login:"Owner",nickname:"Owner",password:"short"})).status,400);
assert.equal((await call("register","POST",{login:"bad login",nickname:"Owner",password})).status,400);
assert.equal((await call("register","POST",{login:"Owner\n",nickname:"Owner",password})).status,400);
assert.equal((await call("register","POST",{login:"Owner",nickname:"Owner\n",password},null,"newline-fixture")).status,400);
assert.equal((await call("register","POST",{login:"Emoji",nickname:"Emoji",password:"😀".repeat(8)})).status,400);
sql.prepare("INSERT INTO cape_profiles(uuid,owner_hash,cape) VALUES(?,?,?)").run("legacy-fixture","legacy-owner","ember");
const legacy=sql.prepare("SELECT * FROM cape_profiles").all();
const created=await call("register","POST",{login:"OWNER",nickname:"Owner",password,uuid:"legacy-fixture"});
assert.equal(created.status,201);const identity=created.value.profile,token=created.value.token;
assert.equal(identity.login,"owner");assert.match(identity.id,/^[a-f0-9-]{36}$/);assert.match(token,/^[a-f0-9]{64}$/);
const row=sql.prepare("SELECT * FROM vortex_accounts").get();
assert.notEqual(row.password_hash,password);assert.equal(row.password_hash.length,64);
assert.equal(Buffer.from(await derivePassword(password,row.password_salt)).toString("hex"),row.password_hash);
const stored=sql.prepare("SELECT token_hash FROM vortex_sessions").get().token_hash;
assert.notEqual(stored,token);assert.equal(stored,createHash("sha256").update(token).digest("hex"));
assert.equal((await call("register","POST",{login:"other",nickname:"oWnEr",password},null,"fixture-duplicate")).status,409);
assert.equal((await call("login","POST",{login:"owner",password:"wrong fixture only"})).status,401);
assert.deepEqual((await call("login","POST",{login:"unknown",password:"wrong fixture only"})).value,{error:"invalid_credentials"});
const signedIn=await call("login","POST",{login:"Owner",password});assert.equal(signedIn.status,200);
assert.equal(signedIn.value.profile.id,identity.id);
assert.equal((await call("me","GET")).status,401);
assert.equal((await call("me","GET",undefined,"a".repeat(64))).status,401);
assert.deepEqual((await call("me","GET",undefined,token)).value.profile,identity);
assert.equal((await call("nickname","PUT",{nickname:"invalid nick"},token)).status,400);
const renamed=await call("nickname","PUT",{nickname:"Renamed"},token);
assert.equal(renamed.status,200);assert.equal(renamed.value.profile.id,identity.id);assert.equal(renamed.value.profile.login,"owner");
assert.equal((await call("me","GET",undefined,signedIn.value.token)).value.profile.nickname,"Renamed");
assert.equal((await call("logout","POST",{},token)).status,200);
assert.equal((await call("me","GET",undefined,token)).status,401);
assert.equal((await call("password","PUT",{current_password:"wrong",password:nextPassword},signedIn.value.token)).status,401);
const changed=await call("password","PUT",{current_password:password,password:nextPassword},signedIn.value.token);
assert.equal(changed.status,200);assert.equal(changed.value.profile.id,identity.id);
assert.equal((await call("me","GET",undefined,signedIn.value.token)).status,401);
assert.equal((await call("login","POST",{login:"owner",password})).status,401);
assert.equal((await call("login","POST",{login:"owner",password:nextPassword})).status,200);
for(let i=0;i<10;i++)assert.equal((await call("login","POST",{login:"owner",password:nextPassword},null,"session-fixture")).status,200);
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM vortex_sessions WHERE account_id=?").get(identity.id).n,8);
sql.prepare("UPDATE vortex_sessions SET expires=0").run();
assert.equal((await call("me","GET",undefined,changed.value.token)).status,401);
assert.equal((await call("register","POST",{login:"large",nickname:"Large",password:"x".repeat(5000)},null,"body-fixture")).status,413);
sql.prepare("INSERT INTO vortex_auth_limits VALUES(?,1,0)").run("expired-fixture");
await call("me","GET",undefined,null,"cleanup-fixture");
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM vortex_auth_limits WHERE key='expired-fixture'").get().n,0);
for(let i=0;i<21;i++){
  const result=await call("login","POST",{login:"unknown",password},null,"throttle-fixture");
  assert.equal(result.status,i<20?401:429);
}
const throttledKey=createHash("sha256").update("login-ip:throttle-fixture").digest("hex");
const originalReset=sql.prepare("SELECT reset_at FROM vortex_auth_limits WHERE key=?").get(throttledKey).reset_at;
assert.equal((await call("login","POST",{login:"unknown",password},null,"throttle-fixture")).status,429);
assert.equal(sql.prepare("SELECT reset_at FROM vortex_auth_limits WHERE key=?").get(throttledKey).reset_at,originalReset,"blocked clicks never extend cooldown");
sql.prepare("UPDATE vortex_auth_limits SET reset_at=0 WHERE key=?").run(throttledKey);
assert.equal((await call("login","POST",{login:"unknown",password},null,"throttle-fixture")).status,401,"expired gate allows a real credential check");
assert.ok(!JSON.stringify(sql.prepare("SELECT * FROM vortex_auth_limits").all()).includes("throttle-fixture"));
assert.deepEqual(sql.prepare("SELECT * FROM cape_profiles").all(),legacy);
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM cape_textures").get().n,0);
const broken=await call("me","GET",undefined,token,"broken-db",{VORTEX_ACCOUNTS_ENABLED:"true",DB:{prepare(){throw new Error("fixture-private-database-error");}}});
assert.deepEqual(broken,{status:503,value:{error:"service_unavailable"}});
sql.close();
console.log("VORTEX_ACCOUNTS_OK: registration, password hashing, unique names, stable identity, token hashes, logout/expiry, password rotation, session cap, durable throttle, request bounds, safe errors, legacy cape isolation");
