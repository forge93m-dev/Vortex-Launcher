using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using VortexLauncher.Core.Accounts;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Views;

public sealed class LauncherWardrobeWindow : Window
{
    private sealed record Item(string Name,string? Cape,WardrobeImage? Image,bool Skin);
    private readonly VortexSession _session;
    private readonly string _uuid;
    private readonly string? _serverUuid;
    private readonly VortexAccountClient _accounts;
    private readonly LauncherWardrobeClient _api;
    private readonly WardrobeLibrary _library;
    private readonly CancellationTokenSource _life=new();
    private readonly ListBox _items=new(){Name="wardrobeItems",MinHeight=200};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Text="Choose a saved image. Apply publishes only the selected cosmetic to your Vortex account."};
    private readonly TextBlock _selection=new(){FontSize=19,Text="Preview"};
    private readonly WardrobePreview _preview=new(){MinHeight=290,Height=340};
    private readonly CheckBox _slim=new(){Content="Slim arms",IsVisible=false};
    private readonly Button _apply=new(){Name="wardrobeApply",Content="Apply online",Classes={"PrimaryBtn"}};
    private readonly Button _refresh=new(){Content="Check online status",Classes={"SecondaryBtn"}};
    private readonly Button _import=new(){Content="Import PNG…",Classes={"SecondaryBtn"}};
    private readonly Button _skins=new(){Content="Skins",Classes={"SecondaryBtn"}};
    private readonly Button _capes=new(){Content="Capes",Classes={"SecondaryBtn"}};
    private bool _skin,_busy;
    public LauncherWardrobeWindow(VortexSession session,string uuid,VortexAccountClient accounts,LauncherWardrobeClient? api=null,string? dataRoot=null)
    {
        _session=session;_uuid=uuid;_accounts=accounts;_api=api??new();_library=new(dataRoot??PlatformSupport.DataRoot,session.Profile.Id);
        var offline=Guid.Parse(VortexLauncher.Models.AccountModel.GenerateOfflineUuid(session.Profile.Nickname));
        string serverId=Convert.ToHexString(offline.ToByteArray()).ToLowerInvariant();
        if(Guid.Parse(uuid)==offline)_serverUuid=Guid.ParseExact(serverId,"N").ToString();
        Title="Vortex Wardrobe";Width=960;Height=650;MinWidth=800;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Application.Current?.FindResource("ThemeBackgroundBrush") as IBrush??Brush.Parse("#10141A");
        var layout=new Grid{RowDefinitions=new("Auto,*,Auto"),Margin=new Thickness(24)};
        var header=new StackPanel{Spacing=5,Margin=new Thickness(0,0,0,20)};
        header.Children.Add(new TextBlock{Text="Wardrobe",FontSize=28,FontWeight=FontWeight.SemiBold});
        header.Children.Add(new TextBlock{Text=$"{session.Profile.Nickname} · Vortex skins and capes · visible to compatible Vortex clients",TextWrapping=TextWrapping.Wrap,Opacity=.7});layout.Children.Add(header);
        var body=new Grid{ColumnDefinitions=new("135,18,*,20,250")};Grid.SetRow(body,1);layout.Children.Add(body);
        var nav=new StackPanel{Spacing=10};nav.Children.Add(_capes);nav.Children.Add(_skins);nav.Children.Add(_import);nav.Children.Add(_slim);body.Children.Add(nav);
        Grid.SetColumn(_items,2);body.Children.Add(_items);
        _items.ItemTemplate=new FuncDataTemplate<Item>((item,_)=>{
            var row=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=12,Margin=new Thickness(6)};
            if(item?.Image!=null){var preview=new WardrobePreview{Width=44,Height=70,MinHeight=0,Back=!item.Skin};preview.SetImage(item.Image,item.Skin);preview.DetachedFromVisualTree+=(_,_)=>preview.Dispose();row.Children.Add(preview);}
            row.Children.Add(new TextBlock{Text=item?.Name,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});return row;
        });
        var pane=new StackPanel{Spacing=12};Grid.SetColumn(pane,4);body.Children.Add(pane);pane.Children.Add(_selection);pane.Children.Add(_preview);
        var rotate=new Button{Content="Front / Back",Classes={"SecondaryBtn"},HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};pane.Children.Add(rotate);pane.Children.Add(new TextBlock{Text="Live texture preview",Opacity=.6,FontSize=11});
        var bottom=new StackPanel{Spacing=12,Margin=new Thickness(0,16,0,0)};Grid.SetRow(bottom,2);layout.Children.Add(bottom);bottom.Children.Add(_status);
        var actions=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10};actions.Children.Add(_apply);actions.Children.Add(_refresh);bottom.Children.Add(actions);Content=layout;
        _capes.Click+=async(_,_)=>await ShowCategory(false);_skins.Click+=async(_,_)=>await ShowCategory(true);
        _items.SelectionChanged+=(_,_)=>Select();_slim.IsCheckedChanged+=(_,_)=>{_preview.Slim=_slim.IsChecked==true;_preview.InvalidateVisual();};
        rotate.Click+=(_,_)=>{_preview.Back=!_preview.Back;_preview.InvalidateVisual();};
        _import.Click+=async(_,_)=>await Import();_apply.Click+=async(_,_)=>await Apply();_refresh.Click+=async(_,_)=>await Refresh();
        Opened+=async(_,_)=>{await ShowCategory(false);await Refresh();};
        Closed+=(_,_)=>{_life.Cancel();_preview.Dispose();};WindowTitleBar.Apply(this);
    }
    private void Busy(bool busy){_busy=busy;_apply.IsEnabled=_refresh.IsEnabled=_import.IsEnabled=_skins.IsEnabled=_capes.IsEnabled=_items.IsEnabled=_slim.IsEnabled=!busy;}
    private async Task ShowCategory(bool skin)
    {
        if(_busy)return;Busy(true);_skin=skin;_slim.IsVisible=skin;_preview.Back=!skin;
        try{
            var list=await Task.Run(()=>{
                var result=new List<Item>{new(skin?"Original Minecraft skin":"Vanilla cape","none",null,skin)};
                if(!skin)foreach(var id in new[]{"vortex","aurora","ember"}){
                    using var input=AssetLoader.Open(new Uri($"avares://Vortex Launcher/Assets/Capes/{id}.png"));using var output=new MemoryStream();input.CopyTo(output);
                    result.Add(new(char.ToUpperInvariant(id[0])+id[1..],id,WardrobeImages.Decode(output.ToArray(),false),false));
                }
                foreach(var path in _library.Files(skin)){try{result.Add(new(Path.GetFileNameWithoutExtension(path),null,_library.Read(path,skin),skin));}catch{ /* Preserve unreadable files; never replace originals. */ }}
                return result;
            },_life.Token);_items.ItemsSource=list;_items.SelectedIndex=0;
        }catch(Exception e){ShowError(e);}finally{Busy(false);}
    }
    private void Select(){if(_items.SelectedItem is not Item item)return;_selection.Text=item.Name;_preview.SetImage(item.Image,_skin);_preview.Back=!_skin;_preview.Slim=_slim.IsChecked==true;_status.Text="Preview only. Apply online to publish this selection. The other cosmetic is unchanged.";}
    private async Task Import()
    {
        if(_busy)return;
        var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=_skin?"Choose a 64 × 64 skin PNG":"Choose a cape texture or picture PNG",AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("PNG image"){Patterns=new[]{"*.png"}}}});
        string? path=files.Count>0?files[0].TryGetLocalPath():null;if(path==null)return;
        Busy(true);
        try{string imported=await Task.Run(()=>_library.Import(path,_skin),_life.Token);Busy(false);await ShowCategory(_skin);_items.SelectedItem=_items.Items.OfType<Item>().FirstOrDefault(i=>i.Name==Path.GetFileNameWithoutExtension(imported));_status.Text="PNG saved to your launcher wardrobe. Preview it, then Apply online.";}
        catch(Exception e){ShowError(e);}finally{Busy(false);}
    }
    private async Task Apply()
    {
        if(_busy||_items.SelectedItem is not Item item)return;bool slim=_slim.IsChecked==true;Busy(true);_status.Text="Publishing… Waiting for server confirmation.";
        try{string access=await _accounts.WardrobeSessionAsync(_session,_uuid,_life.Token);await _api.ApplyAsync(access,_uuid,item.Skin,item.Cape,item.Cape!=null?null:item.Image,slim,_life.Token,_serverUuid);
            _status.Text="Online · Confirmed by Vortex. Restart Minecraft to load your account appearance. Other updated clients refresh within about a minute.";
        }catch(Exception e){ShowError(e);}finally{Busy(false);}
    }
    private async Task Refresh()
    {
        if(_busy)return;Busy(true);_status.Text="Checking your online wardrobe…";
        try{var p=await _api.LookupAsync(_uuid,_life.Token);_status.Text=p==null?"No online appearance yet. Choose a cosmetic and Apply online.":$"Online profile found · Cape: {(p.Cape.StartsWith("hd:")||p.Cape.StartsWith("custom:")?"Custom":p.Cape)} · Skin: {(p.Skin.Length>0?"Custom":"Original")}.";
            if(!_skin&&p!=null&&p.Skin.Length>0){var image=await _api.DownloadSkinAsync(p.Skin,_life.Token);_preview.SetImage(image,true);_preview.Slim=p.Slim;}
        }
        catch(Exception e){ShowError(e);}finally{Busy(false);}
    }
    private void ShowError(Exception e){_status.Text=e is OperationCanceledException?"Operation cancelled. Online result may be unknown; check status before retrying.":e is InvalidOperationException or IOException?e.Message:"Could not complete the wardrobe operation. Saved images were preserved. Check your connection and retry.";}
}
