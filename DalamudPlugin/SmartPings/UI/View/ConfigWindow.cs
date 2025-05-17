using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Reactive.Bindings;
using SmartPings.Audio;
using SmartPings.Input;
using SmartPings.Log;
using SmartPings.UI.Util;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SmartPings.UI.View;

public class ConfigWindow : Window, IPluginUIView, IDisposable
{
    // this extra bool exists for ImGui, since you can't ref a property
    private bool visible = false;
    public bool Visible
    {
        get => this.visible;
        set => this.visible = value;
    }

    public IReactiveProperty<Keybind> KeybindBeingEdited { get; } = new ReactiveProperty<Keybind>();
    public IObservable<Keybind> ClearKeybind => clearKeybind.AsObservable();
    private readonly Subject<Keybind> clearKeybind = new();

    public IReactiveProperty<bool> EnableGroundPings { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> EnableGuiPings { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> SendGuiPingsToCustomServer { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> SendGuiPingsToXivChat { get; } = new ReactiveProperty<bool>();

    public IObservable<Unit> PrintStatuses => printStatuses.AsObservable();
    private readonly Subject<Unit> printStatuses = new();
    public IObservable<Unit> PrintNodeMap => printNodeMap.AsObservable();
    private readonly Subject<Unit> printNodeMap = new();

    public IReactiveProperty<float> MasterVolume { get; } = new ReactiveProperty<float>();

    public IReactiveProperty<bool> PlayRoomJoinAndLeaveSounds { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> KeybindsRequireGameFocus { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> PrintLogsToChat { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; } = new ReactiveProperty<int>();

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly WindowSystem windowSystem;
    private readonly Configuration configuration;
    private readonly string[] falloffTypes;
    private readonly string[] allLoggingLevels;

    // Direct application logic is being placed into this UI script because this is debug UI
    public ConfigWindow(WindowSystem windowSystem,
        Configuration configuration) : base(
        $"{PluginInitializer.Name} Config")
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.falloffTypes = Enum.GetNames(typeof(AudioFalloffModel.FalloffType));
        this.allLoggingLevels = LogLevel.AllLoggingLevels.Select(l => l.Name).ToArray();
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            KeybindBeingEdited.Value = Keybind.None;
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(350, 400), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(350, 250), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("SmartPings Config", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawContents();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        inputDevices = null;
        outputDevices = null;
        windowSystem.RemoveWindow(this);
        GC.SuppressFinalize(this);
    }

    private void DrawContents()
    {
        using var tabs = ImRaii.TabBar("pvc-config-tabs");
        if (!tabs) return;

        DrawMainTab();
        //DrawFalloffTab();
        DrawMiscTab();
    }

    private void DrawMainTab()
    {
        using var deviceTab = ImRaii.TabItem("Main");
        if (!deviceTab) return;

        //using (var deviceTable = ImRaii.Table("AudioDevices", 2))
        //{
        //    if (deviceTable)
        //    {
        //        ImGui.TableSetupColumn("AudioDevicesCol1", ImGuiTableColumnFlags.WidthFixed, 80);
        //        ImGui.TableSetupColumn("AudioDevicesCol2", ImGuiTableColumnFlags.WidthFixed, 230);

        //        ImGui.TableNextRow(); ImGui.TableNextColumn();
        //        ImGui.AlignTextToFramePadding();
        //        ImGui.Text("Output Device"); ImGui.TableNextColumn();
        //    }
        //}

        //var globalFontScale = ImGui.GetIO().FontGlobalScale;
        ////ImGui.GetIO().FontGlobalScale = 1.5f * globalFontScale;
        ImGui.Text("Keybinds");
        //ImGui.GetIO().FontGlobalScale = globalFontScale;
        ImGui.SameLine(); Common.HelpMarker("Right click to clear a keybind.");
        //DrawKeybindEdit(Keybind.Ping, this.configuration.PingKeybind, "Ping Keybind");
        DrawKeybindEdit(Keybind.QuickPing, this.configuration.QuickPingKeybind, "Quick Ping Keybind",
            "Lefting clicking while holding this keybind will execute a ping.");

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        var enableGroundPings = this.EnableGroundPings.Value;
        if (ImGui.Checkbox("Enable Ground Pings", ref enableGroundPings))
        {
            this.EnableGroundPings.Value = enableGroundPings;
        }

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        var enableGuiPings = this.EnableGuiPings.Value;
        if (ImGui.Checkbox("Enable UI Pings", ref enableGuiPings))
        {
            this.EnableGuiPings.Value = enableGuiPings;
        }

        using (ImRaii.Disabled(!this.EnableGuiPings.Value))
        {
            var sendGuiPingsToCustomServer = this.SendGuiPingsToCustomServer.Value;
            if (ImGui.Checkbox("Send UI pings to joined room", ref sendGuiPingsToCustomServer))
            {
                this.SendGuiPingsToCustomServer.Value = sendGuiPingsToCustomServer;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Sends UI pings as /echo messages to other players in the same plugin room. This avoids sending traceable data to XIV servers.");
            }

            var sendGuiPingsToXivChat = this.SendGuiPingsToXivChat.Value;
            if (ImGui.Checkbox("Send UI pings in game chat (!)", ref sendGuiPingsToXivChat))
            {
                this.SendGuiPingsToXivChat.Value = sendGuiPingsToXivChat;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Sending messages in game chat may be traceable as plugin usage. Use with caution!");
            }
        }

#if DEBUG
        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        ImGui.Text("DEBUG");
        if (ImGui.Button("Print Statuses"))
        {
            this.printStatuses.OnNext(Unit.Default);
        }
        if (ImGui.Button("Print Node Map"))
        {
            this.printNodeMap.OnNext(Unit.Default);
        }
#endif
    }

    private void DrawKeybindEdit(Keybind keybind, VirtualKey currentBinding, string label, string? tooltip = null)
    {
        using var id = ImRaii.PushId($"{keybind} Keybind");
        {
            if (ImGui.Button(this.KeybindBeingEdited.Value == keybind ?
                    "Recording..." :
                    KeyCodeStrings.TranslateKeyCode(currentBinding),
                new Vector2(5 * ImGui.GetFontSize(), 0)))
            {
                this.KeybindBeingEdited.Value = this.KeybindBeingEdited.Value != keybind ?
                    keybind : Keybind.None;
            }
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            this.clearKeybind.OnNext(keybind);
            this.KeybindBeingEdited.Value = Keybind.None;
        }
        ImGui.SameLine();
        ImGui.Text(label);
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private void DrawFalloffTab()
    {
        using var falloffTab = ImRaii.TabItem("Something 2");
        if (!falloffTab) return;
    }
    private void DrawMiscTab()
    {
        using var miscTab = ImRaii.TabItem("Misc");
        if (!miscTab) return;

        var playRoomJoinAndLeaveSounds = this.PlayRoomJoinAndLeaveSounds.Value;
        if (ImGui.Checkbox("Play room join and leave sounds", ref playRoomJoinAndLeaveSounds))
        {
            this.PlayRoomJoinAndLeaveSounds.Value = playRoomJoinAndLeaveSounds;
        }

        var keybindsRequireGameFocus = this.KeybindsRequireGameFocus.Value;
        if (ImGui.Checkbox("Keybinds require game focus", ref keybindsRequireGameFocus))
        {
            this.KeybindsRequireGameFocus.Value = keybindsRequireGameFocus;
        }

        var printLogsToChat = this.PrintLogsToChat.Value;
        if (ImGui.Checkbox("Print logs to chat", ref printLogsToChat))
        {
            this.PrintLogsToChat.Value = printLogsToChat;
        }

        if (printLogsToChat)
        {
            ImGui.SameLine();
            var minLogLevel = this.MinimumVisibleLogLevel.Value;
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("Min log level", ref minLogLevel, allLoggingLevels, allLoggingLevels.Length))
            {
                this.MinimumVisibleLogLevel.Value = minLogLevel;
            }
        }

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Bugs or suggestions?");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.40f, 0.95f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.41f, 0.45f, 1.0f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.32f, 0.36f, 0.88f, 1));
        if (ImGui.Button("Discord"))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/rSucAJ6A7u", UseShellExecute = true });
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1.0f, 0.39f, 0.20f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.49f, 0.30f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.92f, 0.36f, 0.18f, 1));
        if (ImGui.Button("Support on Ko-fi")) {
            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/ricimon", UseShellExecute = true });
        }
        ImGui.PopStyleColor(3);
    }
}
