﻿using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using NLog;
using SmartPings.Data;
using System;

namespace SmartPings
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        // Saved UI inputs
        public bool PublicRoom { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public string RoomPassword { get; set; } = string.Empty;

        public int SelectedAudioOutputDeviceIndex { get; set; } = -1;

        public VirtualKey PingKeybind { get; set; } = VirtualKey.G;
        public VirtualKey QuickPingKeybind { get; set; } = VirtualKey.CONTROL;
        public VirtualKey QuickerPingKeybind { get; set; } = VirtualKey.X;

        public bool EnableGroundPings { get; set; } = true;
        public bool EnablePingWheel { get; set; } = true;
        public GroundPing.Type DefaultGroundPingType { get; set; } = GroundPing.Type.Basic;

        public bool EnableGuiPings { get; set; } = true;
        public bool EnableHpMpPings { get; set; } = true;
        public bool SendGuiPingsToCustomServer { get; set; } = true;
        public bool SendGuiPingsToXivChat { get; set; }
        public XivChatSendLocation XivChatSendLocation { get; set; } = XivChatSendLocation.Active;

        public float MasterVolume { get; set; } = 2.0f;

        public bool PlayRoomJoinAndLeaveSounds { get; set; } = true;
        public bool KeybindsRequireGameFocus { get; set; }
        public bool PrintLogsToChat { get; set; }

        public int MinimumVisibleLogLevel { get; set; } = LogLevel.Info.Ordinal;

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
