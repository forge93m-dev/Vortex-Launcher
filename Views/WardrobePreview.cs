using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VortexLauncher.Core.Accounts;

namespace VortexLauncher.Views;

// Actual skin UVs, not a web view or a decorative stock avatar. Static rendering costs no idle frames.
public sealed class WardrobePreview : Control,IDisposable
{
    public WardrobePreview()=>RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.None);
    private Bitmap? _skin,_cape;
    public bool Back {get;set;}
    public bool Slim {get;set;}
    public void SetImage(WardrobeImage? image,bool skin)
    {
        var bitmap=image==null?null:new Bitmap(new MemoryStream(image.Png));
        if(skin){_skin?.Dispose();_skin=bitmap;}else{_cape?.Dispose();_cape=bitmap;}
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double scale=Math.Min(Bounds.Width*.8/16,Bounds.Height*.85/32);if(scale<=0)return;
        double cx=(Bounds.Width-16*scale)/2,y=(Bounds.Height-32*scale)/2;
        var baseBrush=Brush.Parse("#84908D");
        void Part(int u,int v,int w,int h,double x,double dy,double dw=-1)
        {
            var dest=new Rect(cx+x*scale,y+dy*scale,(dw<0?w:dw)*scale,h*scale);
            if(_skin!=null)context.DrawImage(_skin,new Rect(u,v,w,h),dest);else context.DrawRectangle(baseBrush,null,dest);
        }
        Part(Back?24:8,8,8,8,4,0);Part(Back?56:40,8,8,8,4,0);
        Part(Back?32:20,20,8,12,4,8);Part(Back?32:20,36,8,12,4,8);
        int arm=Slim?3:4;
        Part(Back?52:44,20,arm,12,4-arm,8);Part(Back?52:44,36,arm,12,4-arm,8);
        Part(Back?44:36,52,arm,12,12,8);Part(Back?60:52,52,arm,12,12,8);
        Part(Back?12:4,20,4,12,4,20);Part(Back?12:4,36,4,12,4,20);
        Part(Back?28:20,52,4,12,8,20);Part(Back?12:4,52,4,12,8,20);
        if(Back&&_cape!=null){double s=_cape.PixelSize.Width/64d;context.DrawImage(_cape,new Rect(12*s,s,10*s,16*s),new Rect(cx+3*scale,y+8*scale,10*scale,16*scale));}
    }
    public void Dispose(){_skin?.Dispose();_cape?.Dispose();_skin=_cape=null;}
}
