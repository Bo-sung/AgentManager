namespace AgentManager.ViewModels;

public interface IDialogService
{
    bool Confirm(string message, string title);
}
