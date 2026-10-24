import {createHash,timingSafeEqual} from "node:crypto";
import {handleAccounts} from "./accounts.mjs";

// Private staging entrypoint only. Never imports or binds the legacy cape service.
const reply=(value,status=200)=>Response.json(value,{status,headers:{"Cache-Control":"no-store","X-Content-Type-Options":"nosniff"}});
export default {async fetch(request,env){
  const path=new URL(request.url).pathname;
  if(path==="/health" && request.method==="GET")return reply({service:"vortex-accounts-staging",staging:true,public_registration:false});
  if(!path.startsWith("/v3/accounts/"))return reply({error:"not_found"},404);
  // No key, no access: secrets stay out of config, source, client builds and URLs.
  const supplied=request.headers.get("X-Vortex-Staging-Key")||"",expected=env.STAGING_KEY;
  if(typeof expected!=="string" || !/^[a-f0-9]{64}$/.test(expected))return reply({error:"staging_unavailable"},503);
  if(supplied.length!==64 || !timingSafeEqual(createHash("sha256").update(supplied).digest(),createHash("sha256").update(expected).digest()))return reply({error:"unauthorized"},401);
  try{return await handleAccounts(request,env) || reply({error:"not_found"},404);}
  catch{console.error(JSON.stringify({event:"staging_account_failure"}));return reply({error:"service_unavailable"},503);}
}};
