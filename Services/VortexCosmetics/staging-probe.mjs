// Isolated deployment probe: no DB, user data, credentials or production imports.
import {scrypt} from "node:crypto";
export default {async fetch(request,env={}){
  const path=new URL(request.url).pathname;
  if(path==="/health")return Response.json({service:"vortex-accounts-staging",probe_enabled:env.PROBE_ENABLED==="true",accounts_enabled:false});
  if(path!=="/probe" || request.method!=="POST" || env.PROBE_ENABLED!=="true")return new Response("Not found",{status:404});
  try{
    const result=await new Promise((resolve,reject)=>scrypt("Synthetic staging test only 481!","public-fixed-fixture-salt",32,{N:16384,r:8,p:5,maxmem:64*1024*1024},(error,result)=>error?reject(error):resolve(result)));
    return Response.json({scrypt_supported:true,bytes:result.length,accounts_enabled:false},{headers:{"Cache-Control":"no-store"}});
  }catch{return Response.json({scrypt_supported:false,accounts_enabled:false},{status:503});}
}};
