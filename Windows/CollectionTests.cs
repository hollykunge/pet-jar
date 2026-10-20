using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class CollectionTests
{
    private static void Check(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    internal static void Run()
    {
        var legacy=JsonSerializer.Deserialize<Settings>("{\"Left\":50}")!;
        Check(legacy.JarPets.SetEquals(Enum.GetValues<Client>()),"old settings keep the original four pets");
        var settings=new Settings();settings.Executables[Client.Claude]="bound.exe";
        int saved=0,changed=0;bool persist=true;
        var collection=new PetCollection(settings,()=> {saved++;return persist;});collection.Changed+=()=>changed++;
        Check(collection.Update(Array.Empty<Client>(),new[] {Client.Claude}),"return to showcase");
        Check(!collection.Contains(Client.Claude)&&collection.Count==3&&changed==1&&saved==1,"membership changes and saves exactly once");
        Check(settings.Executables[Client.Claude]=="bound.exe","moving a pet preserves its client binding");
        Check(collection.Update(new[] {Client.Claude,Client.Claude},Array.Empty<Client>())&&collection.Count==4,"adding duplicates still adds one pet");
        persist=false;Check(!collection.Update(Array.Empty<Client>(),new[] {Client.Grok})&&collection.Contains(Client.Grok)&&changed==2,"failed save restores membership without notification");persist=true;
        Check(collection.Update(Array.Empty<Client>(),Enum.GetValues<Client>())&&collection.Count==0,"every pet can return to the showcase");
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        Check(restored.JarPets.Count==0&&restored.Executables[Client.Claude]=="bound.exe","empty collection persists independently of bindings");
        Check(!collection.Update(new[] {(Client)999},Array.Empty<Client>()),"unknown pets cannot enter the collection");

        var shop=new PetShowcaseWindow(collection);
        var muse=FindTile(shop,"showcase-select-muse")!;
        void Toggle(ToggleButton tile) {tile.IsChecked=tile.IsChecked!=true;tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
        int beforeClick=changed;
        Toggle(muse);Check(collection.Contains(Client.Muse)&&muse.IsChecked==true&&changed==beforeClick+1,"one showcase click immediately adds the pet");
        Toggle(muse);Check(!collection.Contains(Client.Muse)&&muse.IsChecked==false&&changed==beforeClick+2,"clicking the active pet immediately returns it");
        persist=false;Toggle(muse);Check(!collection.Contains(Client.Muse)&&muse.IsChecked==false&&changed==beforeClick+2,"pet save failure restores the card without changing the jar");persist=true;
        Toggle(muse);shop.Close();
        Check(collection.Contains(Client.Muse)&&settings.Executables[Client.Claude]=="bound.exe","closing the showcase retains the immediate change and client bindings");

        var drawing=new JarDrawing {Width=260,Height=260,ShowHint=false};
        drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));
        byte[] Capture()
        {
            var bitmap=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);bitmap.Render(drawing);
            var pixels=new byte[520*520*4];bitmap.CopyPixels(pixels,520*4,0);return pixels;
        }
        var full=Capture();
        foreach(var client in Enum.GetValues<Client>())
        {
            drawing.SetPets(Enum.GetValues<Client>().Where(c=>c!=client));
            var missing=Capture();int eyeChanges=0;
            foreach(var eye in JarGeometry.Eyes(client))
                for(int y=(int)(eye.Top*2);y<(int)(eye.Bottom*2);y++)
                    for(int x=(int)(eye.Left*2);x<(int)(eye.Right*2);x++)
                        if(!full.AsSpan((y*520+x)*4,4).SequenceEqual(missing.AsSpan((y*520+x)*4,4)))eyeChanges++;
            Check(eyeChanges>20,"removed body and eyes disappear: "+client);
            drawing.SetEyes(client,new(.5,.8));Check(Capture().AsSpan().SequenceEqual(missing),"removed eyes do not animate: "+client);
            drawing.SetEyes(client,default);drawing.SetPets(Enum.GetValues<Client>());
            Check(Capture().AsSpan().SequenceEqual(full),"adding back restores the original pixels: "+client);
        }
        var emptySettings=new Settings {JarPets=new()};
        var window=new PetWindow(emptySettings) {FreezeExpression=true};
        foreach(var point in new[] {new Point(99,63),new Point(180,115),new Point(101,161),new Point(182,181)})
            Check(window.HitPet(point)==null,"removed pets cannot receive a mouse click");
        window.SetHover(Client.Grok);
        Check(window.Drawing.Hovered==null,"removed pets cannot display quota");
        emptySettings.JarPets.Add(Client.Claude);window.RefreshPets();
        var added=window.Drawing.Scene.Objects.Single(o=>o.Client==Client.Claude);
        Check(window.HitPet(added.Center)==Client.Claude,"added pet uses its current body hit region");window.Close();
    }
    private static ToggleButton? FindTile(DependencyObject root,string id)
    {
        if(root is ToggleButton tile&&AutomationProperties.GetAutomationId(tile)==id)return tile;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())if(FindTile(child,id) is ToggleButton found)return found;
        return null;
    }
}
