using System;
using System.Windows;

namespace PetJar;

internal static class ComposerPlacement
{
    internal static Point At(Rect pet,Point click,Size composer,Rect work)
    {
        // Keep the jar unobstructed and line up the input's centre with the
        // actual click, retaining that local anchor when the jar is dragged.
        double left=pet.Left-composer.Width-12;
        if(left<work.Left)left=pet.Right+12;
        double top=pet.Top+click.Y-composer.Height/2;
        return new(
            Math.Clamp(left,work.Left,Math.Max(work.Left,work.Right-composer.Width)),
            Math.Clamp(top,work.Top+ComposerWindow.AvatarOverlap,
                Math.Max(work.Top+ComposerWindow.AvatarOverlap,work.Bottom-composer.Height)));
    }
}
