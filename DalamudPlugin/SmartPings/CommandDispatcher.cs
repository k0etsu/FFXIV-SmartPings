using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using System;
using SmartPings.UI.Presenter;

namespace SmartPings;

public class CommandDispatcher(
    ICommandManager commandManager,
    MainWindowPresenter mainWindowPresenter,
    ConfigWindowPresenter configWindowPresenter) : IDalamudHook
{
    private const string commandName = "/smartpings";
    private const string commandNameAlt = "/sp";
    private const string configCommandName = "/smartpingsconfig";
    private const string configCommandNameAlt = "/spc";

    private readonly ICommandManager commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
    private readonly MainWindowPresenter mainWindowPresenter = mainWindowPresenter ?? throw new ArgumentNullException(nameof(mainWindowPresenter));
    private readonly ConfigWindowPresenter configWindowPresenter = configWindowPresenter ?? throw new ArgumentNullException(nameof(configWindowPresenter));

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
        this.commandManager.AddHandler(configCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the SmartPings config window"
        });
        this.commandManager.AddHandler(configCommandNameAlt, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the SmartPings config window"
        });
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(commandName);
        this.commandManager.RemoveHandler(commandNameAlt);
        this.commandManager.RemoveHandler(configCommandName);
        this.commandManager.RemoveHandler(configCommandNameAlt);
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

    private void OnConfigCommand(string command, string args)
    {
        this.configWindowPresenter.View.Visible = true;
    }
}
