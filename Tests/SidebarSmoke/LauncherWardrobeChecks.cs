using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SkiaSharp;
using VortexLauncher.Core.Accounts;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;
using VortexLauncher.Views;

internal static class LauncherWardrobeChecks
{
    public static void Run(Action<int> pump,string output)
    {
        string root=Directory.CreateDirectory(Path.Combine(output,"launcher-wardrobe")).FullName;
        using var bitmap=new SKBitmap(new SKImageInfo(64,64,SKColorType.Rgba8888,SKAlphaType.Unpremul));bitmap.Erase(new SKColor(80,150,180,70));
        using var skImage=SKImage.FromBitmap(bitmap);using var encoded=skImage.Encode(SKEncodedImageFormat.Png,100);byte[] png=encoded.ToArray();
        var skin=WardrobeImages.Decode(png,true);Check(skin.Rgba.Length==16384&&skin.Rgba[(8*64+8)*4+3]==255,"skin base opacity matches Java importer");
        var cape=WardrobeImages.Decode(png,false);Check(cape.Width==1024&&cape.Height==512,"photo import fits both HD cape faces without modifying source");
        var payload=WardrobeImages.CapePayload(cape);Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(payload)==0x56435032,"HD cape header matches Java protocol");
        using(var input=new MemoryStream(payload,12,payload.Length-12))using(var zip=new System.IO.Compression.ZLibStream(input,System.IO.Compression.CompressionMode.Decompress))using(var decoded=new MemoryStream()){zip.CopyTo(decoded);Check(decoded.ToArray().SequenceEqual(cape.Rgba),"HD zlib pixels round trip exactly");}
        bool rejected=false;try{WardrobeImages.Decode(new byte[35],true);}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid images rejected before native decoding");
        string source=Path.Combine(root,"original.png");File.WriteAllBytes(source,png);string id="11111111-1111-4111-8111-111111111111";
        var library=new WardrobeLibrary(root,id);string saved=library.Import(source,true);Check(File.ReadAllBytes(source).SequenceEqual(png),"source PNG unchanged");
        Check(library.Import(source,true)==saved&&library.Files(true).Count==1,"imports saved and deduplicated");
        Check(new WardrobeLibrary(root,"22222222-2222-4222-8222-222222222222").Files(true).Count==0,"saved images isolated by account ID");
        var fixture=new Fixture();using var transport=new HttpClient(fixture);var api=new LauncherWardrobeClient(transport,_=>null);string uuid=AccountModel.GenerateOfflineUuid("FixturePlayer");
        string canonical=Convert.ToHexString(Guid.Parse(uuid).ToByteArray()).ToLowerInvariant();
        api.ApplyAsync(new string('a',64),uuid,false,"aurora",null,false,default,canonical).GetAwaiter().GetResult();
        Check(fixture.SavedProfiles.SetEquals(new[]{Guid.Parse(uuid).ToString("N"),canonical}),"launcher and Java offline-server aliases both receive confirmed publication");
        api.ApplyAsync(new string('a',64),uuid,true,null,skin,true,default).GetAwaiter().GetResult();
        var downloaded=api.DownloadSkinAsync(Convert.ToHexString(SHA256.HashData(skin.Rgba)).ToLowerInvariant(),default).GetAwaiter().GetResult();Check(downloaded.Rgba.SequenceEqual(skin.Rgba),"online skin preview verifies checksum and preserves pixels");
        api.ApplyAsync(new string('a',64),uuid,false,null,cape,false,default).GetAwaiter().GetResult();
        api.ApplyAsync(new string('a',64),uuid,true,"none",null,false,default).GetAwaiter().GetResult();
        var found=api.LookupAsync(uuid,default).GetAwaiter().GetResult();Check(found?.Skin==""&&found.Cape.StartsWith("hd:"),"skin removal preserves published cape");
        using var failHttp=new HttpClient(new Fixture{FailSave=true});var badApi=new LauncherWardrobeClient(failHttp,_=>null);
        rejected=false;try{badApi.ApplyAsync(new string('a',64),uuid,false,"ember",null,false,default).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Check(rejected,"failed online save is not reported as success");
        var session=new VortexSession(new(id,"fixture","FixturePlayer"),new string('b',64),DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds());
        var window=new LauncherWardrobeWindow(session,uuid,new VortexAccountClient(transport),api,root);window.Show();
        var items=window.GetVisualDescendants().OfType<ListBox>().Single(b=>b.Name=="wardrobeItems");
        var apply=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="wardrobeApply");
        for(int i=0;i<80&&(items.ItemCount<4||!apply.IsEnabled);i++)pump(25);
        Check(items.ItemCount>=4,"bundled cape resources and saved gallery load");items.SelectedIndex=1;pump(100);
        foreach(var size in new[]{new Avalonia.Size(800,650),new Avalonia.Size(1100,760)}){window.Width=size.Width;window.Height=size.Height;pump(100);Check(window.GetVisualDescendants().OfType<WardrobePreview>().Any(p=>p.IsEffectivelyVisible&&p.Bounds.Height>200),"actual UV preview visible at "+size);window.CaptureRenderedFrame()!.Save(Path.Combine(output,$"launcher-wardrobe-{size.Width}.png"));}window.Close();pump(40);
        var instance=new InstanceModel{InstancePath=root};Directory.CreateDirectory(Path.Combine(root,"screenshots"));File.WriteAllBytes(Path.Combine(root,"screenshots","real.png"),png);instance.RefreshScreenshotCover();Check(instance.DisplayCoverPath.EndsWith("real.png"),"real instance screenshots used as automatic covers");instance.CoverImagePath="chosen.png";Check(instance.DisplayCoverPath=="chosen.png","manual cover has precedence");
        var plist=File.ReadAllText("Installer/macOS/Info.plist");Check(plist.Contains("NSMicrophoneUsageDescription"),"macOS bundle declares microphone purpose");
        string mods=Directory.CreateDirectory(Path.Combine(root,"mods")).FullName;File.WriteAllBytes(Path.Combine(mods,"voicechat-test.jar.disabled"),Array.Empty<byte>());Check(!MacMicrophonePermission.HasVoiceChat(root),"disabled voice chat does not request microphone");File.WriteAllBytes(Path.Combine(mods,"voicechat-test.jar"),Array.Empty<byte>());Check(MacMicrophonePermission.HasVoiceChat(root),"voice chat launch opts into microphone request");
        Console.WriteLine("LAUNCHER_WARDROBE_OK (fixture HTTP only; no live account changes)");
    }
    private sealed class Fixture:HttpMessageHandler
    {
        public bool FailSave;public HashSet<string> SavedProfiles=new();private string _cape="none",_skin="",_id="";private bool _slim;private byte[] _skinPixels=Array.Empty<byte>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=new ByteArrayContent(_skinPixels)};
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var b=body.RootElement;
            if(request.RequestUri!.AbsolutePath.EndsWith("lookup")){_id=b.GetProperty("uuids")[0].GetString()!;return Json(new{profiles=new[]{new{uuid=_id,cape=_cape,skin_hash=_skin.Length==0?null:_skin,slim=_slim?1:0}}});}
            Check(request.Headers.Authorization?.Scheme=="Bearer","cosmetic capability sent in authorization header only");
            if(FailSave)return Json(new{error="profile_owned"},HttpStatusCode.Forbidden);
            SavedProfiles.Add(b.GetProperty("uuid").GetString()!);
            if(request.RequestUri.AbsolutePath.EndsWith("/skin")){_skinPixels=b.GetProperty("enabled").GetBoolean()?Convert.FromBase64String(b.GetProperty("pixels").GetString()!):Array.Empty<byte>();_skin=_skinPixels.Length>0?Hash(_skinPixels):"";_slim=b.GetProperty("slim").GetBoolean();}
            else if(b.TryGetProperty("pixels",out var bytes)){var pixels=Convert.FromBase64String(bytes.GetString()!);_cape=(pixels.Length==8192?"custom:":"hd:")+Hash(pixels);}
            else {_cape=b.GetProperty("cape").GetString()!;Check(_cape is "none" or "vortex" or "aurora" or "ember","bundled capes use named selection without null pixels");}
            return Json(new{saved=true});
        }
        private static string Hash(byte[] p)=>Convert.ToHexString(SHA256.HashData(p)).ToLowerInvariant();
        private static HttpResponseMessage Json(object body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(JsonSerializer.Serialize(body))};
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("WARDROBE: "+message);Console.WriteLine("PASS "+message);}
}
