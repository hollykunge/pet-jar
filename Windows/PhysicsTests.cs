using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class PhysicsTests
{
    private static void Check(bool value,string reason) {if(!value)throw new InvalidOperationException(reason);}
    internal static void Render(string path,Settings? settings=null)
    {
        var drawing=new PhysicsJarDrawing(settings?.JarPets??new(Enum.GetValues<Client>()),settings?.JarFillers) {Width=260,Height=260,ShowHint=false};
        Save(drawing,path);
        File.WriteAllText(Path.ChangeExtension(path,"json"),System.Text.Json.JsonSerializer.Serialize(new {Coverage=DepthTests.Measure(drawing.Scene),Objects=drawing.Scene.Objects.Select(o=>new {o.Id,o.Depth,o.Sprite,o.Center,o.Size,Angle=o.Body.GetAngle(),o.Compression})},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
    }
    private static void Save(FrameworkElement drawing,string path)
    {
        drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));
        var bitmap=new RenderTargetBitmap(1040,1040,384,384,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    internal static void Run()
    {
        var scene=new JarPhysicsScene(Enum.GetValues<Client>());
        int propCount=scene.FillerCount;
        DepthTests.CheckScene(scene);
        Check(propCount>JarPhysicsScene.FrontPropCount&&scene.Objects.Count==propCount+4,"front and rear fillers have independent bodies");
        Check(scene.Objects.Select(o=>o.Id).Distinct().Count()==scene.Objects.Count,"body IDs are unique");
        Check(scene.ContactCount>15,"initial pile settles against other bodies and glass");
        Check(scene.Objects.Any(o=>o.Softness>=.06&&o.Compression>.004),"resting contacts keep a slight plush compression");
        Check(scene.Objects.Where(o=>o.Softness==0).All(o=>o.Squash.Transform.IsIdentity),"rigid bodies keep their material shape in a packed pile");
        var loaded=scene.Objects.Where(o=>o.Softness>=.06).MaxBy(o=>o.Compression)!;double loadedShape=loaded.Compression;
        foreach(var item in scene.Objects)item.Body.SetAwake(false);
        for(int i=0;i<120;i++)scene.Advance(1d/120);
        Check(loaded.Compression>.004&&Math.Abs(loaded.Compression-loadedShape)<.01,"sleeping contact islands retain their last solved plush pressure");
        var pet=scene.Objects.Single(o=>o.Client==Client.Grok);var before=pet.Center;
        // A nearby lift can displace a packed pile; targeting the far top-left
        // through several occupied layers would incorrectly require tunnelling.
        scene.Grab(pet,before);var target=before+new Vector(-25,-35);scene.Drag(target);
        for(int i=0;i<100;i++)scene.Advance(1d/120);
        Check((pet.Center-before).Length>18,"mouse joint moves the picked body");
        Check((pet.Center-target).Length<35,"mouse joint follows the pointer while preserving collisions");
        scene.Release();
        for(int i=0;i<300;i++)scene.Advance(1d/120);
        Check(scene.Grabbed==null,"release ends the joint in a dense pile");
        Check(scene.CollisionCount>10,"movement produces real contact events");
        scene.Grab(pet,pet.Center);scene.Drag(new(-500,900));
        for(int i=0;i<240;i++)scene.Advance(1d/120);
        scene.Release();
        for(int i=0;i<600;i++)scene.Advance(1d/120);
        foreach(var item in scene.Objects) {
            Check(double.IsFinite(item.Center.X)&&double.IsFinite(item.Center.Y),"finite solver state: "+item.Id);
            Check(JarGeometry.Interior.FillContains(item.Center),"body stays inside the actual glass: "+item.Id);
            Check(item.Compression>=-.025&&item.Compression<=item.Softness,"bounded material deformation");
        }
        scene.Grab(pet,pet.Center);scene.SetPets(new[] {Client.Claude});
        Check(scene.Grabbed==null&&scene.Objects.Count==propCount+1,"removing a grabbed pet cleans the joint and body");
        scene.SetPets(new[] {Client.Claude,Client.Claude});Check(scene.Objects.Count==propCount+1,"no duplicate physics pets");
        scene.SetPets(Array.Empty<Client>());Check(scene.Objects.Count==propCount,"props remain interactive in an empty showcase selection");
        scene.SetPets(Enum.GetValues<Client>());Check(scene.Objects.Count==propCount+4,"showcase restores independent pet bodies");
        Check(scene.Hit(new Point(-20,-20))==null,"outside glass never hits");
        var held=scene.Objects.Single(o=>o.Client==Client.Muse);scene.Press(held,held.Center);
        scene.SetPets(new[] {Client.ChatGPT,Client.Grok,Client.Claude});
        Check(scene.Pressed==null,"returning a pressed pet releases its material grip");
        scene.SetPets(Enum.GetValues<Client>());
        var random=new Random(752);
        for(int turn=0;turn<24;turn++) {
            var item=scene.Objects[random.Next(scene.Objects.Count)];scene.Grab(item,item.Center);
            scene.Drag(new(random.Next(-150,410),random.Next(-120,350)));
            for(int i=0;i<36;i++)scene.Advance(1d/120);
            scene.Release(new Vector(random.Next(-1000,1001),random.Next(-1000,1001)));
            Check(item.Body.GetLinearVelocity().Length()<=7.001,"rapid throws have a bounded release velocity");
            for(int i=0;i<90;i++)scene.Advance(1d/120);
        }
        for(int i=0;i<600;i++)scene.Advance(1d/120);
        foreach(var item in scene.Objects)Check(JarGeometry.Interior.FillContains(item.Center)&&double.IsFinite(item.Body.GetAngle()),"repeated throws stay finite and inside the glass: "+item.Id+" at "+item.Center);

        // Check unobstructed gravity/pokes separately: a filled pile can support
        // a released body immediately, so it must not be forced through others.
        var open=new JarPhysicsScene(Array.Empty<Client>());
        for(int i=0;i<600;i++)open.Advance(1d/120);
        open.SetPets(new[] {Client.Grok});var falling=open.Objects.Single(o=>o.Client==Client.Grok);double elevated=falling.Center.Y;
        for(int i=0;i<300;i++)open.Advance(1d/120);
        Check(falling.Center.Y>elevated+10,"a pet falls through available space under gravity");
        var prop=open.Objects.Where(o=>o.Client==null&&o.Depth==0).MinBy(o=>o.Center.Y)!;before=prop.Center;open.Poke(prop,before);
        for(int i=0;i<12;i++)open.Advance(1d/120);
        Check((prop.Center-before).Length>.1,"an unobstructed decoration reacts to a poke");

        var velocities=open.Objects.Select(o=>o.Body.GetLinearVelocity()).ToArray();
        open.ApplyJarVelocityChange(new Vector(100,0));
        for(int i=0;i<open.Objects.Count;i++) {
            var delta=open.Objects[i].Body.GetLinearVelocity()-velocities[i];
            Check(Math.Abs(delta.X+2)<.001&&Math.Abs(delta.Y)<.001,"frame inertia has the same acceleration for every mass");
        }
        double oldX=open.Objects.Average(o=>o.Center.X);
        for(int i=0;i<6;i++)open.Advance(1d/120);
        Check(open.Objects.Average(o=>o.Center.X)<oldX-.1,"objects lag behind rightward jar motion");
        double oldY=open.Objects.Average(o=>o.Center.Y);open.ApplyJarVelocityChange(new Vector(0,160));
        for(int i=0;i<12;i++)open.Advance(1d/120);
        Check(open.Objects.Average(o=>o.Center.Y)<oldY-.3,"a downward jar movement lifts objects relative to the glass");

        var rest=scene.Objects.ToDictionary(o=>o,o=>o.Center);var movement=new JarMotion();movement.Begin(new(0,0),0);
        Point jarPosition=default;
        for(int i=1;i<=240;i++) {
            jarPosition=new(85*Math.Sin(i*.13),35*Math.Sin(i*.19));
            scene.ApplyJarVelocityChange(movement.Sample(jarPosition,i/120d));scene.Advance(1d/120);
        }
        movement.End();
        for(int i=241;i<=600;i++) {scene.ApplyJarVelocityChange(movement.Sample(jarPosition,i/120d));scene.Advance(1d/120);}
        Check(scene.Objects.Any(o=>o.Client!=null&&(o.Center-rest[o]).Length>1)&&scene.Objects.Count(o=>o.Client==null&&(o.Center-rest[o]).Length>1)>8,"shaking rearranges both pets and decorations");
        foreach(var item in scene.Objects)Check(JarGeometry.Interior.FillContains(item.Center)&&double.IsFinite(item.Body.GetAngle()),"repeated jar shaking stays inside the glass: "+item.Id);

        var drawing=new PhysicsJarDrawing(Enum.GetValues<Client>()) {Width=260,Height=260,ShowHint=false};
        drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));
        byte[] Capture() {
            var bitmap=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);bitmap.Render(drawing);
            var pixels=new byte[520*520*4];bitmap.CopyPixels(pixels,520*4,0);return pixels;
        }
        foreach(var client in Enum.GetValues<Client>()) {
            var body=drawing.Scene.Objects.Single(o=>o.Client==client);
            var bounds=body.Bounds;bounds.Inflate(1,1);var original=Capture();drawing.SetEyes(client,new(1,1));var changed=Capture();int difference=0;
            for(int i=0;i<original.Length;i+=4)if(!original.AsSpan(i,4).SequenceEqual(changed.AsSpan(i,4))) {
                difference++;var p=new Point((i/4%520)/2d,(i/4/520)/2d);
                Check(bounds.Contains(p),"eyes share the moving body's transform: "+client);
            }
            Check(difference>8,"standalone eyes visibly animate: "+client);drawing.SetEyes(client,default);
        }
        var visible=drawing.Scene.Objects.Where(o=>o.Client!=null).First(o=>drawing.Scene.Hit(o.Center)==o);
        before=visible.Center;drawing.Scene.Grab(visible,before);drawing.Scene.Drag(new(120,45));
        for(int i=0;i<90;i++)drawing.Scene.Advance(1d/120);drawing.UpdatePoses();
        Check((visible.Center-before).Length>10&&visible.Contains(visible.Center),"render transform and alpha hit move with the body");
        drawing.Scene.Release();
    }
}
