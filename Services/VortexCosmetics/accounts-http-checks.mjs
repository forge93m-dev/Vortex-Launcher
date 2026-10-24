import assert from "node:assert/strict";
import {randomBytes,randomUUID} from "node:crypto";

// Synthetic identities only. Tokens/passwords are never printed or written to disk.
export async function checkAccountHttp(send,key,{publicService=false}={}){
  const suffix=randomUUID().replaceAll("-","").slice(0,10);
  const login="qa_"+suffix,nickname="QA_"+suffix,renamed="QR_"+suffix;
  const password=randomBytes(24).toString("hex"),replacement=randomBytes(24).toString("hex");
  async function call(path,method="GET",value,token,access=key){
    const headers={};if(access)headers["X-Vortex-Staging-Key"]=access;
    if(value!==undefined)headers["Content-Type"]="application/json";
    if(token)headers.Authorization="Bearer "+token;
    const response=await send(path,{method,headers,body:value===undefined?undefined:JSON.stringify(value)});
    assert.equal(response.headers.get("Cache-Control"),"no-store");
    const data=await response.json();
    assert.ok(!JSON.stringify(data).includes(password));assert.ok(!JSON.stringify(data).includes(replacement));
    return {status:response.status,data};
  }
  assert.equal((await call("/health", "GET",undefined,undefined,"")).data.public_registration,publicService);
  if(!publicService){
    assert.equal((await call("/v3/accounts/capabilities","GET",undefined,undefined,"")).status,401);
    assert.equal((await call("/v3/accounts/capabilities","GET",undefined,undefined,"b".repeat(64))).status,401);
  }
  assert.equal((await call("/v3/accounts/capabilities")).data.enabled,true);
  let first,second,rotated;
  try{
    const registered=await call("/v3/accounts/register","POST",{login,nickname,password});
    assert.equal(registered.status,201,"register");first=registered.data.token;
    assert.ok(typeof first==="string" && /^[a-f0-9]{64}$/.test(first),"valid opaque session");const id=registered.data.profile.id;
    assert.equal((await call("/v3/accounts/register","POST",{login:login.toUpperCase(),nickname,password})).status,409);
    assert.equal((await call("/v3/accounts/login","POST",{login,password:"deliberately-wrong-fixture"})).status,401);
    const signedIn=await call("/v3/accounts/login","POST",{login,password});assert.equal(signedIn.status,200);second=signedIn.data.token;
    assert.equal(signedIn.data.profile.id,id);
    assert.equal((await call("/v3/accounts/me")).status,401);
    assert.equal((await call("/v3/accounts/me","GET",undefined,first)).data.profile.id,id);
    const changed=await call("/v3/accounts/nickname","PUT",{nickname:renamed},first);
    assert.equal(changed.status,200);assert.equal(changed.data.profile.id,id);assert.equal(changed.data.profile.nickname,renamed);
    assert.equal((await call("/v3/accounts/me","GET",undefined,second)).data.profile.nickname,renamed);
    assert.equal((await call("/v3/accounts/logout","POST",{},first)).status,200);first=null;
    const passwordChange=await call("/v3/accounts/password","PUT",{current_password:password,password:replacement},second);
    assert.equal(passwordChange.status,200);rotated=passwordChange.data.token;
    assert.equal((await call("/v3/accounts/me","GET",undefined,second)).status,401);second=null;
    assert.equal((await call("/v3/accounts/login","POST",{login,password})).status,401);
    const finalLogin=await call("/v3/accounts/login","POST",{login,password:replacement});assert.equal(finalLogin.status,200);second=finalLogin.data.token;
    assert.equal(finalLogin.data.profile.id,id);assert.equal(finalLogin.data.profile.nickname,renamed);
    assert.equal((await call("/v3/accounts/register","POST",{login,nickname,password:"x".repeat(5000)})).status,413);
    console.log("ACCOUNT_HTTP_OK: "+(publicService?"public service":"private gate")+", registration, unique names, login, stable ID, rename, logout, password rotation and bounded input");
    return {id,login};
  }finally{
    for(const token of [first,second,rotated].filter(Boolean)){
      const result=await call("/v3/accounts/logout","POST",{},token);
      assert.ok(result.status===200 || result.status===401,"fixture session revocation");
    }
  }
}
