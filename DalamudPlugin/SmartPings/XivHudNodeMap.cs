using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SmartPings.Log;
using System.Collections.Generic;

namespace SmartPings;

public unsafe class XivHudNodeMap
{
    public enum HudSection
    {
        None = 0,
        StatusEnhancements = 1,
        StatusEnfeeblements = 2,
        StatusOther = 3,
        StatusConditionalEnhancements = 4,

        TargetStatus = 10,
        ForcusTargetStatus = 11,

        PartyList1Status = 21,
        PartyList2Status = 22,
        PartyList3Status = 23,
        PartyList4Status = 24,
        PartyList5Status = 25,
        PartyList6Status = 26,
        PartyList7Status = 27,
        PartyList8Status = 28,
        PartyList9Status = 29,

        PartyList1Hp = 31,
        PartyList2Hp = 32,
        PartyList3Hp = 33,
        PartyList4Hp = 34,
        PartyList5Hp = 35,
        PartyList6Hp = 36,
        PartyList7Hp = 37,
        PartyList8Hp = 38,
        PartyList9Hp = 39,

        PartyList1Mp = 41,
        PartyList2Mp = 42,
        PartyList3Mp = 43,
        PartyList4Mp = 44,
        PartyList5Mp = 45,
        PartyList6Mp = 46,
        PartyList7Mp = 47,
        PartyList8Mp = 48,
        PartyList9Mp = 49,
    }

    public struct HudElement
    {
        public HudSection HudSection;
        public uint Index; // 0-indexed
    }

    public IReadOnlyDictionary<nint, HudElement> CollisionNodeMap => this.collisionNodeMap;

    public bool IsLoaded => this.CollisionNodeMap.Count > 0;

    private readonly IGameGui gameGui;
    private readonly ILogger logger;

    private readonly Dictionary<nint, HudElement> collisionNodeMap = [];

    public XivHudNodeMap(
        IGameGui gameGui,
        ILogger logger)
    {
        this.gameGui = gameGui;
        this.logger = logger;
    }

    public void Load()
    {
        this.collisionNodeMap.Clear();

        var statusEnhancements = (AtkUnitBase*)gameGui.GetAddonByName("_StatusCustom0");
        if (statusEnhancements == null)
        {
            this.logger.Error("Could not load _StatusCustom0 addon.");
            return;
        }
        for (uint i = 2; i <= 21; i++)
        {
            var componentNode = statusEnhancements->GetComponentByNodeId(i);
            if (componentNode == null || componentNode->AtkResNode == null) { continue; }
            collisionNodeMap.TryAdd((nint)componentNode->AtkResNode, new()
            {
                HudSection = HudSection.StatusEnhancements,
                Index = i - 2,
            });
        }

        foreach (var n in collisionNodeMap)
        {
            this.logger.Info("Node {0} -> {1}:{2}", n.Key.ToString("X"), n.Value.HudSection, n.Value.Index);
        }
    }

    public bool TryGetAsHudElement(nint nodeAddress, out HudElement hudElement)
    {
        return this.CollisionNodeMap.TryGetValue(nodeAddress, out hudElement);
    }

    public bool TryGetAsHudElement(AtkResNode* nodePtr, out HudElement hudElement)
    {
        return this.CollisionNodeMap.TryGetValue((nint)nodePtr, out hudElement);
    }

    public bool ConditionalEnhancementsEnabled()
    {
        var addon = (AtkUnitBase*)this.gameGui.GetAddonByName("_StatusCustom3");
        if (addon == null) { return false; }
        return addon->IsVisible;
    }
}
