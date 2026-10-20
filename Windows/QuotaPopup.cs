using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace PetJar;

internal sealed class QuotaPopup : Popup
{
    internal QuotaPopup(FrameworkElement owner)
    {
        PlacementTarget=owner;Placement=PlacementMode.Custom;AllowsTransparency=true;StaysOpen=true;
        IsHitTestVisible=false;Focusable=false;PopupAnimation=PopupAnimation.None;
        CustomPopupPlacementCallback=(size,target,offset)=>new[] {
            new CustomPopupPlacement(new Point(-size.Width-6,Math.Max(0,(260-size.Height)/2)),PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(266,Math.Max(0,(260-size.Height)/2)),PopupPrimaryAxis.Vertical)
        };
    }
    internal void Loading(Client target)
    {
        Child=QuotaCard.Build(target,null,true);IsOpen=true;
    }
    internal void Update(Client target,AccountQuota quota,bool bound)
    {
        Child=QuotaCard.Build(target,quota,bound);IsOpen=true;
    }
}
