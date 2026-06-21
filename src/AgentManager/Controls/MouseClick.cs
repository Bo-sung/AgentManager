using System.Windows;
using System.Windows.Input;

namespace AgentManager.Controls;

/// <summary>
/// Attached behavior: bind a left-click (MouseLeftButtonUp) on any UIElement to an
/// ICommand, so list rows / nav items / swatches don't need code-behind handlers.
/// Usage: controls:MouseClick.Command="{Binding ...}" controls:MouseClick.CommandParameter="{Binding}"
/// </summary>
public static class MouseClick
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(MouseClick), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject o, ICommand? v) => o.SetValue(CommandProperty, v);

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(MouseClick), new PropertyMetadata(null));

    public static object? GetCommandParameter(DependencyObject o) => o.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject o, object? v) => o.SetValue(CommandParameterProperty, v);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if (e.OldValue is not null) el.MouseLeftButtonUp -= OnMouseUp;
        if (e.NewValue is not null) el.MouseLeftButtonUp += OnMouseUp;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var cmd = GetCommand(d);
        var param = GetCommandParameter(d);
        if (cmd?.CanExecute(param) == true) cmd.Execute(param);
    }
}
