import {scrypt,randomBytes,timingSafeEqual,createHash} from "node:crypto";

// Separate Vortex identities. These routes never claim Minecraft UUIDs or change legacy cape owners.
const SESSION_MS=30*24*60*60*1000;
const HASH_OPTIONS={N:16384,r:8,p:5,maxmem:64*1024*1024};
const hex=bytes=>Buffer.from(bytes).toString("hex");
const digest=value=>createHash("sha256").update(value).digest("hex");
const reply=(value,status=200,headers={})=>Response.json(value,{status,headers:{"Cache-Control":"no-store","X-Content-Type-Options":"nosniff",...headers}});
const fail=(code,status)=>Object.assign(new Error(code),{code,status});
const login=value=>typeof value==="string" && value.length>=3 && value.length<=24 && !/[^a-zA-Z0-9_]/.test(value)?value.toLowerCase():null;
const nickname=value=>typeof value==="string" && value.length>=3 && value.length<=16 && !/[^a-zA-Z0-9_]/.test(value)?value:null;
function password(value){return typeof value==="string" && [...value].length>=15 && value.length<=128 && Buffer.byteLength(value,"utf8")<=512;}
export function derivePassword(value,salt){return new Promise((resolve,reject)=>scrypt(value,salt,32,HASH_OPTIONS,(error,result)=>error?reject(error):resolve(result)));}
async function read(request){
  if(!request.headers.get("content-type")?.startsWith("application/json"))throw fail("invalid_request",400);
  if(!request.body)throw fail("invalid_request",400);
  const reader=request.body.getReader();let size=0;const chunks=[];
  try{for(;;){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>4096){await reader.cancel();throw fail("request_too_large",413);}chunks.push(value);}}
  finally{reader.releaseLock();}
  try{const data=JSON.parse(Buffer.concat(chunks).toString("utf8"));if(!data || typeof data!=="object" || Array.isArray(data))throw 0;return data;}
  catch{throw fail("invalid_request",400);}
}
async function limit(db,key,max,windowMs){
  const now=Date.now();
  await db.prepare("DELETE FROM vortex_auth_limits WHERE key IN (SELECT key FROM vortex_auth_limits WHERE reset_at<=? LIMIT 128)").bind(now).run();
  const result=await db.prepare(`INSERT INTO vortex_auth_limits(key,hits,reset_at) VALUES(?,1,?)
    ON CONFLICT(key) DO UPDATE SET hits=CASE WHEN reset_at<=? THEN 1 ELSE hits+1 END,
    reset_at=CASE WHEN reset_at<=? THEN excluded.reset_at ELSE reset_at END RETURNING hits,reset_at`).bind(digest(key),now+windowMs,now,now).all();
  const row=result.results[0];
  if(row.hits>max)throw Object.assign(fail("rate_limited",429),{retryAfter:Math.max(1,Math.ceil((row.reset_at-now)/1000))});
}
const publicProfile=row=>({id:row.id,login:row.login,nickname:row.nickname});
async function session(db,id,passwordHash){
  const token=hex(randomBytes(32)),expires=Date.now()+SESSION_MS;
  const inserted=await db.batch([
    db.prepare("DELETE FROM vortex_sessions WHERE expires<=?").bind(Date.now()),
    db.prepare("DELETE FROM vortex_sessions WHERE account_id=? AND token_hash NOT IN (SELECT token_hash FROM vortex_sessions WHERE account_id=? ORDER BY created DESC LIMIT 7)").bind(id,id),
    db.prepare("INSERT INTO vortex_sessions(token_hash,account_id,created,expires) SELECT ?,id,?,? FROM vortex_accounts WHERE id=? AND password_hash=?").bind(digest(token),Date.now(),expires,id,passwordHash)
  ]);
  if(inserted[2].meta.changes!==1)throw fail("unauthorized",401);
  return {token,expires};
}
export async function authenticate(request,db){
  const token=request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
  if(!token)throw fail("unauthorized",401);
  const result=await db.prepare(`SELECT a.id,a.login,a.nickname,s.token_hash,s.expires FROM vortex_sessions s
    JOIN vortex_accounts a ON a.id=s.account_id WHERE s.token_hash=? AND s.expires>?`).bind(digest(token),Date.now()).all();
  if(!result.results.length)throw fail("unauthorized",401);return result.results[0];
}
export async function handleAccounts(request,env){
  const path=new URL(request.url).pathname;
  if(!path.startsWith("/v3/accounts/"))return null;
  const enabled=env.VORTEX_ACCOUNTS_ENABLED==="true";
  if(path==="/v3/accounts/capabilities" && request.method==="GET")return reply({enabled,protocol:1,password_min:15});
  if(!enabled || !env.DB)return reply({error:"accounts_not_enabled"},503);
  try{
    const db=env.DB,ip=request.headers.get("CF-Connecting-IP")||"local";
    // Durable limits are shared across isolates; throttle BEFORE expensive password work.
    await limit(db,"all:"+ip,120,15*60*1000);
    if((path==="/v3/accounts/register" || path==="/v3/accounts/login") && request.method==="POST"){
      await limit(db,"login-ip:"+ip,20,15*60*1000);
      const value=await read(request),name=login(value.login);
      if(!name || typeof value.password!=="string" || value.password.length>128 || Buffer.byteLength(value.password,"utf8")>512)throw fail("invalid_credentials",400);
      await limit(db,"login-name:"+name,30,15*60*1000);
      if(path.endsWith("/register")){
        if(!password(value.password) || !nickname(value.nickname))throw fail("invalid_registration",400);
        await limit(db,"register:"+ip,5,60*60*1000);
        const salt=hex(randomBytes(16)),hash=hex(await derivePassword(value.password,salt)),id=crypto.randomUUID();
        const result=await db.prepare("INSERT INTO vortex_accounts(id,login,nickname,nickname_key,password_salt,password_hash,created) VALUES(?,?,?,?,?,?,?) ON CONFLICT DO NOTHING")
          .bind(id,name,value.nickname,value.nickname.toLowerCase(),salt,hash,Date.now()).run();
        if(result.meta.changes!==1)throw fail("name_unavailable",409);
        return reply({profile:{id,login:name,nickname:value.nickname},...await session(db,id,hash)},201);
      }
      const result=await db.prepare("SELECT id,login,nickname,password_salt,password_hash FROM vortex_accounts WHERE login=?").bind(name).all();
      const account=result.results[0];
      const actual=await derivePassword(value.password,account?.password_salt||"0".repeat(32));
      const expected=account?Buffer.from(account.password_hash,"hex"):Buffer.alloc(32);
      if(expected.length!==actual.length || !timingSafeEqual(actual,expected) || !account)throw fail("invalid_credentials",401);
      return reply({profile:publicProfile(account),...await session(db,account.id,account.password_hash)});
    }
    const account=await authenticate(request,db);
    if(path==="/v3/accounts/me" && request.method==="GET")return reply({profile:publicProfile(account)});
    if(path==="/v3/accounts/logout" && request.method==="POST"){
      await db.prepare("DELETE FROM vortex_sessions WHERE token_hash=?").bind(account.token_hash).run();return reply({signed_out:true});
    }
    if(path==="/v3/accounts/nickname" && request.method==="PUT"){
      await limit(db,"rename:"+account.id,5,60*60*1000);
      const value=await read(request);if(!nickname(value.nickname))throw fail("invalid_nickname",400);
      const result=await db.prepare("UPDATE OR IGNORE vortex_accounts SET nickname=?,nickname_key=? WHERE id=?").bind(value.nickname,value.nickname.toLowerCase(),account.id).run();
      if(result.meta.changes!==1)throw fail("name_unavailable",409);
      return reply({profile:{id:account.id,login:account.login,nickname:value.nickname}});
    }
    if(path==="/v3/accounts/password" && request.method==="PUT"){
      await limit(db,"password:"+account.id,5,60*60*1000);
      const value=await read(request);if(!password(value.password) || typeof value.current_password!=="string" || value.current_password.length>128)throw fail("invalid_registration",400);
      const result=await db.prepare("SELECT password_salt,password_hash FROM vortex_accounts WHERE id=?").bind(account.id).all();
      const old=result.results[0],actual=await derivePassword(value.current_password,old.password_salt);
      if(!timingSafeEqual(actual,Buffer.from(old.password_hash,"hex")))throw fail("invalid_credentials",401);
      const salt=hex(randomBytes(16)),hash=hex(await derivePassword(value.password,salt));
      const changed=await db.batch([
        db.prepare("UPDATE vortex_accounts SET password_salt=?,password_hash=? WHERE id=? AND password_hash=?").bind(salt,hash,account.id,old.password_hash),
        db.prepare("DELETE FROM vortex_sessions WHERE account_id=? AND EXISTS (SELECT 1 FROM vortex_accounts WHERE id=? AND password_hash=?)").bind(account.id,account.id,hash)
      ]);
      if(changed[0].meta.changes!==1)throw fail("unauthorized",401);
      return reply({profile:publicProfile(account),...await session(db,account.id,hash)});
    }
    return reply({error:"not_found"},404);
  }catch(error){
    if(error.status===429 && Number.isSafeInteger(error.retryAfter))return reply({error:"rate_limited",retry_after:error.retryAfter},429,{"Retry-After":String(error.retryAfter)});
    return reply({error:error.status?error.code:"service_unavailable"},error.status||503);
  }
}
