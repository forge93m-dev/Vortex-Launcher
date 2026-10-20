using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Accounts;

public sealed record OnlineWardrobe(string Cape,string Skin,bool Slim);

public sealed class LauncherWardrobeClient
{
    private static readonly Uri Origin=new("https://vortex-accounts.forge93-m.workers.dev/v3/wardrobe/");
    private static readonly HttpClient Shared=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(20)};
    private readonly HttpClient _http;
    private readonly Func<string,string?> _owner;
    public LauncherWardrobeClient():this(Shared){}
    internal LauncherWardrobeClient(HttpClient http,Func<string,string?>? owner=null){_http=http;_owner=owner??LegacyOwner;}
    public async Task<OnlineWardrobe?> LookupAsync(string uuid,CancellationToken token)
    {
        string id=Guid.Parse(uuid).ToString("N");
        using var doc=await RequestAsync("lookup",HttpMethod.Post,new{uuids=new[]{id}},null,token);
        var profiles=doc.RootElement.GetProperty("profiles");
        if(profiles.GetArrayLength()>1)throw new InvalidOperationException("Unexpected wardrobe response.");
        if(profiles.GetArrayLength()==0)return null;
        var row=profiles[0];if(row.GetProperty("uuid").GetString()!=id)throw new InvalidOperationException("The wardrobe returned a different player.");
        return new(row.GetProperty("cape").GetString()??"none",row.GetProperty("skin_hash").GetString()??"",row.GetProperty("slim").GetInt32()==1);
    }
    public async Task<WardrobeImage> DownloadSkinAsync(string hash,CancellationToken token)
    {
        if(hash.Length!=64 || hash.Any(c=>!"0123456789abcdef".Contains(c)))throw new InvalidOperationException("Invalid skin checksum.");
        using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(Origin,"textures/"+hash));
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("The saved skin preview is unavailable. Retry later.");
        using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[4096];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+read>16384)throw new InvalidOperationException("Unexpected skin texture size.");bytes.Write(buffer,0,read);}
        byte[] pixels=bytes.ToArray();if(Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant()!=hash)throw new InvalidOperationException("Skin texture checksum mismatch.");
        return WardrobeImages.SkinFromRaw(pixels);
    }
    public async Task ApplyAsync(string access,string uuid,bool skin,string? builtIn,WardrobeImage? image,bool slim,CancellationToken token,string? alsoUuid=null)
    {
        string id=Guid.Parse(uuid).ToString("N");
        if(!VortexAccountClient.ValidToken(access))throw new InvalidOperationException("Sign in to Vortex again.");
        if(!skin && image==null && builtIn is not ("none" or "vortex" or "aurora" or "ember"))throw new InvalidOperationException("Choose a valid cape.");
        if(skin && image!=null && (image.Width!=64 || image.Height!=64 || image.Rgba.Length!=16384))throw new InvalidOperationException("Invalid skin dimensions.");
        byte[]? pixels=skin?image?.Rgba:image==null?null:WardrobeImages.CapePayload(image);
        object Body(string target)=>skin?new{uuid=target,enabled=image!=null,slim,pixels=pixels==null?null:Convert.ToBase64String(pixels),legacy_owner=_owner(target)}:
            new{uuid=target,cape=builtIn,pixels=pixels==null?null:Convert.ToBase64String(pixels),legacy_owner=_owner(target)};
        // Omit absent pixels: the server distinguishes a bundled cape from an uploaded texture.
        string expected=pixels==null?(skin?"":builtIn!):Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        if(!skin && pixels!=null)expected=(pixels.Length==8192?"custom:":"hd:")+expected;
        var targets=alsoUuid==null?new[]{id}:new[]{id,Guid.Parse(alsoUuid).ToString("N")}.Distinct().ToArray();
        foreach(string target in targets){
            using var doc=await RequestAsync(skin?"skin":"cape",HttpMethod.Put,Body(target),access,token);
            if(!doc.RootElement.TryGetProperty("saved",out var saved)||saved.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("The server did not confirm this change. Check online status before retrying.");
            var found=await LookupAsync(target,token);
            if(found==null || (skin?found.Skin!=expected || image!=null&&found.Slim!=slim:found.Cape!=expected))
                throw new InvalidOperationException("The save was accepted, but the latest appearance could not be confirmed. Refresh before retrying.");
        }
    }
    private static string? LegacyOwner(string id)
    {
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".vortex","cape-keys.json");
        if(!File.Exists(file))return null;
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(file)!);dir!=null;dir=dir.Parent)
            if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Legacy cape key storage is linked. Use the existing in-game wardrobe for migration.");
        if(new FileInfo(file).Length>65536 || (File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Legacy cape key storage could not be safely read.");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(file));
        return doc.RootElement.TryGetProperty(id,out var key)&&VortexAccountClient.ValidToken(key.GetString())?key.GetString():null;
    }
    private async Task<JsonDocument> RequestAsync(string route,HttpMethod method,object body,string? access,CancellationToken token)
    {
        using var request=new HttpRequestMessage(method,new Uri(Origin,route)){Content=JsonContent.Create(body,options:new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull})};
        if(access!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[4096];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+count>16384)throw new InvalidOperationException("Unexpected wardrobe response size.");bytes.Write(buffer,0,count);}
        JsonDocument doc;try{doc=JsonDocument.Parse(bytes.ToArray());}catch(JsonException){throw new InvalidOperationException("The wardrobe service returned an invalid response.");}
        if(response.IsSuccessStatusCode)return doc;
        string code=doc.RootElement.TryGetProperty("error",out var e)?e.GetString()??"":"";doc.Dispose();
        throw new InvalidOperationException(code switch{
            "sign_in_required"=>"Wardrobe access expired. Close this window, sign in again and retry.",
            "legacy_proof_required" or "profile_owned"=>"This appearance belongs to a protected profile. Use the original computer's in-game wardrobe to migrate it; no ownership was changed.",
            "use_vortex_nickname"=>"Select Use nickname for Minecraft in Profiles before publishing.",
            "rate_limited"=>"Too many changes. Wait a minute before retrying.",
            _=>"The online wardrobe could not confirm this change. Your saved images are unchanged."});
    }
}
