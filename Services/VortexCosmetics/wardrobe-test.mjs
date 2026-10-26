import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {createHash} from "node:crypto";
import {deflateSync} from "node:zlib";
import {handleAccounts} from "./accounts.mjs";
import {handleWardrobe,offlineIds} from "./wardrobe.mjs";
const hash=v=>createHash("sha256").update(v).digest("hex");
const sql=new DatabaseSync(":memory:"),legacy=new DatabaseSync(":memory:");
sql.exec("PRAGMA foreign_keys=ON");sql.exec(readFileSync(new URL("schema.accounts.sql",import.meta.url),"utf8"));sql.exec(readFileSync(new URL("schema.wardrobe.sql",import.meta.url),"utf8"));
legacy.exec("CREATE TABLE cape_profiles(uuid TEXT PRIMARY KEY,owner_hash TEXT,cape TEXT)");
function wrap(db){return {prepare(query){let args=[];return {bind(...v){args=v;return this;},async all(){return {results:db.prepare(query).all(...args)};},async run(){return {meta:{changes:Number(db.prepare(query).run(...args).changes)}};}};},async batch(statements){db.exec("BEGIN");try{const rows=[];for(const s of statements)rows.push(await s.run());db.exec("COMMIT");return rows;}catch(e){db.exec("ROLLBACK");throw e;}}};}
const env={DB:wrap(sql),LEGACY_DB:wrap(legacy),VORTEX_ACCOUNTS_ENABLED:"true"};
async function call(path,method="GET",value,token){const request=new Request("https://fixture.test"+path,{method,headers:{"Content-Type":"application/json",...(token?{Authorization:"Bearer "+token}:{})},body:value===undefined?undefined:JSON.stringify(value)});const response=await handleWardrobe(request,env)||await handleAccounts(request,env);const data=response.headers.get("Content-Type")?.includes("json")?await response.json():Buffer.from(await response.arrayBuffer());return {status:response.status,data};}
const first=(await call("/v3/accounts/register","POST",{login:"fixture",nickname:"Fixture",password:"Synthetic password only 472!"})).data;
const uuid=offlineIds(first.profile.nickname)[1],canonical=offlineIds(first.profile.nickname)[0];
assert.equal(uuid.length,32);assert.notEqual(uuid,canonical);
const issued=await call("/v3/accounts/wardrobe-session","POST",{uuid},first.token);assert.equal(issued.status,200);const token=issued.data.token;
assert.equal((await call("/v3/accounts/me","GET",undefined,token)).status,401,"cosmetic capability cannot access account");
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},first.token)).status,401,"account token is not a cosmetic capability");
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},token)).status,200);
assert.equal((await call("/v3/wardrobe/cape","PUT",{uuid:canonical,cape:"ember"},token)).status,200,"offline-server UUID alias is bound to same account nickname");
const skin=Buffer.alloc(16384,255),skinHash=hash(skin);
assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:true,slim:true,pixels:skin.toString("base64")},token)).status,200);
// Animated built-in capes are accepted like any built-in cape; unknown ids are not.
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"pulse"},token)).status,200);
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"pulse2"},token)).status,400);
assert.equal((await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]})).data.profiles[0].cape,"pulse");
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},token)).status,200);
let lookup=await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]});assert.equal(lookup.data.profiles[0].cape,"aurora");assert.equal(lookup.data.profiles[0].skin_hash,skinHash);
assert.deepEqual((await call("/v3/wardrobe/textures/"+skinHash)).data,skin);
const cape=Buffer.alloc(8192,127);
assert.equal((await call("/v3/wardrobe/cape","PUT",{pixels:cape.toString("base64")},token)).status,200);
assert.deepEqual((await call("/v3/wardrobe/textures/"+hash(cape))).data,cape);
// Animated custom capes are stored in the account too: same bounds as the cosmetics service, exact pixels, hidden from nobody but the owner's changes.
function anim(width=128,frames=8,frameMs=100,length=width*(width/2)*4*frames,height=width/2){
  const header=Buffer.alloc(20);header.writeUInt32BE(0x56434131,0);header.writeUInt32BE(width,4);header.writeUInt32BE(height,8);header.writeUInt32BE(frames,12);header.writeUInt32BE(frameMs,16);
  return Buffer.concat([header,deflateSync(Buffer.alloc(length,63))]);
}
const animated=anim();
const animSaved=await call("/v3/wardrobe/cape","PUT",{pixels:animated.toString("base64")},token);
assert.equal(animSaved.status,200);assert.equal(animSaved.data.cape,"anim:"+hash(animated));
lookup=await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]});assert.equal(lookup.data.profiles[0].cape,"anim:"+hash(animated));
assert.deepEqual((await call("/v3/wardrobe/textures/"+hash(animated))).data,animated);
for(const frames of [anim(512,61,20),anim(128,120,20),anim(64,2,1000)])assert.equal((await call("/v3/wardrobe/cape","PUT",{pixels:frames.toString("base64")},token)).status,200);
sql.exec("DELETE FROM vortex_auth_limits"); // the write limit is per account and not what is being checked here
for(const bad of [anim(128,1),anim(128,121),anim(128,8,19),anim(128,8,1001),anim(1024,8),anim(128,8,100,undefined,32),anim(512,62),anim(128,8,100,128*64*4*8-1),anim(128,8,100,128*64*4*9),animated.subarray(0,animated.length-1),Buffer.concat([animated,Buffer.from([0])])])
  assert.equal((await call("/v3/wardrobe/cape","PUT",{pixels:bad.toString("base64")},token)).data.error,"invalid_pixels");
sql.exec("DELETE FROM vortex_auth_limits");
assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:false},token)).status,200);
assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:true,slim:false,pixels:"AA=="},token)).status,400);
assert.equal((await call("/v3/wardrobe/cape","PUT",{pixels:"A".repeat(1600000)},token)).status,413);
const old="1".repeat(32),owner="a".repeat(64);legacy.prepare("INSERT INTO cape_profiles VALUES(?,?,?)").run(old,hash(owner),"vortex");
const oldToken=(await call("/v3/accounts/wardrobe-session","POST",{uuid:old},first.token)).data.token;
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},oldToken)).status,403);
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora",legacy_owner:"b".repeat(64)},oldToken)).status,403);
assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:true,slim:false,pixels:skin.toString("base64"),legacy_owner:owner},oldToken)).status,200);
assert.equal((await call("/v3/wardrobe/lookup","POST",{uuids:[old]})).data.profiles[0].cape,"vortex","first skin upload preserves legacy cape");
assert.equal(legacy.prepare("SELECT cape FROM cape_profiles WHERE uuid=?").get(old).cape,"vortex","legacy data is never overwritten");
const unknown=(await call("/v3/accounts/wardrobe-session","POST",{uuid:"2".repeat(32)},first.token)).data.token;
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"ember"},unknown)).data.error,"use_vortex_nickname");
assert.equal((await call("/v3/wardrobe/cape","PUT",{uuid:old,cape:"ember"},token)).status,403,"capability cannot escape its profile scope");
const outsider=(await call("/v3/accounts/register","POST",{login:"outsider",nickname:"Outsider",password:"Synthetic second password 827!"})).data;
const hostile=(await call("/v3/accounts/wardrobe-session","POST",{uuid},outsider.token)).data.token;
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"ember"},hostile)).data.error,"profile_owned","a second account cannot overwrite an already bound profile");
await call("/v3/accounts/logout","POST",{},outsider.token);
await call("/v3/accounts/logout","POST",{},first.token);
assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"none"},token)).status,401,"logout revokes dependent game grants");
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM vortex_wardrobe_sessions").get().n,0);
assert.ok(!JSON.stringify(sql.prepare("SELECT * FROM vortex_wardrobe_sessions").all()).includes(token));
console.log("WARDROBE_OK: scoped grants, offline aliases, skins/capes, bounded textures, explicit legacy proof, protected profiles, parent-session revocation; legacy DB unchanged");
sql.close();legacy.close();
