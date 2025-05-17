using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using System;
using SmartPings.UI.Presenter;

namespace SmartPings;

public class CommandDispatcher(
    ICommandManager commandManager,
    MainWindowPresenter mainWindowPresenter) : IDalamudHook
{
    private const string commandName = "/smartpings";
    private const string commandNameAlt = "/sp";

    private readonly ICommandManager commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
    private readonly MainWindowPresenter mainWindowPresenter = mainWindowPresenter ?? throw new ArgumentNullException(nameof(mainWindowPresenter));

    public void HookToDalamud()
    {
        this.commandManager.AddHandler(commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the SmartPings window"
        });
        this.commandManager.AddHandler(commandNameAlt, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the SmartPings window"
        });
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(commandName);
        this.commandManager.RemoveHandler(commandNameAlt);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just display our main ui
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        this.mainWindowPresenter.View.Visible = true;
    }
}
