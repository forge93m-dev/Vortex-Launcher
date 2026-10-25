import {handleAccounts} from "./accounts.mjs";
import {BUILT_IN_CAPES} from "./capes.mjs";
// Only bounded RGBA pixels / bounded zlib pixel envelopes, never image URLs.
export const CAPES = BUILT_IN_CAPES;
const gates = new Map();
const encoder = new TextEncoder();
const uuid = value => typeof value === "string" && /^[a-f0-9]{32}$/.test(value.replaceAll("-", "").toLowerCase())
  ? value.replaceAll("-", "").toLowerCase() : null;
const key = value => typeof value === "string" && /^[a-f0-9]{64}$/.test(value);
export async function digest(value) {
  return [...new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value)))].map(n => n.toString(16).padStart(2,"0")).join("");
}
const json = (value, status = 200, cache = "no-store") => Response.json(value, {status,
  headers: {"Cache-Control":cache, "X-Content-Type-Options":"nosniff", "Content-Security-Policy":"default-src 'none'"}});
async function body(request) {
  if (!(request.headers.get("content-type") || "").startsWith("application/json")) throw Object.assign(new Error("JSON required"), {status:415});
  const reader = request.body?.getReader();
  if (!reader) throw Object.assign(new Error("Empty request"), {status:400});
  let size = 0; const chunks = [];
  try {
    for (;;) {
      const {value, done} = await reader.read(); if (done) break;
      size += value.length;
      const route = new URL(request.url).pathname;
      const limit = route === "/v2/animated" ? 1100000 : route === "/v2/custom" ? 900000 : route === "/v1/custom" ? 12288 : 4096;
      if (size > limit) { await reader.cancel(); throw Object.assign(new Error("Request too large"), {status:413}); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let position = 0;
  for (const chunk of chunks) { bytes.set(chunk, position); position += chunk.length; }
  try {
    const value = JSON.parse(new TextDecoder("utf-8", {fatal:true, ignoreBOM:false}).decode(bytes));
    if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error();
    return value;
  } catch { throw Object.assign(new Error("Invalid JSON"), {status:400}); }
}
function rate(request) {
  const now = Date.now(), ip = request.headers.get("CF-Connecting-IP") || "local";
  for (const [k,v] of gates) if (now - v.at >= 60000) gates.delete(k);
  let item = gates.get(ip);
  if (!item) {
    if (gates.size >= 4096) return false;
    item = {at:now, count:0}; gates.set(ip,item);
  }
  return ++item.count <= 120;
}
const encode64=bytes=> {
  const parts=[]; for(let i=0;i<bytes.length;i+=16384) parts.push(String.fromCharCode(...bytes.subarray(i,i+16384)));
  return btoa(parts.join(""));
};
async function validateHd(bytes) {
  if(bytes.length<18 || bytes.length>655360) throw Object.assign(new Error("Invalid HD size"),{status:400});
  const view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
  const width=view.getUint32(4),height=view.getUint32(8);
  if(view.getUint32(0)!==0x56435032 || ![64,128,256,512,1024].includes(width) || height!==width/2) throw Object.assign(new Error("Invalid HD dimensions"),{status:400});
  const reader=new Response(bytes.slice(12)).body.pipeThrough(new DecompressionStream("deflate")).getReader();
  let total=0;
  try {
    for(;;) {
      const {value,done}=await reader.read(); if(done) break;
      total+=value.length;
      if(total>width*height*4) {await reader.cancel(); throw new Error("Too many pixels");}
    }
    if(total!==width*height*4) throw new Error("Truncated pixels");
  } catch {throw Object.assign(new Error("Invalid compressed pixels"),{status:400});}
  finally {reader.releaseLock();}
}
// Animated custom cape: "VCA1", width, height, frames, milliseconds per frame (big-endian u32), then a zlib stream of
// frames*width*height*4 RGBA bytes. Frames are stacked in one stream; every size and count is bounded.
const ANIMATED_MAX_BYTES=786432;
async function validateAnimated(bytes) {
  if(bytes.length<24 || bytes.length>ANIMATED_MAX_BYTES) throw Object.assign(new Error("Invalid animated cape size"),{status:400});
  const view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
  const width=view.getUint32(4),height=view.getUint32(8),frames=view.getUint32(12),frameMs=view.getUint32(16);
  if(view.getUint32(0)!==0x56434131 || ![64,128,256,512].includes(width) || height!==width/2 || frames<2 || frames>32 || frameMs<50 || frameMs>1000)
    throw Object.assign(new Error("Invalid animated cape header"),{status:400});
  const expected=width*height*4*frames;
  const reader=new Response(bytes.slice(20)).body.pipeThrough(new DecompressionStream("deflate")).getReader();
  let total=0;
  try {
    for(;;) {
      const {value,done}=await reader.read(); if(done) break;
      total+=value.length;
      if(total>expected) {await reader.cancel(); throw new Error("Too many pixels");}
    }
    if(total!==expected) throw new Error("Truncated pixels");
  } catch {throw Object.assign(new Error("Invalid compressed animated pixels"),{status:400});}
  finally {reader.releaseLock();}
}
export default {
  async fetch(request, env) {
    try {
      const accountResponse=await handleAccounts(request,env);if(accountResponse)return accountResponse;
      const path = new URL(request.url).pathname;
      if (path === "/health" && request.method === "GET") return json({service:"Vortex Cosmetics", protocol:1, storage:!!env.DB});
      if (path === "/v1/catalog" && request.method === "GET") return json({protocol:1, capes:CAPES,hd:true},200,"public, max-age=3600");
      if (!rate(request)) return json({error:"Slow down; retry later"},429);
      if (!env.DB) return json({error:"Storage unavailable"},503);
      if (path === "/v1/lookup" && request.method === "POST") {
        const value = await body(request);
        if (!Array.isArray(value.uuids) || value.uuids.length > 32 || value.uuids.some(v => !uuid(v))) return json({error:"Up to 32 valid UUIDs required"},400);
        const ids = [...new Set(value.uuids.map(uuid))];
        if (!ids.length) return json({capes:[]});
        const result = await env.DB.prepare(`SELECT uuid,cape FROM cape_profiles WHERE uuid IN (${ids.map(()=>"?").join(",")}) AND owner_hash IS NOT NULL`).bind(...ids).all();
        return json({capes:result.results.map(row=>({uuid:row.uuid,cape:CAPES.includes(row.cape) || value.custom === true && /^custom:[a-f0-9]{64}$/.test(row.cape) || value.hd === true && /^hd:[a-f0-9]{64}$/.test(row.cape) || value.anim === true && /^anim:[a-f0-9]{64}$/.test(row.cape) ? row.cape:"none"}))});
      }
      if (/^\/v2\/textures\/[a-f0-9]{64}$/.test(path) && request.method === "GET") {
        const found=await env.DB.prepare("SELECT pixels FROM cape_hd_textures WHERE hash=? LIMIT 1").bind(path.slice("/v2/textures/".length)).all();
        if(!found.results.length) return json({error:"Texture not found"},404);
        const bytes=Uint8Array.from(atob(found.results[0].pixels),c=>c.charCodeAt(0));
        if(bytes.length>ANIMATED_MAX_BYTES) return json({error:"Texture unavailable"},503);
        return new Response(bytes,{headers:{"Content-Type":"application/octet-stream","Content-Length":String(bytes.length),
          "Cache-Control":"public, max-age=300","X-Content-Type-Options":"nosniff","Content-Security-Policy":"default-src 'none'"}});
      }
      if(path === "/v2/animated" && request.method === "PUT") {
        const token=request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
        if(!key(token)) return json({error:"Activation required"},401);
        const value=await body(request),id=uuid(value.uuid),owner=await digest(token);
        if(!id || typeof value.pixels!=="string" || value.pixels.length>1048580 || value.pixels.length%4 || !/^[A-Za-z0-9+/]*={0,2}$/.test(value.pixels)) return json({error:"Invalid animated envelope"},400);
        const profile=await env.DB.prepare("SELECT owner_hash FROM cape_profiles WHERE uuid=?").bind(id).all();
        if(profile.results[0]?.owner_hash!==owner) return json({error:"This key cannot change that profile"},403);
        const bytes=Uint8Array.from(atob(value.pixels),c=>c.charCodeAt(0));
        if(encode64(bytes)!==value.pixels) return json({error:"Invalid encoding"},400);
        await validateAnimated(bytes);
        const hash=[...new Uint8Array(await crypto.subtle.digest("SHA-256",bytes))].map(n=>n.toString(16).padStart(2,"0")).join(""),cape="anim:"+hash;
        const results=await env.DB.batch([
          env.DB.prepare("INSERT INTO cape_hd_textures(uuid,hash,pixels) SELECT uuid,?,? FROM cape_profiles WHERE uuid=? AND owner_hash=? ON CONFLICT(uuid) DO UPDATE SET hash=excluded.hash,pixels=excluded.pixels").bind(hash,value.pixels,id,owner),
          env.DB.prepare("UPDATE cape_profiles SET cape=?,updated=? WHERE uuid=? AND owner_hash=?").bind(cape,Date.now(),id,owner)
        ]);
        return results[1].meta.changes===1 ? json({saved:true,cape}) : json({error:"This key cannot change that profile"},403);
      }
      if(path === "/v2/custom" && request.method === "PUT") {
        const token=request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
        if(!key(token)) return json({error:"Activation required"},401);
        const value=await body(request),id=uuid(value.uuid),owner=await digest(token);
        if(!id || typeof value.pixels!=="string" || value.pixels.length>873816 || value.pixels.length%4 || !/^[A-Za-z0-9+/]*={0,2}$/.test(value.pixels)) return json({error:"Invalid HD envelope"},400);
        const profile=await env.DB.prepare("SELECT owner_hash FROM cape_profiles WHERE uuid=?").bind(id).all();
        if(profile.results[0]?.owner_hash!==owner) return json({error:"This key cannot change that profile"},403);
        const bytes=Uint8Array.from(atob(value.pixels),c=>c.charCodeAt(0));
        if(encode64(bytes)!==value.pixels) return json({error:"Invalid encoding"},400);
        await validateHd(bytes);
        const hash=[...new Uint8Array(await crypto.subtle.digest("SHA-256",bytes))].map(n=>n.toString(16).padStart(2,"0")).join(""),cape="hd:"+hash;
        const results=await env.DB.batch([
          env.DB.prepare("INSERT INTO cape_hd_textures(uuid,hash,pixels) SELECT uuid,?,? FROM cape_profiles WHERE uuid=? AND owner_hash=? ON CONFLICT(uuid) DO UPDATE SET hash=excluded.hash,pixels=excluded.pixels").bind(hash,value.pixels,id,owner),
          env.DB.prepare("UPDATE cape_profiles SET cape=?,updated=? WHERE uuid=? AND owner_hash=?").bind(cape,Date.now(),id,owner)
        ]);
        return results[1].meta.changes===1 ? json({saved:true,cape}) : json({error:"This key cannot change that profile"},403);
      }
      if (/^\/v1\/textures\/[a-f0-9]{64}$/.test(path) && request.method === "GET") {
        const hash=path.slice("/v1/textures/".length);
        const found=await env.DB.prepare("SELECT pixels FROM cape_textures WHERE hash=? LIMIT 1").bind(hash).all();
        if(!found.results.length) return json({error:"Texture not found"},404);
        const pixels=Uint8Array.from(atob(found.results[0].pixels),c=>c.charCodeAt(0));
        if(pixels.length!==8192) return json({error:"Texture unavailable"},503);
        return new Response(pixels,{headers:{"Content-Type":"application/octet-stream","Content-Length":"8192",
          "Cache-Control":"public, max-age=300","X-Content-Type-Options":"nosniff","Content-Security-Policy":"default-src 'none'"}});
      }
      if(path === "/v1/custom" && request.method === "PUT") {
        const token=request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
        if(!key(token)) return json({error:"Activation required"},401);
        const value=await body(request), id=uuid(value.uuid);
        if(!id || typeof value.pixels!=="string" || !/^[A-Za-z0-9+/]{10923}=$/.test(value.pixels)) return json({error:"A 64x32 RGBA cape is required"},400);
        const pixels=Uint8Array.from(atob(value.pixels),c=>c.charCodeAt(0));
        if(pixels.length!==8192 || btoa(String.fromCharCode(...pixels))!==value.pixels) return json({error:"Invalid pixels"},400);
        const hash=[...new Uint8Array(await crypto.subtle.digest("SHA-256",pixels))].map(n=>n.toString(16).padStart(2,"0")).join("");
        const owner=await digest(token), cape="custom:"+hash;
        const results=await env.DB.batch([
          env.DB.prepare("INSERT INTO cape_textures(uuid,hash,pixels) SELECT uuid,?,? FROM cape_profiles WHERE uuid=? AND owner_hash=? ON CONFLICT(uuid) DO UPDATE SET hash=excluded.hash,pixels=excluded.pixels").bind(hash,value.pixels,id,owner),
          env.DB.prepare("UPDATE cape_profiles SET cape=?,updated=? WHERE uuid=? AND owner_hash=?").bind(cape,Date.now(),id,owner)
        ]);
        return results[1].meta.changes===1 ? json({saved:true,cape}) : json({error:"This key cannot change that profile"},403);
      }
      if (path === "/v1/claim" && request.method === "POST") {
        const value = await body(request), id = uuid(value.uuid);
        if (!id || !key(value.code) || !key(value.owner)) return json({error:"Invalid activation details"},400);
        const result = await env.DB.prepare("UPDATE cape_profiles SET owner_hash=?, activation_hash=NULL, updated=? WHERE uuid=? AND activation_hash=? AND owner_hash IS NULL")
          .bind(await digest(value.owner),Date.now(),id,await digest(value.code)).run();
        if (result.meta.changes === 1) return json({claimed:true});
        // A lost response must not strand the owner after a successful claim.
        const existing = await env.DB.prepare("SELECT owner_hash FROM cape_profiles WHERE uuid=?").bind(id).all();
        return existing.results[0]?.owner_hash === await digest(value.owner)
          ? json({claimed:true}) : json({error:"Code is invalid, used, or belongs to another profile"},403);
      }
      if (path === "/v1/cape" && request.method === "PUT") {
        const token = request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
        if (!key(token)) return json({error:"Activation required"},401);
        const value = await body(request), id = uuid(value.uuid);
        if (!id || !CAPES.includes(value.cape)) return json({error:"Invalid profile or cape"},400);
        const result = await env.DB.prepare("UPDATE cape_profiles SET cape=?, updated=? WHERE uuid=? AND owner_hash=?")
          .bind(value.cape,Date.now(),id,await digest(token)).run();
        return result.meta.changes === 1 ? json({saved:true,cape:value.cape}) : json({error:"This key cannot change that profile"},403);
      }
      return json({error:"Not found"},404);
    } catch (error) {
      // Never return database statements, credentials, or request contents.
      return json({error:error.status ? error.message : "Service temporarily unavailable"},error.status || 503);
    }
  }
};
