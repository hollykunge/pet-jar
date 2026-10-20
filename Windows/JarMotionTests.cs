using System;
using System.Windows;

namespace PetJar;

internal static class JarMotionTests
{
    private static void Check(bool condition,string reason) {if(!condition)throw new InvalidOperationException(reason);}
    internal static void Run()
    {
        const double dt=1d/60;
        var motion=new JarMotion();var position=new Point(100,200);motion.Begin(position,0);
        for(int i=1;i<=60;i++)Check(motion.Sample(position,i*dt)==default,"a held stationary jar applies no impulse");
        Vector total=default,latest=default;
        for(int i=61;i<=150;i++) {position.X+=5;latest=motion.Sample(position,i*dt);total+=latest;}
        Check(total.X>299&&Math.Abs(total.Y)<.001,"jar velocity follows window travel in DIPs");
        Check(latest.Length<.001,"constant velocity does not keep adding energy");
        motion.End();
        for(int i=151;i<=210;i++) {var stopping=motion.Sample(position,i*dt);Check(stopping.X<=.001,"stopping produces the opposite velocity change");total+=stopping;}
        Check(total.Length<.001&&!motion.IsTracking,"release accounts for the stop and clears motion history");

        motion.Begin(new(0,0),0);motion.End();
        Check(motion.Sample(new(70,-30),dt).Length>0,"a complete fast drag between rendered frames still shakes the jar");
        motion.Reset();Check(motion.Sample(new(10000,10000),10)==default,"reset/finding the jar cannot create a teleport impulse");
        motion.Begin(new(10,20),0);motion.Rebase(new(100,-80));
        Check(motion.Sample(new(110,-60),dt)==default,"screen clamping is excluded from physical travel");
        Check(motion.Sample(new(200,100),2)==default&&motion.IsTracking,"a stalled frame rebases rather than generating a burst");
        Check(motion.Sample(new(205,100),2+dt).X>0,"dragging resumes after a rendering stall");
        motion.Reset();motion.Begin(new(0,0),0);
        Vector reversal=default;
        for(int i=1;i<=30;i++)motion.Sample(new(i*5,0),i*dt);
        for(int i=31;i<=40;i++)reversal+=motion.Sample(new(150-(i-30)*5,0),i*dt);
        Check(reversal.X<0,"reversing the jar reverses its acceleration");
    }
}
