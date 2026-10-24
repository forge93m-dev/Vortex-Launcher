// Local administrator helper. Does not connect to Cloudflare or print the code.
import {randomBytes,createHash} from "node:crypto";
import {mkdirSync,writeFileSync,existsSync} from "node:fs";
import {resolve,join} from "node:path";
import {homedir} from "node:os";

const id=(process.argv[2]||"").replaceAll("-","").toLowerCase();
if(!/^[a-f0-9]{32}$/.test(id)) throw new Error("Pass the exact UUID copied from the in-game Capes screen.");
const directory=resolve(process.argv[3]||join(homedir(),".vortex","cape-invites"));
mkdirSync(directory,{recursive:true,mode:0o700});
const codePath=join(directory,id+".code.txt"),sqlPath=join(directory,id+".sql");
if(existsSync(codePath)||existsSync(sqlPath)) throw new Error("An invite file already exists; nothing was overwritten.");
const code=randomBytes(32).toString("hex"),hash=createHash("sha256").update(code).digest("hex");
writeFileSync(codePath,code+"\n",{mode:0o600,flag:"wx"});
writeFileSync(sqlPath,`INSERT INTO cape_profiles(uuid,activation_hash) VALUES('${id}','${hash}') ON CONFLICT(uuid) DO NOTHING;\n`,{mode:0o600,flag:"wx"});
console.log("Private activation file: "+codePath);
console.log("Run this SQL in the dedicated Vortex D1 console: "+sqlPath);
console.log("Only after the row is inserted, privately give the activation file to that player. Never share their owner key.");
