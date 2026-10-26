import {createHash,randomBytes} from "node:crypto";
import {authenticate} from "./accounts.mjs";
import {BUILT_IN_CAPES,ANIMATED_MAX_BYTES,validateAnimated} from "./capes.mjs";
const hash=value=>createHash("sha256").update(value).digest("hex");
const id=value=>typeof value==="string" && /^[a-f0-9]{32}$/.test(value.replaceAll("-","").toLowerCase())?value.replaceAll("-","").toLowerCase():null;
const reply=(value,status=200)=>Response.json(value,{status,headers:{"Cache-Control":"no-store","X-Content-Type-Options":"nosniff"}});
const fail=(code,status=400)=>Object.assign(new Error(code),{code,status});
async function body(request,max){
  if(!request.headers.get("content-type")?.startsWith("application/json") || !request.body)throw fail("invalid_request");
  const reader=request.body.getReader(),chunks=[];let count=0;
  try{for(;;){const {value,done}=await reader.read();if(done)break;if((count+=value.length)>max){await reader.cancel();throw fail("request_too_large",413);}chunks.push(value);}}
  finally{reader.releaseLock();}
  try{const value=JSON.parse(Buffer.concat(chunks).toString("utf8"));if(!value || Array.isArray(value) || typeof value!=="object")throw 0;return value;}catch{throw fail("invalid_request");}
}
export function offlineIds(nickname){
  const bytes=createHash("md5").update("OfflinePlayer:"+nickname).digest();bytes[6]=(bytes[6]&15)|48;bytes[8]=(bytes[8]&63)|128;
  const java=bytes.toString("hex"),dotnet=Buffer.from(bytes);
  dotnet.subarray(0,4).reverse();dotnet.subarray(4,6).reverse();dotnet.subarray(6,8).reverse();return [java,dotnet.toString("hex")];
}
async function throttle(db,account){
  const now=Date.now(),key=hash("wardrobe:"+account);
  const result=await db.prepare(`INSERT INTO vortex_auth_limits(key,hits,reset_at) VALUES(?,1,?) ON CONFLICT(key) DO UPDATE SET hits=CASE WHEN reset_at<=? THEN 1 ELSE hits+1 END, reset_at=CASE WHEN reset_at<=? THEN excluded.reset_at ELSE reset_at END RETURNING hits`).bind(key,now+60000,now,now).all();
  if(result.results[0].hits>20)throw fail("rate_limited",429);
}
async function grant(request,db){
  const token=request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];if(!token)throw fail("sign_in_required",401);
  const result=await db.prepare(`SELECT g.account_id,g.profile_uuid,a.nickname FROM vortex_wardrobe_sessions g JOIN vortex_sessions s ON s.token_hash=g.parent_hash JOIN vortex_accounts a ON a.id=g.account_id WHERE g.token_hash=? AND g.expires>? AND s.expires>?`).bind(hash(token),Date.now(),Date.now()).all();
  if(!result.results.length)throw fail("sign_in_required",401);return result.results[0];
}
function pixels(value,max){
  if(typeof value!=="string" || value.length>Math.ceil(max/3)*4 || !/^[A-Za-z0-9+/]*={0,2}$/.test(value))throw fail("invalid_pixels");
  const bytes=Buffer.from(value,"base64");if(bytes.length>max || bytes.toString("base64")!==value)throw fail("invalid_pixels");return bytes;
}
async function capeBytes(value){
  const bytes=pixels(value,ANIMATED_MAX_BYTES);
  if(bytes.length===8192)return {bytes,cape:"custom:"+hash(bytes)};
  if(bytes.length>=24 && bytes.readUInt32BE(0)===0x56434131){
    try{await validateAnimated(bytes);}catch{throw fail("invalid_pixels");}
    return {bytes,cape:"anim:"+hash(bytes)};
  }
  if(bytes.length>655360 || bytes.length<18 || bytes.readUInt32BE(0)!==0x56435032)throw fail("invalid_pixels");
  const width=bytes.readUInt32BE(4),height=bytes.readUInt32BE(8);
  if(![64,128,256,512,1024].includes(width) || height!==width/2)throw fail("invalid_pixels");
  const reader=new Response(bytes.subarray(12)).body.pipeThrough(new DecompressionStream("deflate")).getReader();let total=0;
  try{for(;;){const {value,done}=await reader.read();if(done)break;total+=value.length;if(total>width*height*4){await reader.cancel();throw fail("invalid_pixels");}}if(total!==width*height*4)throw fail("invalid_pixels");}
  catch{throw fail("invalid_pixels");}finally{reader.releaseLock();}
  return {bytes,cape:"hd:"+hash(bytes)};
}
export async function handleWardrobe(request,env){
  const path=new URL(request.url).pathname;
  if(path!=="/v3/accounts/wardrobe-session" && !path.startsWith("/v3/wardrobe/"))return null;
  if(env.VORTEX_ACCOUNTS_ENABLED!=="true" || !env.DB)return reply({error:"accounts_not_enabled"},503);
  try{
    const db=env.DB;
    if(path==="/v3/accounts/wardrobe-session" && request.method==="POST"){
      const account=await authenticate(request,db);await throttle(db,account.id);
      const value=await body(request,4096),uuid=id(value.uuid);if(!uuid)throw fail("invalid_profile");
      const token=randomBytes(32).toString("hex"),expires=Math.min(account.expires,Date.now()+8*60*60*1000);
      await db.batch([
        db.prepare("DELETE FROM vortex_wardrobe_sessions WHERE expires<=?").bind(Date.now()),
        db.prepare("DELETE FROM vortex_wardrobe_sessions WHERE account_id=? AND token_hash NOT IN (SELECT token_hash FROM vortex_wardrobe_sessions WHERE account_id=? ORDER BY expires DESC LIMIT 7)").bind(account.id,account.id),
        db.prepare("INSERT INTO vortex_wardrobe_sessions(token_hash,account_id,parent_hash,profile_uuid,expires) SELECT ?,account_id,token_hash,?,? FROM vortex_sessions WHERE token_hash=? AND expires>?").bind(hash(token),uuid,expires,account.token_hash,Date.now())]);
      return reply({token,expires});
    }
    if(path==="/v3/wardrobe/lookup" && request.method==="POST"){
      const value=await body(request,4096);if(!Array.isArray(value.uuids) || value.uuids.length>32 || value.uuids.some(v=>!id(v)))throw fail("invalid_profiles");
      const ids=[...new Set(value.uuids.map(id))];if(!ids.length)return reply({profiles:[]});
      const rows=await db.prepare(`SELECT uuid,cape,skin_hash,slim FROM vortex_wardrobe WHERE uuid IN (${ids.map(()=>"?").join(",")})`).bind(...ids).all();return reply({profiles:rows.results});
    }
    if(/^\/v3\/wardrobe\/textures\/[a-f0-9]{64}$/.test(path) && request.method==="GET"){
      const key=path.split("/").at(-1);
      const rows=await db.prepare("SELECT cape_pixels,skin_pixels,skin_hash FROM vortex_wardrobe WHERE skin_hash=? OR cape=? OR cape=? OR cape=? LIMIT 1").bind(key,"custom:"+key,"hd:"+key,"anim:"+key).all();
      const row=rows.results[0];if(!row)throw fail("not_found",404);
      const bytes=Buffer.from(row.skin_hash===key?row.skin_pixels:row.cape_pixels,"base64");
      return new Response(bytes,{headers:{"Cache-Control":"public, max-age=300","Content-Type":"application/octet-stream","X-Content-Type-Options":"nosniff"}});
    }
    const access=await grant(request,db);await throttle(db,access.account_id);
    if((path==="/v3/wardrobe/cape" || path==="/v3/wardrobe/skin") && request.method==="PUT"){
      const value=await body(request,path.endsWith("/skin")?24000:1500000);
      if(value.uuid!==undefined){const target=id(value.uuid),aliases=offlineIds(access.nickname);if(!target || target!==access.profile_uuid && !(aliases.includes(target) && aliases.includes(access.profile_uuid)))throw fail("invalid_profile",403);access.profile_uuid=target;}
      const current=(await db.prepare("SELECT account_id FROM vortex_wardrobe WHERE uuid=?").bind(access.profile_uuid).all()).results[0];
      let inheritedCape="none",inheritedPixels=null;
      if(current && current.account_id!==access.account_id)throw fail("profile_owned",403);
      if(!current){
        const count=await db.prepare("SELECT COUNT(*) AS n FROM vortex_wardrobe WHERE account_id=?").bind(access.account_id).all();if(count.results[0].n>=64)throw fail("profile_limit",409);
        // Never migrate a legacy identity based only on a matching nickname.
        if(!env.LEGACY_DB)throw fail("legacy_check_unavailable",503);
        const legacy=(await env.LEGACY_DB.prepare("SELECT owner_hash,cape FROM cape_profiles WHERE uuid=?").bind(access.profile_uuid).all()).results[0];
        if(legacy){
          if(typeof value.legacy_owner!=="string" || !/^[a-f0-9]{64}$/.test(value.legacy_owner) || hash(value.legacy_owner)!==legacy.owner_hash)throw fail("legacy_proof_required",403);
          if(path.endsWith("/skin")){
            if(BUILT_IN_CAPES.includes(legacy.cape))inheritedCape=legacy.cape;
            else if(/^(custom|hd|anim):[a-f0-9]{64}$/.test(legacy.cape)){
              const table=legacy.cape.startsWith("custom:")?"cape_textures":"cape_hd_textures";
              const texture=(await env.LEGACY_DB.prepare(`SELECT pixels FROM ${table} WHERE uuid=? AND hash=?`).bind(access.profile_uuid,legacy.cape.split(":")[1]).all()).results[0];
              if(texture){const checked=await capeBytes(texture.pixels);if(checked.cape!==legacy.cape)throw fail("legacy_texture_unavailable",503);inheritedCape=checked.cape;inheritedPixels=texture.pixels;}
              else throw fail("legacy_texture_unavailable",503);
            }
          }
        }
        else if(!offlineIds(access.nickname).includes(access.profile_uuid))throw fail("use_vortex_nickname",403);
      }
      let cape=inheritedCape,capePixels=inheritedPixels,skinHash=null,skinPixels=null,slim=0;
      if(path.endsWith("/cape")){
        if(value.pixels!==undefined){const packed=await capeBytes(value.pixels);cape=packed.cape;capePixels=packed.bytes.toString("base64");}
        else if(BUILT_IN_CAPES.includes(value.cape))cape=value.cape;else throw fail("invalid_cape");
      }else if(value.enabled!==false){const bytes=pixels(value.pixels,16384);if(bytes.length!==16384 || typeof value.slim!=="boolean")throw fail("invalid_pixels");skinHash=hash(bytes);skinPixels=bytes.toString("base64");slim=value.slim?1:0;}
      // Only the selected cosmetic changes; a concurrent account claim cannot overwrite its owner.
      const field=path.endsWith("/cape")?"cape=excluded.cape,cape_pixels=excluded.cape_pixels":"skin_hash=excluded.skin_hash,skin_pixels=excluded.skin_pixels,slim=excluded.slim";
      const result=await db.prepare(`INSERT INTO vortex_wardrobe(uuid,account_id,cape,cape_pixels,skin_hash,skin_pixels,slim) VALUES(?,?,?,?,?,?,?) ON CONFLICT(uuid) DO UPDATE SET ${field} WHERE account_id=excluded.account_id`).bind(access.profile_uuid,access.account_id,cape,capePixels,skinHash,skinPixels,slim).run();
      if(result.meta.changes!==1)throw fail("profile_owned",403);
      return reply({saved:true,cape,skin_hash:skinHash});
    }
    return reply({error:"not_found"},404);
  }catch(error){return reply({error:error.status?error.code:"service_unavailable"},error.status||503);}
}
