using System;
using System.Windows;

namespace PetJar;

// Sample actual window travel once per rendered frame. The scene is in jar
// coordinates, so a change in jar velocity becomes the opposite body impulse.
internal sealed class JarMotion
{
    private Point previous;
    private double sampledAt;
    private Vector velocity;
    private bool held;
    internal bool IsTracking {get;private set;}
    internal void Begin(Point position,double time)
    {
        previous=position;sampledAt=time;held=true;IsTracking=true;
    }
    internal void End()=>held=false;
    internal void Rebase(Vector automaticMove)
    {
        if(IsTracking)previous+=automaticMove;
    }
    internal void Reset() {held=false;IsTracking=false;velocity=default;}
    internal Vector Sample(Point position,double time)
    {
        if(!IsTracking)return default;
        double elapsed=time-sampledAt;
        if(elapsed<=.00001)return default;
        if(!double.IsFinite(position.X)||!double.IsFinite(position.Y)) {Reset();return default;}
        if(elapsed>.2) {previous=position;sampledAt=time;velocity=default;IsTracking=held;return default;}
        var target=(position-previous)/elapsed;
        if(target.Length>750)target*=750/target.Length;
        var old=velocity;
        velocity+=(target-velocity)*(1-Math.Exp(-32*elapsed));
        if(target.Length<.01&&velocity.Length<.4)velocity=default;
        previous=position;sampledAt=time;
        if(!held&&velocity.Length==0)IsTracking=false;
        return velocity-old;
    }
}
