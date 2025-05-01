using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using LolPings.Extensions;
using LolPings.Network;
using LolPings.UI.Presenter;
using LolPings.UI.Util;
using Reactive.Bindings;
using System;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

namespace LolPings.UI.View;

public class MainWindow : Window, IPluginUIView, IDisposable
{
    // this extra bool exists for ImGui, since you can't ref a property
    private bool visible = false;
    public bool Visible
    {
        get => this.visible;
        set => this.visible = value;
    }

    public IReactiveProperty<bool> PublicRoom { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<string> RoomName { get; } = new ReactiveProperty<string>(string.Empty);
    public IReactiveProperty<string> RoomPassword { get; } = new ReactiveProperty<string>(string.Empty);

    private readonly Subject<Unit> joinRoom = new();
    public IObservable<Unit> JoinRoom => joinRoom.AsObservable();
    private readonly Subject<Unit> leaveRoom = new();
    public IObservable<Unit> LeaveRoom => leaveRoom.AsObservable();

    private readonly WindowSystem windowSystem;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly ServerConnection serverConnection;
    private readonly MapManager mapChangeHandler;
    private readonly Configuration configuration;
    private readonly ConfigWindowPresenter configWindowPresenter;
    private readonly IClientState clientState;

    private string? createPrivateRoomButtonText;

    public MainWindow(
        WindowSystem windowSystem,
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        ServerConnection serverConnection,
        MapManager mapChangeHandler,
        Configuration configuration,
        ConfigWindowPresenter configWindowPresenter,
        IClientState clientState) : base(
        PluginInitializer.Name)
    {
        this.windowSystem = windowSystem;
        this.pluginInterface = pluginInterface;
        this.textureProvider = textureProvider;
        this.serverConnection = serverConnection;
        this.mapChangeHandler = mapChangeHandler;
        this.configuration = configuration;
        this.configWindowPresenter = configWindowPresenter;
        this.clientState = clientState;
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            this.createPrivateRoomButtonText = null;
            return;
        }

        var width = 350;
        ImGui.SetNextWindowSize(new Vector2(width, 400), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 250), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("LolPings", ref this.visible))
        {
            DrawContents();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        windowSystem.RemoveWindow(this);
        GC.SuppressFinalize(this);
    }

    private void DrawContents()
    {
        using var tabs = ImRaii.TabBar("lp-tabs");
        if (!tabs) return;

        using (var iconFont = ImRaii.PushFont(UiBuilder.IconFont))
        {
            var gearIcon = FontAwesomeIcon.Cog.ToIconString();
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - ImGuiHelpers.GetButtonSize(gearIcon).X);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5);
            if (ImGui.Button(gearIcon)) this.configWindowPresenter.View.Visible = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Configuration");
        }

        DrawPublicTab();
        DrawPrivateTab();

        //var indent = 10;
        //ImGui.Indent(indent);

        //ImGui.Indent(-indent);

        //ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        if (this.serverConnection.InRoom)
        {
            DrawServerRoom();
            ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------
        }
    }

    private void DrawPublicTab()
    {
        using var publicTab = ImRaii.TabItem("Public room");
        if (!publicTab) return;

        this.PublicRoom.Value = true;

        ImGui.BeginDisabled(!this.serverConnection.ShouldBeInRoom);
        ImGui.Text(string.Format("Room ID: {0}", this.mapChangeHandler.GetCurrentMapPublicRoomName()));
        ImGui.EndDisabled();

#if DEBUG
        unsafe
        {
            ImGui.Text(string.Format("(DEBUG) Territory type: {0}", ((TerritoryIntendedUseEnum)FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance()->CurrentTerritoryIntendedUseId).ToString()));
        }
#endif

        ImGui.BeginDisabled(this.serverConnection.ShouldBeInRoom);
        if (ImGui.Button("Join Public Room")) 
        {
            this.joinRoom.OnNext(Unit.Default);
        }
        ImGui.EndDisabled();

        var dcMsg = this.serverConnection.Channel?.LatestServerDisconnectMessage;
        if (dcMsg != null)
        {
            ImGui.SameLine();
            using var c = ImRaii.PushColor(ImGuiCol.Text, Vector4Colors.Red);
            ImGui.Text("Unknown error (see /xllog)");
        }
    }

    private void DrawPrivateTab()
    {
        using var privateTab = ImRaii.TabItem("Private room");
        if (!privateTab) return;

        this.PublicRoom.Value = false;

        ImGuiInputTextFlags readOnlyIfInRoom = this.serverConnection.InRoom ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;
        string roomName = this.RoomName.Value;
         
        if (ImGui.InputText("Room Name", ref roomName, 100, ImGuiInputTextFlags.AutoSelectAll | readOnlyIfInRoom)) 
        {
            this.RoomName.Value = roomName;
        }
        ImGui.SameLine(); Common.HelpMarker("Leave blank to join your own room");

        string roomPassword = this.RoomPassword.Value;
        ImGui.PushItemWidth(38);
        if (ImGui.InputText("Room Password (up to 4 digits)", ref roomPassword, 4, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll | readOnlyIfInRoom))
        {
            this.RoomPassword.Value = roomPassword;
        }
        ImGui.PopItemWidth();
        if (!ImGui.IsItemActive())
        {
            while (roomPassword.Length < 4)
            {
                roomPassword = "0" + roomPassword;
            }
            this.RoomPassword.Value = roomPassword;
        }
        ImGui.SameLine(); Common.HelpMarker("Sets the password if joining your own room");

        ImGui.BeginDisabled(this.serverConnection.InRoom);
        if (this.createPrivateRoomButtonText == null || !this.serverConnection.InRoom)
        {
            var playerName = this.clientState.GetLocalPlayerFullName();
            this.createPrivateRoomButtonText = roomName.Length == 0 || roomName == playerName ?
                "Create Private Room" : "Join Private Room";
        }
        if (ImGui.Button(this.createPrivateRoomButtonText))
        {
            this.joinRoom.OnNext(Unit.Default);
        }
        ImGui.EndDisabled();

        var dcMsg = this.serverConnection.Channel?.LatestServerDisconnectMessage;
        if (dcMsg != null)
        {
            ImGui.SameLine();
            using var c = ImRaii.PushColor(ImGuiCol.Text, Vector4Colors.Red);
            // this is kinda scuffed but will do for now
            if (dcMsg.Contains("incorrect password"))
            {
                ImGui.Text("Incorrect password");
            }
            else if (dcMsg.Contains("room does not exist"))
            {
                ImGui.Text("Room not found");
            }
            else
            {
                ImGui.Text("Unknown error (see /xllog)");
            }
        }
    }

    private void DrawServerRoom()
    {
        ImGui.AlignTextToFramePadding();
        var roomName = this.serverConnection.Channel?.RoomName;
        if (string.IsNullOrEmpty(roomName) || roomName.StartsWith("public"))
        {
            ImGui.Text("Public Room");
        }
        else
        {
            ImGui.Text($"{roomName}'s Room");
        }
        if (this.serverConnection.ShouldBeInRoom)
        {
            ImGui.SameLine();
            if (ImGui.Button("Leave"))
            {
                this.leaveRoom.OnNext(Unit.Default);
            }
        }

        var indent = 10;
        ImGui.Indent(indent);

        foreach (var (playerName, index) in this.serverConnection.PlayersInRoom.Select((p, i) => (p, i)))
        {
            Vector4 color = Vector4Colors.Red;
            string tooltip = "Connection Error";
            bool connected = false;

            // Assume first player is always the local player
            if (index == 0)
            {
                var channel = this.serverConnection.Channel;
                if (channel != null)
                {
                    if (channel.Connected)
                    {
                        color = Vector4Colors.Green;
                        tooltip = "Connected";
                        connected = true;
                    }
                    else if (channel.Connecting)
                    {
                        color = Vector4Colors.Orange;
                        tooltip = "Connecting";
                    }
                }
            }
            else
            {
                // Other players are always connected
                color = Vector4Colors.Green;
                tooltip = "Connected";
                connected = true;
            }

            // Highlight row on hover
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var h = ImGui.GetTextLineHeightWithSpacing();
            var rowMin = new Vector2(ImGui.GetWindowPos().X, pos.Y);
            var rowMax = new Vector2(rowMin.X + ImGui.GetWindowWidth(), pos.Y + h);
            if (ImGui.IsMouseHoveringRect(rowMin, rowMax))
            {
                drawList.AddRectFilled(rowMin, rowMax, ImGui.ColorConvertFloat4ToU32(Vector4Colors.Gray));
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup($"peer-menu-{index}");
                }
            }
            using (var popup = ImRaii.Popup($"peer-menu-{index}"))
            {
                if (popup)
                {
                    ImGui.Text(playerName);
                }
            }

            // Connectivity/activity indicator
            var radius = 0.3f * h;
            pos += new Vector2(0, h / 2f);
            if (index == 0)
            {
                if (connected)
                {
                    drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
                else
                {
                    drawList.AddCircle(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
            }
            else
            {
                if (connected)
                {
                    drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
                else
                {
                    drawList.AddCircle(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
            }
            // Tooltip
            if (Vector2.Distance(ImGui.GetMousePos(), pos) < radius)
            {
                ImGui.SetTooltip(tooltip);
            }
            pos += new Vector2(radius + 3, -h / 2.25f);
            ImGui.SetCursorScreenPos(pos);

            // Player Label
            var playerLabel = new StringBuilder(playerName);
            ImGui.Text(playerLabel.ToString());
        }

        ImGui.Indent(-indent);
    }
}
