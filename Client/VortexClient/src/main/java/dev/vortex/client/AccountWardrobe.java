package dev.vortex.client;

import com.google.gson.*;
import java.net.URI;
import java.net.http.*;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.TimeUnit;

/** Only a short-lived cosmetics capability enters Minecraft, never the launcher login session. */
public final class AccountWardrobe {
    private static final String ORIGIN="https://vortex-accounts.forge93-m.workers.dev/v3/wardrobe/";
    private static final String TOKEN=System.getenv("VORTEX_WARDROBE_TOKEN");
    private static final HttpClient HTTP=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(4)).followRedirects(HttpClient.Redirect.NEVER).build();
    private static final Gson JSON=new Gson();
    public record Profile(String cape,String skin,boolean slim){}
    public static boolean connected(){return CapeConnection.secret(TOKEN);}
    private static byte[] request(String route,String method,Object value,boolean authenticated,int limit)throws Exception{
        var builder=HttpRequest.newBuilder(URI.create(ORIGIN+route)).timeout(Duration.ofSeconds(15));
        if(authenticated){if(!connected())throw new IllegalStateException("Sign in to Vortex in the launcher, then launch Minecraft again.");builder.header("Authorization","Bearer "+TOKEN);}
        if(value!=null)builder.header("Content-Type","application/json").method(method,HttpRequest.BodyPublishers.ofString(JSON.toJson(value)));else builder.GET();
        var pending=HTTP.sendAsync(builder.build(),info->new PartyConnection.BoundedBody(limit));HttpResponse<byte[]> response;
        try{response=pending.get(17,TimeUnit.SECONDS);}catch(Exception error){pending.cancel(true);throw error;}
        if(response.statusCode()!=200){
            String code="";try{var parsed=JSON.fromJson(new String(response.body(),java.nio.charset.StandardCharsets.UTF_8),JsonObject.class);code=parsed.get("error").getAsString();}catch(Exception ignored){}
            throw new IllegalStateException(switch(code){
                case "use_vortex_nickname" -> "Select Use nickname for Minecraft in launcher Profiles, then launch again. Other local names are not account-owned.";
                case "legacy_proof_required","profile_owned" -> "This player profile is already protected. Its saved owner key is required; no code is needed for a new Vortex nickname.";
                case "sign_in_required" -> "Wardrobe access expired. Sign in to Vortex and launch Minecraft again.";
                case "rate_limited" -> "Too many wardrobe changes. Wait a minute and retry.";
                default -> "Online wardrobe unavailable. Your local selection was preserved.";
            });
        }
        return response.body();
    }
    private static void save(String route,UUID player,Map<String,Object> values)throws Exception{
        values.put("uuid",CapeConnection.id(player));String owner=CapeKeys.local().get(player);if(!owner.isEmpty())values.put("legacy_owner",owner);
        var response=JSON.fromJson(new String(request(route,"PUT",values,true,16384),java.nio.charset.StandardCharsets.UTF_8),JsonObject.class);
        if(response==null || !response.has("saved") || !response.get("saved").getAsBoolean())throw new IllegalStateException("No wardrobe save confirmation. Retry later.");
    }
    public static void cape(UUID player,String cape,byte[] pixels)throws Exception{
        var value=new HashMap<String,Object>();if(pixels!=null)value.put("pixels",Base64.getEncoder().encodeToString(pixels));else value.put("cape",cape);save("cape",player,value);
    }
    public static void skin(UUID player,SkinPixels.Selection selection)throws Exception{
        var value=new HashMap<String,Object>();value.put("enabled",selection!=null && selection.enabled());
        if(selection!=null && selection.enabled()){value.put("pixels",Base64.getEncoder().encodeToString(selection.rgba()));value.put("slim",selection.slim());}save("skin",player,value);
    }
    public static Map<UUID,Profile> lookup(List<UUID> ids)throws Exception{
        if(ids.size()>32)throw new IllegalArgumentException("Too many wardrobe profiles.");
        var response=JSON.fromJson(new String(request("lookup","POST",Map.of("uuids",ids.stream().map(CapeConnection::id).toList()),false,16384),java.nio.charset.StandardCharsets.UTF_8),JsonObject.class);
        if(response==null || !response.has("profiles") || response.getAsJsonArray("profiles").size()>32)throw new IllegalArgumentException("Invalid wardrobe response.");
        var found=new HashMap<UUID,Profile>();for(var element:response.getAsJsonArray("profiles")){
            var row=element.getAsJsonObject();UUID player=CapeConnection.uuid(row.get("uuid").getAsString());String cape=row.get("cape").getAsString();
            String skin=row.get("skin_hash").isJsonNull()?"":row.get("skin_hash").getAsString();
            if(!ids.contains(player) || !CapeCatalog.valid(cape) || !skin.isEmpty() && !skin.matches("[a-f0-9]{64}") || found.containsKey(player))throw new IllegalArgumentException("Invalid wardrobe profile.");
            found.put(player,new Profile(cape,skin,row.get("slim").getAsInt()==1));
        }return Map.copyOf(found);
    }
    public static byte[] texture(String hash,int max)throws Exception{
        if(!hash.matches("[a-f0-9]{64}"))throw new IllegalArgumentException("Invalid wardrobe texture.");
        byte[] bytes=request("textures/"+hash,"GET",null,false,max);
        if(!java.util.HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(bytes)).equals(hash))throw new IllegalArgumentException("Wardrobe checksum mismatch.");return bytes;
    }
}
