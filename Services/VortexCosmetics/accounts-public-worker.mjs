import {handleAccounts} from "./accounts.mjs";
import {handleWardrobe} from "./wardrobe.mjs";

// Account-only deployment: no legacy cape imports, secrets or database bindings.
const reply=(value,status=200)=>Response.json(value,{status,headers:{"Cache-Control":"no-store","X-Content-Type-Options":"nosniff"}});
export default {async fetch(request,env){
  const path=new URL(request.url).pathname;
  if(path==="/health" && request.method==="GET")return reply({service:"vortex-accounts",public_registration:env.VORTEX_ACCOUNTS_ENABLED==="true" && !!env.DB});
  if(!path.startsWith("/v3/accounts/") && !path.startsWith("/v3/wardrobe/"))return reply({error:"not_found"},404);
  try{return await handleWardrobe(request,env) || await handleAccounts(request,env) || reply({error:"not_found"},404);}
  catch{console.error(JSON.stringify({event:"account_service_failure"}));return reply({error:"service_unavailable"},503);}
}};
