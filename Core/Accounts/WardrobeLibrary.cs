using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace VortexLauncher.Core.Accounts;

public sealed class WardrobeLibrary
{
    private readonly string _root;
    public WardrobeLibrary(string dataRoot,string accountId)=>_root=Path.Combine(dataRoot,"wardrobe",Guid.Parse(accountId).ToString("N"));
    private string Folder(bool skin)
    {
        string dir=Path.Combine(_root,skin?"skins":"capes");
        for(var p=new DirectoryInfo(dir);p!=null;p=p.Parent)
            if(p.Exists && (p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked wardrobe folders are not supported.");
        return dir;
    }
    public IReadOnlyList<string> Files(bool skin)
    {
        var dir=Folder(skin);if(!Directory.Exists(dir))return Array.Empty<string>();
        return Directory.EnumerateFiles(dir,"*.png").Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0)
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(32).ToArray();
    }
    public WardrobeImage Read(string path,bool skin)
    {
        if(!File.Exists(path)||new FileInfo(path).Length>(skin?1024*1024:8*1024*1024))throw new InvalidOperationException("This PNG is missing or too large.");
        return WardrobeImages.Decode(File.ReadAllBytes(path),skin);
    }
    public string Import(string source,bool skin)
    {
        var image=Read(source,skin);string dir=Folder(skin);
        string hash=Convert.ToHexString(SHA256.HashData(image.Png)).ToLowerInvariant();
        var existing=Files(skin).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p).EndsWith(hash[..12],StringComparison.Ordinal));
        if(existing!=null)return existing;
        if(Files(skin).Count>=32)throw new InvalidOperationException("This wardrobe already contains 32 saved images in this category.");
        Directory.CreateDirectory(dir);
        string name=new string(Path.GetFileNameWithoutExtension(source).Where(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_').Take(32).ToArray());
        string target=Path.Combine(dir,(name.Length==0?"Imported":name)+"-"+hash[..12]+".png"),temporary=Path.Combine(dir,"import-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllBytes(temporary,image.Png);File.Move(temporary,target,false);return target;}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
