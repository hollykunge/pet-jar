using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PetJar;

internal static class FillerSelectionTests
{
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    internal static void Run()
    {
        var legacy=JsonSerializer.Deserialize<Settings>("{}")!;
        Check(legacy.JarFillers.SetEquals(Enum.GetValues<FillerKind>()),"legacy settings keep the full filler mix");
        var settings=new Settings {JarPets=new() {Client.Muse}};
        settings.Executables[Client.Muse]="manual.exe";settings.QuotaSources[Client.Muse]=QuotaSource.Unavailable;
        int saved=0,changed=0;bool persist=true;
        var collection=new FillerCollection(settings,()=>{saved++;return persist;});collection.Changed+=()=>changed++;
        var shop=new FillerShowcaseWindow(collection);
        T Find<T>(string id) where T:FrameworkElement=>FindChild<T>(shop,id)??throw new InvalidOperationException("missing filler control: "+id);
        void Click(string id)=>Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Toggle(string id) {var tile=Find<ToggleButton>(id);tile.IsChecked=tile.IsChecked!=true;tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
        Check(FindChild<Button>(shop,"fillers-apply")==null,"filler showcase has no extra apply step");
        Click("fillers-clear");
        Check(saved==1&&changed==1&&settings.JarFillers.Count==0,"clear immediately saves the empty selection once");
        Toggle("fillers-select-knit");
        Check(saved==2&&changed==2&&collection.Matches(new[] {FillerKind.Knit}),"one card click immediately adds and saves its kind");
        persist=false;Toggle("fillers-select-moss");
        Check(changed==2&&collection.Matches(new[] {FillerKind.Knit})&&Find<ToggleButton>("fillers-select-moss").IsChecked==false,"save failure rolls back membership and card state");
        persist=true;Toggle("fillers-select-moss");
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        Check(restored.JarFillers.SetEquals(new[] {FillerKind.Knit,FillerKind.Moss})&&changed==3,"clicking a second kind immediately mixes and persists both");
        Check(settings.JarPets.SetEquals(new[] {Client.Muse})&&settings.Executables[Client.Muse]=="manual.exe"&&settings.QuotaSources[Client.Muse]==QuotaSource.Unavailable,"filler updates preserve pets, bindings and quota source");
        Check(!collection.SetSelection(new[] {(FillerKind)999})&&changed==3,"unknown kinds cannot be saved");
        Toggle("fillers-select-knit");
        Check(collection.Matches(new[] {FillerKind.Moss})&&Find<ToggleButton>("fillers-select-knit").IsChecked==false,"clicking an active card immediately removes its kind");
        Click("fillers-all");Check(settings.JarFillers.Count==8&&changed==5,"select all applies in one transaction");
        Click("fillers-clear");shop.Close();
        Check(settings.JarFillers.Count==0&&changed==6,"closing retains the already applied choice");
        Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!.JarFillers.Count==0,"empty choice survives restart");

        var drawing=new PhysicsJarDrawing(new[] {Client.Muse},new[] {FillerKind.Knit});
        void CheckKinds(params FillerKind[] kinds) {
            var props=drawing.Scene.Objects.Where(o=>o.Client==null).ToArray();
            Check(props.Select(o=>o.Sprite).Distinct().Order().SequenceEqual(kinds.Select(k=>k.Sprite()).Order()),"only selected kinds enter physics");
            Check(kinds.Length==0?props.Length==0:props.Length>JarPhysicsScene.FrontPropCount,"nonempty choices fill the front and rear depth slices");
            Check(drawing.Scene.Objects.Where(o=>o.Client!=null).Select(o=>o.Client!.Value).SequenceEqual(new[] {Client.Muse}),"repacking preserves the chosen pet membership");
            if(kinds.Length>0)Check(props.GroupBy(o=>o.Sprite).Max(g=>g.Count())-props.GroupBy(o=>o.Sprite).Min(g=>g.Count())<=1,"selected kinds share the filler population evenly");
        }
        CheckKinds(FillerKind.Knit);
        DepthTests.CheckScene(drawing.Scene);
        var old=drawing.Scene;var grip=old.Objects.First(o=>o.Client==null);old.Grab(grip,grip.Center);
        drawing.SetEyes(Client.Muse,new(.3,.7));drawing.SetFillers(new[] {FillerKind.Knit,FillerKind.Moss});
        Check(old.Grabbed==null&&old.Pressed==null,"repacking releases the old grab and pressure state");
        CheckKinds(FillerKind.Knit,FillerKind.Moss);
        DepthTests.CheckScene(drawing.Scene);
        Check(drawing.GetEyes(Client.Muse)==new EyePose(.3,.7),"repacking preserves eye animation pose");
        var active=drawing.Scene;drawing.SetFillers(new[] {FillerKind.Moss,FillerKind.Knit,FillerKind.Knit});
        Check(ReferenceEquals(active,drawing.Scene),"same set in another order does not rebuild physics");
        drawing.SetFillers(new[] {FillerKind.Blue,FillerKind.Chrome,FillerKind.Amber});CheckKinds(FillerKind.Blue,FillerKind.Chrome,FillerKind.Amber);
        DepthTests.CheckScene(drawing.Scene);
        Check(drawing.Scene.Objects.Where(o=>o.Client==null).All(o=>o.Softness==0),"hard fillers retain rigid material behavior");
        drawing.SetFillers(Array.Empty<FillerKind>());CheckKinds();
        var window=new PetWindow(new Settings {JarFillers=new(),JarPets=new() {Client.Muse}}) {FreezeExpression=true};
        Check(window.Drawing.Scene.Objects.Count==1,"native startup consumes the persisted empty filler choice");window.Close();
    }
    private static T? FindChild<T>(DependencyObject root,string id) where T:FrameworkElement
    {
        if(root is T candidate&&AutomationProperties.GetAutomationId(candidate)==id)return candidate;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if(FindChild<T>(child,id) is T found)return found;
        return null;
    }
}
