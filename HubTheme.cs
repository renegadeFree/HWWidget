using System.Windows;
using System.Windows.Markup;

namespace HWWidget;

/// <summary>Windows 11 settings-look control templates (rounded buttons, switches, sliders,
/// combo boxes, cards) as used by the DeskBox settings window. Built as XAML text so the
/// hub can stay 100% code-driven.</summary>
static class HubTheme
{
    public static ResourceDictionary Create() => (ResourceDictionary)XamlReader.Parse(Xaml);

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <SolidColorBrush x:Key="TextPrimary" Color="#FFFFFFFF" />
  <SolidColorBrush x:Key="TextSecondary" Color="#B3FFFFFF" />
  <SolidColorBrush x:Key="TextTertiary" Color="#8AFFFFFF" />
  <SolidColorBrush x:Key="Accent" Color="#FF4CC2FF" />
  <SolidColorBrush x:Key="AccentText" Color="#FF003049" />
  <SolidColorBrush x:Key="CardBg" Color="#0FFFFFFF" />
  <SolidColorBrush x:Key="CardBgHover" Color="#17FFFFFF" />
  <SolidColorBrush x:Key="CardStroke" Color="#1AFFFFFF" />
  <SolidColorBrush x:Key="ControlStroke" Color="#1AFFFFFF" />
  <SolidColorBrush x:Key="ControlFill" Color="#0FFFFFFF" />
  <SolidColorBrush x:Key="ControlFillHover" Color="#1AFFFFFF" />
  <SolidColorBrush x:Key="ControlFillPressed" Color="#0AFFFFFF" />
  <SolidColorBrush x:Key="TrackOff" Color="#2EFFFFFF" />
  <SolidColorBrush x:Key="PopupBg" Color="#F22B2B2B" />

  <Style x:Key="HubCard" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource CardBg}" />
    <Setter Property="BorderBrush" Value="{StaticResource CardStroke}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="6" />
    <Setter Property="Margin" Value="0,0,0,4" />
  </Style>

  <Style x:Key="HubButton" TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Background" Value="{StaticResource ControlFill}" />
    <Setter Property="BorderBrush" Value="{StaticResource ControlStroke}" />
    <Setter Property="Padding" Value="12,5" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" CornerRadius="5" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillHover}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillPressed}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubAccentButton" TargetType="Button" BasedOn="{StaticResource HubButton}">
    <Setter Property="Background" Value="{StaticResource Accent}" />
    <Setter Property="BorderBrush" Value="{StaticResource Accent}" />
    <Setter Property="Foreground" Value="{StaticResource AccentText}" />
    <Setter Property="FontWeight" Value="SemiBold" />
  </Style>

  <Style x:Key="HubIconButton" TargetType="Button" BasedOn="{StaticResource HubButton}">
    <Setter Property="Padding" Value="0" />
    <Setter Property="Width" Value="32" />
    <Setter Property="Height" Value="32" />
    <Setter Property="FontFamily" Value="Segoe MDL2 Assets" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
  </Style>

  <Style x:Key="HubNavButton" TargetType="Button" BasedOn="{StaticResource HubButton}">
    <Setter Property="HorizontalContentAlignment" Value="Left" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Padding" Value="10,8" />
    <Setter Property="Margin" Value="0,1" />
  </Style>

  <Style x:Key="HubToggle" TargetType="CheckBox">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Border x:Name="Track" Width="40" Height="20" CornerRadius="10"
                    Background="{StaticResource TrackOff}" BorderBrush="{StaticResource CardStroke}" BorderThickness="1">
              <Ellipse x:Name="Knob" Width="12" Height="12" Fill="{StaticResource TextSecondary}"
                       HorizontalAlignment="Left" Margin="3,0,0,0" />
            </Border>
            <ContentPresenter x:Name="Label" Margin="9,0,0,0" VerticalAlignment="Center" />
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="Background" Value="{StaticResource Accent}" />
              <Setter TargetName="Track" Property="BorderBrush" Value="{StaticResource Accent}" />
              <Setter TargetName="Knob" Property="Fill" Value="#FF1A1A1A" />
              <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
              <Setter TargetName="Knob" Property="Margin" Value="0,0,3,0" />
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Track" Property="Opacity" Value="0.9" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubThumb" TargetType="Thumb">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <Grid>
            <Ellipse Width="20" Height="20" Fill="{StaticResource Accent}" Stroke="#33000000" StrokeThickness="1" />
            <Ellipse Width="6" Height="6" Fill="#FF1A1A1A" />
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubSliderFill" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Height="4" CornerRadius="2" Background="{StaticResource Accent}" VerticalAlignment="Center" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubSliderEmpty" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Height="4" CornerRadius="2" Background="{StaticResource TrackOff}" VerticalAlignment="Center" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubSlider" TargetType="Slider">
    <Setter Property="MinHeight" Value="26" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Slider">
          <Grid VerticalAlignment="Center">
            <Track x:Name="PART_Track">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="Slider.DecreaseLarge" Style="{StaticResource HubSliderFill}" />
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style="{StaticResource HubThumb}" />
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="Slider.IncreaseLarge" Style="{StaticResource HubSliderEmpty}" />
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubComboToggle" TargetType="ToggleButton">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToggleButton">
          <Border x:Name="Bd" Background="{StaticResource ControlFill}" BorderBrush="{StaticResource CardStroke}"
                  BorderThickness="1" CornerRadius="5" Padding="10,5" MinWidth="150">
            <Grid>
              <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="Left" />
              <TextBlock x:Name="Chevron" Text="&#xE70D;" FontFamily="Segoe MDL2 Assets" FontSize="10"
                         HorizontalAlignment="Right" VerticalAlignment="Center" Margin="12,0,0,0" Opacity="0.8" />
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillHover}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubComboBoxItem" TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Padding" Value="10,6" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Margin="2,1" Padding="{TemplateBinding Padding}">
            <ContentPresenter />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillHover}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubCombo" TargetType="ComboBox">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="ItemContainerStyle" Value="{StaticResource HubComboBoxItem}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton x:Name="ToggleButton" Focusable="False" ClickMode="Press" Style="{StaticResource HubComboToggle}"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                          Content="{TemplateBinding SelectionBoxItem}" />
            <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                   IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" VerticalOffset="3">
              <Border Background="{StaticResource PopupBg}" BorderBrush="{StaticResource CardStroke}" BorderThickness="1"
                      CornerRadius="6" Padding="2" Margin="8">
                <Border.Effect>
                  <DropShadowEffect BlurRadius="14" Opacity="0.45" ShadowDepth="3" />
                </Border.Effect>
                <ScrollViewer MaxHeight="280" Width="200" VerticalScrollBarVisibility="Auto">
                  <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" />
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="10" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False" />
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Border Width="4" CornerRadius="2" Background="#4DFFFFFF" Margin="3,0,3,0" />
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False" />
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubPageTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="26" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="TextWrapping" Value="Wrap" />
    <Setter Property="Margin" Value="0,0,0,2" />
  </Style>

  <Style x:Key="HubPageDesc" TargetType="TextBlock">
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="Foreground" Value="{StaticResource TextSecondary}" />
    <Setter Property="TextWrapping" Value="Wrap" />
    <Setter Property="Margin" Value="0,0,0,14" />
  </Style>

  <Style x:Key="HubSubTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="14.5" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Margin" Value="2,16,2,6" />
  </Style>

  <Style x:Key="HubCardTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="13.5" />
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="TextWrapping" Value="Wrap" />
  </Style>

  <Style x:Key="HubCardDesc" TargetType="TextBlock">
    <Setter Property="FontSize" Value="11.5" />
    <Setter Property="Foreground" Value="{StaticResource TextSecondary}" />
    <Setter Property="TextWrapping" Value="Wrap" />
  </Style>

  <Style x:Key="HubGlyph" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="Segoe MDL2 Assets" />
    <Setter Property="FontSize" Value="15" />
    <Setter Property="Foreground" Value="{StaticResource TextSecondary}" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Width" Value="22" />
  </Style>

  <Style x:Key="HubTextBox" TargetType="TextBox">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Background" Value="{StaticResource ControlFill}" />
    <Setter Property="BorderBrush" Value="{StaticResource CardStroke}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="7,4" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="CaretBrush" Value="{StaticResource TextPrimary}" />
    <Setter Property="SelectionBrush" Value="{StaticResource Accent}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" CornerRadius="4" Padding="{TemplateBinding Padding}">
            <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillHover}" />
            </Trigger>
            <Trigger Property="IsFocused" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="{StaticResource Accent}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubPassword" TargetType="PasswordBox">
    <Setter Property="Foreground" Value="{StaticResource TextPrimary}" />
    <Setter Property="Background" Value="{StaticResource ControlFill}" />
    <Setter Property="BorderBrush" Value="{StaticResource CardStroke}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="7,4" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="CaretBrush" Value="{StaticResource TextPrimary}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="PasswordBox">
          <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" CornerRadius="4" Padding="{TemplateBinding Padding}">
            <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource ControlFillHover}" />
            </Trigger>
            <Trigger Property="IsFocused" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="{StaticResource Accent}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="HubProgress" TargetType="ProgressBar">
    <Setter Property="Height" Value="6" />
    <Setter Property="Foreground" Value="{StaticResource Accent}" />
    <Setter Property="Background" Value="{StaticResource TrackOff}" />
    <Setter Property="BorderThickness" Value="0" />
  </Style>
</ResourceDictionary>
""";
}
