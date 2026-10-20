using System;
using System.Linq;
using System.Windows;

namespace PetJar;

internal static class DepthTests
{
    internal sealed record Coverage(double Front,double Filled,double NearMouth,int RearBodies);
    private static void Check(bool value,string reason) {if(!value)throw new InvalidOperationException(reason);}
    internal static Coverage Measure(JarPhysicsScene scene)
    {
        int samples=0,filled=0,front=0,mouth=0,mouthFilled=0;
        var shapes=scene.Objects.Select(item=> {
            var inverse=item.Pose;inverse.Invert();return (item,inverse);
        }).ToArray();
        for(int y=44;y<230;y+=4)for(int x=32;x<229;x+=4) {
            var point=new Point(x,y);if(!JarGeometry.Interior.FillContains(point))continue;
            bool visible=false,near=false;
            foreach(var (item,inverse) in shapes) {
                var local=inverse.Transform(point);
                if(!Art.SpriteContains(item.Sprite,new(local.X/item.Size.Width+.5,local.Y/item.Size.Height+.5)))continue;
                visible=true;if(item.Depth==0)near=true;
            }
            samples++;if(visible)filled++;if(near)front++;
            if(y<80) {mouth++;if(visible)mouthFilled++;}
        }
        return new((double)front/samples,(double)filled/samples,(double)mouthFilled/mouth,scene.Objects.Count(o=>o.Depth<0));
    }
    internal static void CheckScene(JarPhysicsScene scene)
    {
        var coverage=Measure(scene);
        Check(coverage.RearBodies>0&&coverage.RearBodies<=160,"rear fill stays within its body budget");
        Check(coverage.Filled>.90,"filled jar has few sight lines through both slices: "+coverage);
        Check(coverage.Filled>coverage.Front+.035,"rear bodies close the front slice's gaps: "+coverage);
        Check(coverage.NearMouth>.70,"rear pile fills towards the mouth: "+coverage);
        foreach(var item in scene.Objects) {
            var filter=item.Body.GetFixtureList().FilterData;
            Check((filter.maskBits&1)!=0,"every depth slice collides with the same glass wall");
            Check(item.Depth<0?(filter.maskBits&2)==0&&(filter.maskBits&4)!=0:(filter.maskBits&4)==0&&(filter.maskBits&2)!=0,"projected depth overlap cannot jam the foreground physics");
        }
        JarObject? exposed=null;Point at=default;
        for(int y=50;y<220&&exposed==null;y+=5)for(int x=35;x<225;x+=5)
            if(scene.Hit(new(x,y)) is JarObject hit&&hit.Depth<0) {exposed=hit;at=new(x,y);break;}
        Check(exposed!=null,"rear objects remain selectable through exposed gaps");
        scene.Grab(exposed!,at);Check(scene.Grabbed==exposed&&scene.Pressed==exposed&&exposed!.Client==null,"exposed rear filler uses the real mouse joint and no client route");scene.Release();
    }
}
