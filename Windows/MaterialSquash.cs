using System;
using System.Windows;
using System.Windows.Media;

namespace PetJar;

internal readonly record struct SquashMaterial(double Limit,double Stiffness,double Damping,double Bulge)
{
    internal static SquashMaterial For(Client? client,string sprite)=>
        client is Client.ChatGPT or Client.Muse or Client.Claude?new(.09,145,19,.42):
        sprite.Contains("knit")?new(.065,180,23,.35):
        sprite.Contains("moss")?new(.045,200,25,.3):
        sprite.Contains("bubble")?new(.02,230,28,.35):new(0,450,42,0);
}

// A bounded local strain tensor: compression follows the contact direction and
// gently bulges sideways. This is visual compliance around a rigid collider.
internal sealed class MaterialSquash(SquashMaterial material)
{
    private Strain value,speed,load,grip;
    internal double Limit=>material.Limit;
    internal double Compression=>Math.Max(0,value.Largest);
    internal bool IsChanging=>speed.LengthSquared>1e-7||(Target-value).LengthSquared>1e-7;
    internal Matrix Transform=>new(1-value.X+material.Bulge*value.Y,-(1+material.Bulge)*value.Cross,
        -(1+material.Bulge)*value.Cross,1-value.Y+material.Bulge*value.X,0,0);
    private Strain Target {
        get {
            double weight=load.X+load.Y;
            double amount=weight>1e-8?material.Limit*(1-Math.Exp(-.18*weight))/weight:0;
            return (load*amount+grip).Clamp(0,material.Limit);
        }
    }
    internal void ClearLoad()=>load=default;
    internal void AddLoad(Vector normal,double ownWeights)
    {
        if(material.Limit==0||!double.IsFinite(ownWeights)||ownWeights<=0)return;
        load+=Strain.Along(normal)*Math.Min(25,ownWeights);
    }
    internal void Hold(Vector normal)=>grip=Strain.Along(normal)*Math.Min(.055,material.Limit*.7);
    internal void Release()=>grip=default;
    internal void Impact(Vector normal,double relativeSpeed)
    {
        if(material.Limit==0||!double.IsFinite(relativeSpeed))return;
        speed=(speed+Strain.Along(normal)*(material.Limit*Math.Clamp(relativeSpeed,0,4)*5)).Clamp(-2,2);
    }
    internal void Advance(double seconds)
    {
        if(material.Limit==0)return;
        var target=Target;
        speed+=((target-value)*material.Stiffness-speed*material.Damping)*seconds;
        value=(value+speed*seconds).Clamp(-.008,material.Limit);
        if(speed.LengthSquared<1e-7&&(target-value).LengthSquared<1e-7) {value=target;speed=default;}
    }
    private readonly record struct Strain(double X,double Y,double Cross)
    {
        private double Radius=>Math.Sqrt((X-Y)*(X-Y)*.25+Cross*Cross);
        internal double Largest=>(X+Y)*.5+Radius;
        internal double LengthSquared=>X*X+Y*Y+2*Cross*Cross;
        internal static Strain Along(Vector direction) {
            if(direction.LengthSquared<1e-8)direction=new(1,0);
            direction.Normalize();return new(direction.X*direction.X,direction.Y*direction.Y,direction.X*direction.Y);
        }
        internal Strain Clamp(double minimum,double maximum) {
            double radius=Radius,center=(X+Y)*.5,high=Math.Clamp(center+radius,minimum,maximum),low=Math.Clamp(center-radius,minimum,maximum);
            if(radius<1e-9)return new((high+low)*.5,(high+low)*.5,0);
            double midpoint=(high+low)*.5,scale=(high-low)*.5/radius;
            return new(midpoint+(X-Y)*.5*scale,midpoint-(X-Y)*.5*scale,Cross*scale);
        }
        public static Strain operator +(Strain a,Strain b)=>new(a.X+b.X,a.Y+b.Y,a.Cross+b.Cross);
        public static Strain operator -(Strain a,Strain b)=>new(a.X-b.X,a.Y-b.Y,a.Cross-b.Cross);
        public static Strain operator *(Strain a,double amount)=>new(a.X*amount,a.Y*amount,a.Cross*amount);
    }
}
