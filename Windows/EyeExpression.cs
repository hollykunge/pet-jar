using System;

namespace PetJar;

internal enum EyeMood { Idle, Hover }
internal readonly record struct EyePose(double Left,double Right)
{
    internal static EyePose Blend(EyePose from,EyePose to,double amount) =>
        new(from.Left+(to.Left-from.Left)*amount,from.Right+(to.Right-from.Right)*amount);
}

internal static class EyeExpression
{
    internal static double Ease(double value) { double t=Math.Clamp(value,0,1);return Math.Clamp(t*t*t*(t*(t*6-15)+10),0,1); }
    // Quick closure, a short hold, then a slower reopening. Both ends have zero velocity.
    private static double Blink(double time,double duration=.32)
    {
        if(time<=0 || time>=duration)return 0;
        double t=time/duration;
        return t<.30?Ease(t/.30):t<.43?1:1-Ease((t-.43)/.57);
    }
    internal static EyePose Sample(Client client,double age,EyeMood mood,bool stationary=false)
    {
        if(stationary)return mood==EyeMood.Idle?default:new(1,1);
        age=Math.Max(0,age);
        int identity=(int)client;
        if(mood==EyeMood.Idle)
        {
            // Different phases and periods keep the pile from blinking in unison.
            double period=6.7+identity*1.17, shifted=age+identity*1.73;
            int cycle=(int)(shifted/period);
            double phase=shifted%period-1.5;
            if(client==Client.ChatGPT) {
                double peek=.82*Blink(phase,1.35);
                return new(peek,.82*Blink(phase-.045,1.35));
            }
            double left=Blink(phase),right=Blink(phase-.018);
            if(cycle%3==1) {left=Math.Max(left,Blink(phase-.46,.28));right=Math.Max(right,Blink(phase-.478,.28));}
            return new(left,right);
        }
        // Greeting, attentive blinking and occasional winks use the same eye art.
        double blink=client==Client.ChatGPT?0:Blink(age-.06,.36);
        double periodA=3.1+identity*.31, periodB=4.05+identity*.23;
        double phaseA=Math.Max(0,age-1.7)%(periodA+periodB);
        double recurring=Math.Max(Blink(phaseA-.7),Blink(phaseA-periodA-.7));
        double leftBlink=Math.Max(blink,recurring),rightBlink=leftBlink;
        // A single-eye greeting appears once on entry, never shifts the face.
        if(mood==EyeMood.Hover && client!=Client.ChatGPT)
            rightBlink=Math.Max(rightBlink,Blink(age-1.0,.44));
        if(client==Client.ChatGPT)return new(1-leftBlink,1-rightBlink);
        return new(leftBlink,rightBlink);
    }
}

// Capture the currently displayed pose when an interaction changes. Rapidly
// moving between characters therefore cannot reset an eye to a hard endpoint.
internal sealed class EyeAnimator(Client client)
{
    private EyeMood mood;
    private double started;
    private EyePose from;
    private double transition;
    internal EyePose At(double time,bool stationary=false)
    {
        var target=EyeExpression.Sample(client,time-started,mood,stationary);
        return stationary||transition==0?target:EyePose.Blend(from,target,EyeExpression.Ease((time-started)/transition));
    }
    internal void SetMood(EyeMood next,double time,bool stationary=false)
    {
        if(mood==next)return;
        from=At(time,stationary);mood=next;started=time;
        transition=next==EyeMood.Idle?.30:.18;
    }
}
