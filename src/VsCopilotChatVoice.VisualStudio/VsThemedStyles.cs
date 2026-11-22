using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace VsCopilotChatVoice.VisualStudio;

// Self-contained control styles driven only by Visual Studio environment brushes (hover, pressed, focus and disabled
// included). They do not depend on resolving Visual Studio's internal dialog styles, which are not guaranteed to be
// reachable from a tool window or a DialogWindow.
internal static class VsThemedStyles
{
    private const string Header =
        "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
        " xmlns:vs='clr-namespace:Microsoft.VisualStudio.PlatformUI;assembly=Microsoft.VisualStudio.Shell.15.0' ";

    private const double DisabledOpacity = 0.45;

    internal static Style Button() => Parse(Header + "TargetType='Button'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "1", "10,3") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>" +
        "<Border x:Name='chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' SnapsToDevicePixels='True'>" +
        "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger>" +
        "<Trigger Property='IsDefault' Value='True'><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger>" +
        "<MultiTrigger><MultiTrigger.Conditions><Condition Property='IsMouseOver' Value='True'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions>" +
        "<Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></MultiTrigger>" +
        "<MultiTrigger><MultiTrigger.Conditions><Condition Property='IsPressed' Value='True'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions>" +
        "<Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></MultiTrigger>" +
        Disabled("chrome") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    // Flat toolbar-style glyph button: transparent until hovered, like Visual Studio tool window buttons.
    internal static Style IconButton() => Parse(Header + "TargetType='Button'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "0", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>" +
        "<Border x:Name='chrome' CornerRadius='3' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' SnapsToDevicePixels='True'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<MultiTrigger><MultiTrigger.Conditions><Condition Property='IsMouseOver' Value='True'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions>" +
        "<Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></MultiTrigger>" +
        "<MultiTrigger><MultiTrigger.Conditions><Condition Property='IsPressed' Value='True'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions>" +
        "<Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></MultiTrigger>" +
        Disabled("chrome") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    // Segmented-strip choice (Concise/Detailed): the selected segment is marked by an accent border.
    internal static Style VersionChoice() => Parse(Header + "TargetType='RadioButton'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "0", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>" +
        "<Border x:Name='chrome' CornerRadius='3' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' SnapsToDevicePixels='True'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='chrome' Property='Background' Value='@ToolWindowButtonHoverActive@'/><Setter TargetName='chrome' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger>" +
        Disabled("chrome") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static Style RadioButton() => Parse(Header + "TargetType='RadioButton'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "0", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>" +
        "<Border x:Name='root' Background='Transparent' SnapsToDevicePixels='True'><StackPanel Orientation='Horizontal'>" +
        "<Grid Width='14' Height='14' VerticalAlignment='Center' Margin='0,0,6,0'>" +
        "<Ellipse x:Name='box' Stroke='@ToolWindowText@' StrokeThickness='1' Fill='@ToolWindowBackground@'/>" +
        "<Ellipse x:Name='dot' Width='6' Height='6' Fill='@ToolWindowText@' Visibility='Collapsed'/></Grid>" +
        "<ContentPresenter VerticalAlignment='Center' RecognizesAccessKey='True'/></StackPanel></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='dot' Property='Visibility' Value='Visible'/></Trigger>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='Stroke' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger>" +
        Disabled("root") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static Style CheckBox() => Parse(Header + "TargetType='CheckBox'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "0", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>" +
        "<Border x:Name='root' Background='Transparent' SnapsToDevicePixels='True'><StackPanel Orientation='Horizontal'>" +
        "<Grid Width='16' Height='16' VerticalAlignment='Center' Margin='0,0,8,0'>" +
        "<Border x:Name='box' BorderThickness='1' CornerRadius='3' BorderBrush='@ComboBoxBorder@' Background='@ToolWindowBackground@'/>" +
        "<Path x:Name='mark' Data='M 4,8 L 7,11 L 12,5' Stroke='@SystemHighlightText@' StrokeThickness='1.6' Visibility='Collapsed'/>" +
        "<Rectangle x:Name='mixed' Width='6' Height='6' Fill='@SystemHighlightText@' Visibility='Collapsed'/></Grid>" +
        "<ContentPresenter VerticalAlignment='Center' RecognizesAccessKey='True'/></StackPanel></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='mark' Property='Visibility' Value='Visible'/><Setter TargetName='box' Property='Background' Value='@AccentMedium@'/><Setter TargetName='box' Property='BorderBrush' Value='@AccentMedium@'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='{x:Null}'><Setter TargetName='mixed' Property='Visibility' Value='Visible'/><Setter TargetName='box' Property='Background' Value='@AccentMedium@'/><Setter TargetName='box' Property='BorderBrush' Value='@AccentMedium@'/></Trigger>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger>" +
        Disabled("root") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static Style ComboBox() => Parse(Header + "TargetType='ComboBox'>" +
        Common("ComboBoxBackground", "ComboBoxText", "ComboBoxBorder", "1", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>" +
        "<Grid SnapsToDevicePixels='True'>" +
        "<ToggleButton x:Name='toggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>" +
        "<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>" +
        "<Border x:Name='frame' Background='@ComboBoxBackground@' BorderBrush='@ComboBoxBorder@' BorderThickness='1'>" +
        "<Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0' Data='M 0,0 L 4,4 L 8,0' Stroke='@ComboBoxGlyph@' StrokeThickness='1.2'/></Border>" +
        "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='@ToolWindowButtonHoverActiveBorder@'/></Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate></ToggleButton.Template></ToggleButton>" +
        "<ContentPresenter IsHitTestVisible='False' Margin='8,3,26,3' VerticalAlignment='Center' HorizontalAlignment='Left' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'/>" +
        "<Popup x:Name='popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='None'>" +
        "<Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}' Background='@ToolWindowBackground@' BorderBrush='@ToolWindowBorder@' BorderThickness='1'>" +
        "<ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup></Grid>" +
        "<ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='" + DisabledOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'/></Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate></Setter.Value></Setter></Style>");

    internal static Style ComboBoxItem() => Parse(Header + "TargetType='ComboBoxItem'>" +
        Common("ToolWindowBackground", "ToolWindowText", "ToolWindowBorder", "0", "0") +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>" +
        "<Border x:Name='row' Background='Transparent' Padding='8,3' SnapsToDevicePixels='True'><ContentPresenter/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsSelected' Value='True'><Setter TargetName='row' Property='Background' Value='@SystemHighlight@'/><Setter TargetName='row' Property='TextElement.Foreground' Value='@SystemHighlightText@'/></Trigger>" +
        "<Trigger Property='IsHighlighted' Value='True'><Setter TargetName='row' Property='Background' Value='@SystemHighlight@'/><Setter TargetName='row' Property='TextElement.Foreground' Value='@SystemHighlightText@'/></Trigger>" +
        Disabled("row") +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static Style Slider() => Parse(Header + "TargetType='Slider'>" +
        "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>" +
        "<Grid Height='22'>" +
        "<Border Height='4' VerticalAlignment='Center' Background='@ToolWindowBorder@'/>" +
        "<Track x:Name='PART_Track'>" +
        "<Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
        "<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
        "<Track.Thumb><Thumb Focusable='False'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Width='8' Height='16' CornerRadius='2' Background='@AccentMedium@'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
        "</Track></Grid>" +
        "<ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='" + DisabledOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'/></Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate></Setter.Value></Setter></Style>");

    private static string Common(string background, string foreground, string border, string thickness, string padding) =>
        "<Setter Property='Background' Value='@" + background + "@'/><Setter Property='Foreground' Value='@" + foreground + "@'/>" +
        "<Setter Property='BorderBrush' Value='@" + border + "@'/><Setter Property='BorderThickness' Value='" + thickness + "'/>" +
        "<Setter Property='Padding' Value='" + padding + "'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>";

    private static string Disabled(string target) =>
        "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='" + target + "' Property='Opacity' Value='" +
        DisabledOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'/></Trigger>";

    private static Style Parse(string xaml)
    {
        // ToolWindowButtonHoverActive/Down fills render as light plates in the VS dark theme (unreadable under tool window
        // text), so interaction states keep the normal fill and show an accent border from the proven SystemHighlight brush.
        xaml = xaml.Replace("@ToolWindowButtonHoverActiveBorder@", "@SystemHighlight@").Replace("@ToolWindowButtonDownBorder@", "@SystemHighlight@")
            .Replace("@ToolWindowButtonHoverActive@", "@ToolWindowBackground@").Replace("@ToolWindowButtonDown@", "@ToolWindowBackground@");
        string resolved = Regex.Replace(xaml, "@(\\w+)@", "{DynamicResource {x:Static vs:EnvironmentColors.$1BrushKey}}");
        return (Style)XamlReader.Parse(resolved);
    }
}
