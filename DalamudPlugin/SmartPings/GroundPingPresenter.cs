using System;
using System.Collections.Generic;
using SmartPings.Extensions;
using SmartPings.Network;
using SmartPings.UI.Presenter;
using SmartPings.UI.View;

namespace SmartPings;

public class GroundPingPresenter : IPluginUIPresenter
{
    public IPluginUIView View => this.view;

    public LinkedList<GroundPing> GroundPings = [];

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
            this.GroundPings.AddLast(ping);
            this.serverConnection.SendGroundPing(ping);
        });
    }
}
