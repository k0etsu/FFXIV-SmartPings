using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using Reactive.Bindings;
using SmartPings.Input;
using SmartPings.Log;
using SmartPings.UI.View;
using System;

namespace SmartPings.UI.Presenter;

public class ConfigWindowPresenter(
    ConfigWindow view,
    IFramework framework,
    Configuration configuration,
    KeyStateWrapper keyStateWrapper,
    ILogger logger) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly ConfigWindow view = view;
    private readonly IFramework framework = framework;
    private readonly Configuration configuration = configuration;
    private readonly KeyStateWrapper keyStateWrapper = keyStateWrapper;
    private readonly ILogger logger = logger;

    private bool keyDownListenerSubscribed;

    public void Dispose()
    {
        this.keyStateWrapper.OnKeyDown -= OnKeyDown;
        GC.SuppressFinalize(this);
    }

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
    }

    private void BindVariables()
    {
        Bind(this.view.EnablePingInput,
            b => { this.configuration.EnablePingInput = b; this.configuration.Save(); }, this.configuration.EnablePingInput);

        Bind(this.view.SendGuiPingsToCustomServer,
            b => { this.configuration.SendGuiPingsToCustomServer = b; this.configuration.Save(); }, this.configuration.SendGuiPingsToCustomServer);
        Bind(this.view.SendGuiPingsToXivChat,
            b => { this.configuration.SendGuiPingsToXivChat = b; this.configuration.Save(); }, this.configuration.SendGuiPingsToXivChat);

        Bind(this.view.MasterVolume,
            f => { this.configuration.MasterVolume = f; this.configuration.Save(); }, this.configuration.MasterVolume);

        Bind(this.view.PlayRoomJoinAndLeaveSounds,
            b => { this.configuration.PlayRoomJoinAndLeaveSounds = b; this.configuration.Save(); }, this.configuration.PlayRoomJoinAndLeaveSounds);
        Bind(this.view.KeybindsRequireGameFocus,
            b => { this.configuration.KeybindsRequireGameFocus = b; this.configuration.Save(); }, this.configuration.KeybindsRequireGameFocus);
        Bind(this.view.PrintLogsToChat,
            b => { this.configuration.PrintLogsToChat = b; this.configuration.Save(); }, this.configuration.PrintLogsToChat);
        Bind(this.view.MinimumVisibleLogLevel,
            i => { this.configuration.MinimumVisibleLogLevel = i; this.configuration.Save(); }, this.configuration.MinimumVisibleLogLevel);
    }

    private void BindActions()
    {
        this.view.KeybindBeingEdited.Subscribe(k =>
        {
            if (k != Keybind.None && !this.keyDownListenerSubscribed)
            {
                this.keyStateWrapper.OnKeyDown += OnKeyDown;
                this.keyDownListenerSubscribed = true;
            }
            else if (k == Keybind.None && this.keyDownListenerSubscribed)
            {
                this.keyStateWrapper.OnKeyDown -= OnKeyDown;
                this.keyDownListenerSubscribed = false;
            }
        });
        this.view.ClearKeybind.Subscribe(k =>
        {
            switch (k)
            {
                case Keybind.Ping:
                    this.configuration.PingKeybind = default; break;
                case Keybind.QuickPing:
                    this.configuration.QuickPingKeybind = default; break;
                default:
                    return;
            }
            this.configuration.Save();
        });
    }

    private void Bind<T>(
        IReactiveProperty<T> reactiveProperty,
        Action<T> dataUpdateAction,
        T initialValue)
    {
        if (initialValue != null)
        {
            reactiveProperty.Value = initialValue;
        }
        reactiveProperty.Subscribe(dataUpdateAction);
    }

    private void OnKeyDown(VirtualKey key)
    {
        // Disallow any keybinds to left mouse
        if (key == VirtualKey.LBUTTON) { return; }

        // This callback can be called from a non-framework thread, and UI values should only be modified
        // on the framework thread (or else the game can crash)
        this.framework.Run(() =>
        {
            var editedKeybind = this.view.KeybindBeingEdited.Value;
            this.view.KeybindBeingEdited.Value = Keybind.None;

            switch (editedKeybind)
            {
                case Keybind.Ping:
                    this.configuration.PingKeybind = key; break;
                case Keybind.QuickPing:
                    this.configuration.QuickPingKeybind = key; break;
                default:
                    return;
            }
            this.configuration.Save();
        });
    }
}
