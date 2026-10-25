import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {deflateSync} from "node:zlib";
import service,{digest} from "./worker.mjs";

const sql = new DatabaseSync(":memory:");
sql.exec(readFileSync(new URL("schema.sql", import.meta.url),"utf8"));
const env = {DB:{async batch(statements) {
  sql.exec("BEGIN"); try {const results=[]; for(const statement of statements) results.push(await statement.run()); sql.exec("COMMIT"); return results;}
  catch(error) {sql.exec("ROLLBACK"); throw error;}
}, prepare(query) { return {bind(...args) { return {
  async all() { return {results:sql.prepare(query).all(...args)}; },
  async run() { return {meta:{changes:Number(sql.prepare(query).run(...args).changes)}}; }
}; }}; }}};
const id = "11111111111141118111111111111111", other = "22222222222242228222222222222222";
const code = "a".repeat(64), owner = "b".repeat(64), attacker = "c".repeat(64);
sql.prepare("INSERT INTO cape_profiles(uuid,activation_hash) VALUES(?,?)").run(id,await digest(code));
async function call(path, method="GET", value, token, ip="fixture") {
  const headers = {"CF-Connecting-IP":ip};
  if(value !== undefined) headers["Content-Type"]="application/json";
  if(token) headers.Authorization="Bearer "+token;
  const response = await service.fetch(new Request("https://fixture.test"+path,{method,headers,
    body:value===undefined?undefined:JSON.stringify(value)}),env);
  return {status:response.status,value:await response.json()};
}
assert.equal((await call("/health")).value.storage,true);
assert.deepEqual((await call("/v1/catalog")).value.capes,["none","vortex","aurora","ember","nebula","flow","pulse"]);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"vortex"})).status,401);
assert.equal((await call("/v1/claim","POST",{uuid:id,code:attacker,owner})).status,403);
assert.deepEqual((await call("/v1/lookup","POST",{uuids:[id]})).value,{capes:[]});
assert.equal((await call("/v1/claim","POST",{uuid:id,code,owner})).status,200);
assert.equal((await call("/v1/claim","POST",{uuid:id,code,owner})).status,200);
assert.equal((await call("/v1/claim","POST",{uuid:id,code,owner:attacker})).status,403);
assert.equal(sql.prepare("SELECT activation_hash FROM cape_profiles WHERE uuid=?").get(id).activation_hash,null);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"aurora"},attacker)).status,403);
assert.equal((await call("/v1/cape","PUT",{uuid:other,cape:"aurora"},owner)).status,403);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"https://evil.test/image.png"},owner)).status,400);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"nebula2"},owner,"animated-fixture")).status,400);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"Nebula"},owner,"animated-fixture")).status,400);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"vortex"},owner)).status,200);
const otherCode="d".repeat(64),otherOwner="e".repeat(64);
sql.prepare("INSERT INTO cape_profiles(uuid,activation_hash) VALUES(?,?)").run(other,await digest(otherCode));
assert.equal((await call("/v1/claim","POST",{uuid:other,code:otherCode,owner:otherOwner})).status,200);
assert.equal((await call("/v1/cape","PUT",{uuid:other,cape:"ember"},otherOwner)).status,200);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"ember"},otherOwner)).status,403);
const visible = await call("/v1/lookup","POST",{uuids:[id,id.toUpperCase(),other]});
assert.deepEqual(visible.value,{capes:[{uuid:id,cape:"vortex"},{uuid:other,cape:"ember"}]});
assert.ok(!JSON.stringify(visible).includes(owner));
// Animated capes are ordinary catalogue ids: selectable by their owner, visible in the public lookup.
for(const animated of ["nebula","flow","pulse"]){
  assert.equal((await call("/v1/cape","PUT",{uuid:other,cape:animated},otherOwner,"animated-fixture")).status,200);
  assert.deepEqual((await call("/v1/lookup","POST",{uuids:[other]},null,"animated-fixture")).value,{capes:[{uuid:other,cape:animated}]});
  assert.equal((await call("/v1/cape","PUT",{uuid:other,cape:animated},owner,"animated-fixture")).status,403);
}
assert.equal((await call("/v1/cape","PUT",{uuid:other,cape:"ember"},otherOwner)).status,200);
assert.equal((await call("/v1/lookup","POST",{uuids:["' OR 1=1 --"]})).status,400);
assert.equal((await call("/v1/lookup","POST",{uuids:Array(33).fill(id)})).status,400);
assert.equal((await call("/v1/claim","POST",{uuid:id,code,owner,extra:"x".repeat(5000)})).status,413);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"none"},owner)).status,200);
assert.equal((await call("/v1/lookup","POST",{uuids:[id]})).value.capes[0].cape,"none");
const pixels=Buffer.alloc(8192,127), encoded=pixels.toString("base64");
assert.equal((await call("/v1/custom","PUT",{uuid:id,pixels:encoded},otherOwner)).status,403);
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM cape_textures").get().n,0);
const uploaded=await call("/v1/custom","PUT",{uuid:id,pixels:encoded},owner);
assert.equal(uploaded.status,200); assert.match(uploaded.value.cape,/^custom:[a-f0-9]{64}$/);
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true})).value.capes[0].cape,uploaded.value.cape);
assert.equal((await call("/v1/lookup","POST",{uuids:[id]})).value.capes[0].cape,"none"); // older clients remain compatible
const image=await service.fetch(new Request("https://fixture.test/v1/textures/"+uploaded.value.cape.slice(7)),env);
assert.equal(image.status,200); assert.equal(image.headers.get("Content-Type"),"application/octet-stream");
assert.deepEqual(Buffer.from(await image.arrayBuffer()),pixels);
const replacement=await call("/v1/custom","PUT",{uuid:id,pixels:Buffer.alloc(8192,42).toString("base64")},owner);
assert.equal(replacement.status,200); assert.notEqual(replacement.value.cape,uploaded.value.cape);
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM cape_textures WHERE uuid=?").get(id).n,1);
assert.equal((await service.fetch(new Request("https://fixture.test/v1/textures/"+uploaded.value.cape.slice(7)),env)).status,404);
assert.equal((await call("/v1/custom","PUT",{uuid:id,pixels:"not PNG or pixels"},owner)).status,400);
assert.equal((await call("/v1/custom","PUT",{uuid:id,pixels:"x".repeat(14000)},owner)).status,413);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:replacement.value.cape},owner)).status,400); // custom IDs cannot be forged through selection
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"none"},owner)).status,200);
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true})).value.capes[0].cape,"none");
function hdEnvelope(width=1024,height=512,length=width*height*4) {
  const header=Buffer.alloc(12); header.writeUInt32BE(0x56435032,0); header.writeUInt32BE(width,4); header.writeUInt32BE(height,8);
  return Buffer.concat([header,deflateSync(Buffer.alloc(length,127))]);
}
const hd=hdEnvelope(), hdEncoded=hd.toString("base64");
assert.equal((await call("/v2/custom","PUT",{uuid:id,pixels:hdEncoded},otherOwner)).status,403);
const hdUpload=await call("/v2/custom","PUT",{uuid:id,pixels:hdEncoded},owner);
assert.equal(hdUpload.status,200); assert.match(hdUpload.value.cape,/^hd:[a-f0-9]{64}$/);
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true})).value.capes[0].cape,"none");
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true,hd:true})).value.capes[0].cape,hdUpload.value.cape);
const hdResponse=await service.fetch(new Request("https://fixture.test/v2/textures/"+hdUpload.value.cape.slice(3)),env);
assert.equal(hdResponse.status,200); assert.deepEqual(Buffer.from(await hdResponse.arrayBuffer()),hd);
for(const malformed of [hdEnvelope(2048,1024),hdEnvelope(64,32,8193),hdEnvelope(64,32,8191),hd.subarray(0,hd.length-1),Buffer.concat([hd,Buffer.from([0])])]) {
  assert.equal((await call("/v2/custom","PUT",{uuid:id,pixels:malformed.toString("base64")},owner)).status,400);
}
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM cape_hd_textures WHERE uuid=?").get(id).n,1);
// Animated custom capes: VCA1 header + zlib frames, bounded in size, frame count, speed and exact pixel count.
function animEnvelope(width=128,height=64,frames=8,frameMs=100,length=width*height*4*frames) {
  const header=Buffer.alloc(20); header.writeUInt32BE(0x56434131,0); header.writeUInt32BE(width,4); header.writeUInt32BE(height,8);
  header.writeUInt32BE(frames,12); header.writeUInt32BE(frameMs,16);
  return Buffer.concat([header,deflateSync(Buffer.alloc(length,63))]);
}
const anim=animEnvelope();
assert.equal((await call("/v2/animated","PUT",{uuid:id,pixels:anim.toString("base64")},otherOwner)).status,403);
const animUpload=await call("/v2/animated","PUT",{uuid:id,pixels:anim.toString("base64")},owner);
assert.equal(animUpload.status,200); assert.match(animUpload.value.cape,/^anim:[a-f0-9]{64}$/);
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true,hd:true})).value.capes[0].cape,"none"); // older clients never see ids they do not know
assert.equal((await call("/v1/lookup","POST",{uuids:[id],custom:true,hd:true,anim:true})).value.capes[0].cape,animUpload.value.cape);
const animResponse=await service.fetch(new Request("https://fixture.test/v2/textures/"+animUpload.value.cape.slice(5)),env);
assert.equal(animResponse.status,200); assert.deepEqual(Buffer.from(await animResponse.arrayBuffer()),anim);
for(const malformed of [animEnvelope(128,64,1),animEnvelope(128,64,33),animEnvelope(128,64,8,10),animEnvelope(128,64,8,5000),animEnvelope(1024,512,8),animEnvelope(128,32,8),
    animEnvelope(128,64,8,100,128*64*4*8-1),animEnvelope(128,64,8,100,128*64*4*9),anim.subarray(0,anim.length-1),Buffer.concat([anim,Buffer.from([0])]),Buffer.concat([Buffer.from([0]),anim.subarray(1)])]) {
  assert.equal((await call("/v2/animated","PUT",{uuid:id,pixels:malformed.toString("base64")},owner)).status,400);
}
assert.equal((await call("/v2/animated","PUT",{uuid:id,pixels:"A".repeat(1048584)},owner)).status,400); // over the envelope bound
assert.equal((await call("/v2/animated","PUT",{uuid:id,pixels:"A".repeat(1200000)},owner)).status,413); // oversized request body
const big=animEnvelope(512,256,32,50);
const bigUpload=await call("/v2/animated","PUT",{uuid:id,pixels:big.toString("base64")},owner);
assert.equal(bigUpload.status,200); assert.match(bigUpload.value.cape,/^anim:[a-f0-9]{64}$/); // 512-wide animated capes are accepted
assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM cape_hd_textures WHERE uuid=?").get(id).n,1);
assert.equal((await call("/v1/cape","PUT",{uuid:id,cape:"none"},owner)).status,200);
let last;
for(let i=0;i<121;i++) last=await call("/v1/lookup","POST",{uuids:[]},null,"rate-fixture");
assert.equal(last.status,429);
assert.equal((await service.fetch(new Request("https://fixture.test/v1/lookup",{method:"POST",headers:{"Content-Type":"application/json"},body:"{}"}),{})).status,503);
sql.close();
console.log("COSMETICS_SERVICE_OK: SQLite schema, profile enrollment, owner-only catalogue/custom/HD updates, bounded exact HD decompression, animated custom capes (header/size/frame/speed bounds, exact pixels, hidden from older clients), truncation/trailing-data rejection, replacement, backwards compatibility and no key disclosure");
