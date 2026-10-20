using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace VortexLauncher.Core.Accounts;

public sealed record WardrobeImage(int Width, int Height, byte[] Rgba, byte[] Png);

public static class WardrobeImages
{
    private static readonly object DecodeLock = new();
    public static WardrobeImage Decode(byte[] bytes, bool skin)
    {
        if (bytes.Length < 33 || bytes.Length > (skin ? 1024*1024 : 8*1024*1024) ||
            !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
            throw new InvalidOperationException("Choose a PNG image, up to 1 MB for skins or 8 MB for capes.");
        lock (DecodeLock)
        {
            using var data=SKData.CreateCopy(bytes);
            using var codec=SKCodec.Create(data) ?? throw new InvalidOperationException("This PNG could not be read.");
            var info=codec.Info;
            if (info.Width<1 || info.Height<1 || info.Width>4096 || info.Height>4096 || (long)info.Width*info.Height>8_388_608)
                throw new InvalidOperationException("Choose an image up to 4096 pixels per side and 8 megapixels.");
            if (skin && (info.Width!=64 || info.Height!=64)) throw new InvalidOperationException("Minecraft skins must be 64 × 64 pixels.");
            using var bitmap=new SKBitmap(new SKImageInfo(info.Width,info.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
            if (codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success) throw new InvalidOperationException("This PNG is incomplete or damaged.");
            if (skin)
            {
                foreach(var box in new[]{(0,0,32,16),(0,16,64,32),(16,48,48,64)})
                    for(int y=box.Item2;y<box.Item4;y++)for(int x=box.Item1;x<box.Item3;x++){var c=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,c.WithAlpha(255));}
            }
            if (!skin && !CapeSize(bitmap.Width,bitmap.Height))
            {
                // Fit a photo onto both cape faces; no destructive crop or source overwrite.
                using var atlas=new SKBitmap(new SKImageInfo(1024,512,SKColorType.Rgba8888,SKAlphaType.Unpremul));
                using var canvas=new SKCanvas(atlas);canvas.Clear(new SKColor(16,18,24));
                float scale=Math.Min(160f/bitmap.Width,256f/bitmap.Height),w=bitmap.Width*scale,h=bitmap.Height*scale;
                using var paint=new SKPaint{FilterQuality=SKFilterQuality.High};
                foreach(int x in new[]{16,192})canvas.DrawBitmap(bitmap,new SKRect(x+(160-w)/2,16+(256-h)/2,x+(160+w)/2,16+(256+h)/2),paint);
                return Snapshot(atlas);
            }
            return Snapshot(bitmap);
        }
    }
    private static WardrobeImage Snapshot(SKBitmap bitmap)
    {
        var pixels=new byte[bitmap.Width*bitmap.Height*4];Marshal.Copy(bitmap.GetPixels(),pixels,0,pixels.Length);
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        return new(bitmap.Width,bitmap.Height,pixels,png.ToArray());
    }
    public static bool CapeSize(int w,int h)=>(w is 64 or 128 or 256 or 512 or 1024)&&h==w/2;
    public static WardrobeImage SkinFromRaw(byte[] rgba)
    {
        if(rgba.Length!=16384)throw new InvalidOperationException("Unexpected skin texture size.");
        using var bitmap=new SKBitmap(new SKImageInfo(64,64,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        Marshal.Copy(rgba,0,bitmap.GetPixels(),rgba.Length);return Snapshot(bitmap);
    }
    public static byte[] CapePayload(WardrobeImage image)
    {
        if(!CapeSize(image.Width,image.Height) || image.Rgba.Length!=image.Width*image.Height*4)throw new InvalidOperationException("Invalid cape dimensions.");
        if(image.Width==64)return (byte[])image.Rgba.Clone();
        using var output=new MemoryStream();
        Span<byte> header=stackalloc byte[12];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header,0x56435032);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header[4..],image.Width);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header[8..],image.Height);output.Write(header);
        using(var zipped=new ZLibStream(output,CompressionLevel.Optimal,true))zipped.Write(image.Rgba);
        if(output.Length>655360)throw new InvalidOperationException("This cape contains too much image detail. Choose a smaller texture.");
        return output.ToArray();
    }
}
