using System;
using System.Windows;
using System.Windows.Markup;
using System.Globalization;

namespace HWWidget;

/// <summary>Dark/light Win11-ish context menu styling, kept as XAML text so the menu
/// can be built in code (its items are dynamic) without duplicating a template per item.</summary>
static class MenuStyles
{
    public static ResourceDictionary Create(Palette p)
    {
        string bg = p.IsLight ? "#F2F7F7F7" : "#F22B2B2B";
        string hover = p.IsLight ? "#14000000" : "#2EFFFFFF";
        string fg = p.IsLight ? "#E6000000" : "#EFFFFFFF";
        string line = p.IsLight ? "#22000000" : "#22FFFFFF";
        string border = p.IsLight ? "#33000000" : "#33FFFFFF";

        // tokens, not string.Format: the XAML is full of {StaticResource ...} braces
        string xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="MenuBg" Color="@BG@" />
  <SolidColorBrush x:Key="MenuHover" Color="@HOVER@" />

  <Style TargetType="ContextMenu">
    <Setter Property="Foreground" Value="@FG@" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border Background="{StaticResource MenuBg}" BorderBrush="@BORDER@" BorderThickness="1"
                  CornerRadius="8" Padding="3" Margin="8">
            <Border.Effect>
              <DropShadowEffect BlurRadius="16" Opacity="0.5" ShadowDepth="3" />
            </Border.Effect>
            <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" />
            </ScrollViewer>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="Separator">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Separator">
          <Border Height="1" Background="@LINE@" Margin="10,4" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="@FG@" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Grid>
            <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="6"
                    Margin="4,1" Padding="8,6">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="16" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="Check" Text="&#x2713;" FontSize="11" VerticalAlignment="Center"
                           Visibility="Collapsed" />
                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True"
                                  VerticalAlignment="Center" />
                <TextBlock x:Name="Arrow" Grid.Column="2" Text="&#x203A;" Margin="14,0,0,0"
                           Opacity="0.7" VerticalAlignment="Center" Visibility="Collapsed" />
              </Grid>
            </Border>
            <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False"
                   IsOpen="{TemplateBinding IsSubmenuOpen}" Placement="Right"
                   HorizontalOffset="0" VerticalOffset="-7" PopupAnimation="Fade">
              <Border Background="{StaticResource MenuBg}" BorderBrush="@BORDER@" BorderThickness="1"
                      CornerRadius="8" Padding="2" Margin="6">
                <Border.Effect>
                  <DropShadowEffect BlurRadius="14" Opacity="0.45" ShadowDepth="3" />
                </Border.Effect>
                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" />
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource MenuHover}" />
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Check" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="HasItems" Value="True">
              <Setter TargetName="Arrow" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";

        xaml = xaml.Replace("@BG@", bg).Replace("@HOVER@", hover).Replace("@FG@", fg)
                   .Replace("@LINE@", line).Replace("@BORDER@", border);

        return (ResourceDictionary)XamlReader.Parse(xaml);
    }
}
