using System;
using LolPings.Extensions;
using LolPings.Network;
using LolPings.UI.Presenter;
using LolPings.UI.View;

namespace LolPings;

public class GroundPingPresenter : IPluginUIPresenter
{
    public IPluginUIView View => this.view;

    public SwapbackList<GroundPing> GroundPings = [];

    private readonly GroundPingView view;
    private readonly ServerConnection serverConnection;

    public GroundPingPresenter(
        GroundPingView view,
        ServerConnection serverConnection)
    {
        this.view = view;
        this.serverConnection = serverConnection;
    }

    public void SetupBindings()
    {
        BindActions();
    }

    private void BindActions()
    {
        this.view.AddGroundPing.Subscribe(ping =>
        {
            this.GroundPings.Add(ping);
            this.serverConnection.SendGroundPing(ping);
        });
    }
}
