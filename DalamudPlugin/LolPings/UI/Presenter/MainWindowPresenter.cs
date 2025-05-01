using AsyncAwaitBestPractices;
using Dalamud.Plugin.Services;
using LolPings.Extensions;
using LolPings.Input;
using LolPings.Log;
using LolPings.Network;
using LolPings.UI.View;
using Reactive.Bindings;
using System;
using System.Reactive.Linq;

namespace LolPings.UI.Presenter;

public class MainWindowPresenter(
    MainWindow view,
    Configuration configuration,
    IClientState clientState,
    IAudioDeviceController audioDeviceController,
    ServerConnection serverConnection,
    ILogger logger) : IPluginUIPresenter
{
    public IPluginUIView View => this.view;

    private readonly MainWindow view = view;
    private readonly Configuration configuration = configuration;
    private readonly IClientState clientState = clientState;
    private readonly IAudioDeviceController audioDeviceController = audioDeviceController;
    private readonly ServerConnection serverConection = serverConnection;
    private readonly ILogger logger = logger;

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
    }

    private void BindVariables()
    {
        Bind(this.view.PublicRoom,
            b =>
            {
                this.configuration.PublicRoom = b; this.configuration.Save();
            },
            this.configuration.PublicRoom);
        Bind(this.view.RoomName,
            s => { this.configuration.RoomName = s; this.configuration.Save(); }, this.configuration.RoomName);
        Bind(this.view.RoomPassword,
            s => { this.configuration.RoomPassword = s; this.configuration.Save(); }, this.configuration.RoomPassword);
    }

    private void BindActions()
    {
        this.view.JoinRoom.Subscribe(_ =>
        {
            this.serverConection.Channel?.ClearLatestDisconnectMessage();
            if (this.view.PublicRoom.Value)
            {
                this.serverConection.JoinPublicRoom();
            }
            else
            {
                if (string.IsNullOrEmpty(this.view.RoomName.Value))
                {
                    var playerName = this.clientState.GetLocalPlayerFullName();
                    if (playerName == null)
                    {
                        this.logger.Error("Player name is null, cannot autofill private room name.");
                        return;
                    }
                    this.view.RoomName.Value = playerName;
                }
                this.serverConection.JoinPrivateRoom(this.view.RoomName.Value, this.view.RoomPassword.Value);
            }
        });

        this.view.LeaveRoom.Subscribe(_ => this.serverConection.LeaveRoom(false).SafeFireAndForget(ex => this.logger.Error(ex.ToString())));
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

}
