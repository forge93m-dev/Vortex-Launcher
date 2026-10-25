import assert from "node:assert/strict";
import {randomBytes,createHash} from "node:crypto";
import {offlineIds} from "./wardrobe.mjs";
export async function checkWardrobeHttp(send){
  const suffix=randomBytes(4).toString("hex"),login="wg_"+suffix,nickname="WG_"+suffix;
  const password=randomBytes(24).toString("hex");let session;
  async function call(path,method="GET",value,token){const response=await send(path,{method,headers:{"Content-Type":"application/json",...(token?{Authorization:"Bearer "+token}:{})},body:value===undefined?undefined:JSON.stringify(value)});const data=response.headers.get("Content-Type")?.includes("json")?await response.json():Buffer.from(await response.arrayBuffer());return {status:response.status,data};}
  try{
    const account=await call("/v3/accounts/register","POST",{login,nickname,password});assert.equal(account.status,201);session=account.data.token;
    const uuid=offlineIds(nickname)[1];
    const access=await call("/v3/accounts/wardrobe-session","POST",{uuid},session);assert.equal(access.status,200);
    const grant=access.data.token;assert.ok(/^[a-f0-9]{64}$/.test(grant));assert.notEqual(grant,session);
    assert.equal((await call("/v3/accounts/me","GET",undefined,grant)).status,401);
    assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},grant)).status,200);
    // Animated built-in capes are accepted like any built-in cape; unknown ids are not.
    assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"pulse"},grant)).status,200);
    assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"pulse2"},grant)).status,400);
    assert.equal((await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]})).data.profiles[0].cape,"pulse");
    assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"aurora"},grant)).status,200);
    const skin=Buffer.alloc(16384,255),hash=createHash("sha256").update(skin).digest("hex");
    assert.equal((await call("/v3/wardrobe/skin","PUT",{enabled:true,slim:true,pixels:skin.toString("base64")},grant)).status,200);
    const lookup=await call("/v3/wardrobe/lookup","POST",{uuids:[uuid]});assert.equal(lookup.data.profiles[0].cape,"aurora");assert.equal(lookup.data.profiles[0].skin_hash,hash);
    assert.deepEqual((await call("/v3/wardrobe/textures/"+hash)).data,skin);
    await call("/v3/accounts/logout","POST",{},session);session=null;
    assert.equal((await call("/v3/wardrobe/cape","PUT",{cape:"none"},grant)).status,401);
    console.log("WARDROBE_HTTP_OK: actual Worker HTTP capability, public skin/cape lookup, texture bytes and logout revocation");
  }finally{if(session)await call("/v3/accounts/logout","POST",{},session);}
}
