using System;
using Dalamud.Plugin.Services;
using LolPings.Input;
using LolPings.Log;
using LolPings.UI.View;
using Reactive.Bindings;
using WindowsInput.Events;

namespace LolPings.UI.Presenter;

public class ConfigWindowPresenter(
    ConfigWindow view,
    Configuration configuration,
    IFramework framework,
    InputEventSource inputEventSource,
    InputManager inputManager,
    ILogger logger) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly ConfigWindow view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Configuration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IFramework framework = framework ?? throw new ArgumentNullException(nameof(framework));
    private readonly InputEventSource inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));
    private readonly InputManager inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private bool keyDownListenerSubscribed;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
    }

    private void BindVariables()
    {
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
                this.inputEventSource.SubscribeToKeyDown(OnInputKeyDown);
                this.keyDownListenerSubscribed = true;
            }
            else if (k == Keybind.None && this.keyDownListenerSubscribed)
            {
                this.inputEventSource.UnsubscribeToKeyDown(OnInputKeyDown);
                this.keyDownListenerSubscribed = false;
            }
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

    private void OnInputKeyDown(KeyDown k)
    {
        // This callback can be called from a non-framework thread, and UI values should only be modified
        // on the framework thread (or else the game can crash)
        this.framework.Run(() =>
        {
            var editedKeybind = this.view.KeybindBeingEdited.Value;
            this.view.KeybindBeingEdited.Value = Keybind.None;

            //switch (editedKeybind)
            //{
            //    case Keybind.PushToTalk:
            //        this.configuration.PushToTalkKeybind = k.Key;
            //        break;
            //    case Keybind.MuteMic:
            //        this.configuration.MuteMicKeybind = k.Key;
            //        break;
            //    case Keybind.Deafen:
            //        this.configuration.DeafenKeybind = k.Key;
            //        break;
            //    default:
            //        return;
            //}
            this.configuration.Save();
            this.inputManager.UpdateListeners();
        });

    }
}
