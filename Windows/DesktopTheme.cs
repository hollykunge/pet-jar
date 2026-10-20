using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace PetJar;

internal static class DesktopTheme
{
    internal static readonly Brush Ink=JarDrawing.Brush(.19,.18,.16);
    internal static readonly Brush Muted=JarDrawing.Brush(.48,.46,.42);
    internal static readonly Brush Cream=JarDrawing.Brush(.98,.97,.94);

    internal static void Put(Grid grid,UIElement child,int row,int column=0) {Grid.SetRow(child,row);Grid.SetColumn(child,column);grid.Children.Add(child);}
    internal static ResourceDictionary Styles() => (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Cursor" Value="Hand"/><Setter Property="Padding" Value="8,2"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="12" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.86"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.42"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#B86F36"/><Setter TargetName="Surface" Property="BorderThickness" Value="1"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="FlatButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#7A756B"/><Setter Property="Padding" Value="0"/></Style>
        </ResourceDictionary>
        """);
}
