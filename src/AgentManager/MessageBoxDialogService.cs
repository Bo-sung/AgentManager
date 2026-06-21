using System.Windows;

namespace AgentManager;

public sealed class MessageBoxDialogService : ViewModels.IDialogService
{
    public bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
