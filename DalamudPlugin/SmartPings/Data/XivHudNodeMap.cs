using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SmartPings.Log;
using System;
using System.Collections.Generic;

namespace SmartPings.Data;

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

    private readonly IGameGui gameGui;
    private readonly ILogger logger;

    private readonly Dictionary<nint, HudElement> collisionNodeMap = [];

    private bool enhancementsLoaded;
    private bool enfeeblementsLoaded;
    private bool otherLoaded;
    private bool conditionalEnhancementsLoaded;
    private int partyListIndicesLoaded;

    public XivHudNodeMap(
        IGameGui gameGui,
        ILogger logger)
    {
        this.gameGui = gameGui;
        this.logger = logger;
    }

    public void Load()
    {
        // Enhancements
        if (!this.enhancementsLoaded)
        {
            var statusEnhancements = (AtkUnitBase*)gameGui.GetAddonByName("_StatusCustom0");
            if (statusEnhancements == null)
            {
                this.logger.Error("Could not load _StatusCustom0 addon.");
                Unload();
                return;
            }
            for (uint i = 2; i <= 21; i++)
            {
                var componentNode = statusEnhancements->GetComponentByNodeId(i);
                if (componentNode == null || componentNode->AtkResNode == null) { continue; }
                this.collisionNodeMap.TryAdd((nint)componentNode->AtkResNode, new()
                {
                    HudSection = HudSection.StatusEnhancements,
                    Index = i - 2,
                });

            }
            this.enhancementsLoaded = true;
        }

        // Enfeeblements
        if (!this.enfeeblementsLoaded)
        {
            var statusEnfeeblements = (AtkUnitBase*)gameGui.GetAddonByName("_StatusCustom1");
            if (statusEnfeeblements == null)
            {
                this.logger.Error("Could not load _StatusCustom1 addon.");
                Unload();
                return;
            }
            for (uint i = 2; i <= 21; i++)
            {
                var componentNode = statusEnfeeblements->GetComponentByNodeId(i);
                if (componentNode == null || componentNode->AtkResNode == null) { continue; }
                this.collisionNodeMap.TryAdd((nint)componentNode->AtkResNode, new()
                {
                    HudSection = HudSection.StatusEnfeeblements,
                    Index = i - 2,
                });
            }
            this.enfeeblementsLoaded = true;
        }

        // Other
        if (!this.otherLoaded)
        {
            var statusOther = (AtkUnitBase*)gameGui.GetAddonByName("_StatusCustom2");
            if (statusOther == null)
            {
                this.logger.Error("Could not load _StatusCustom2 addon.");
                Unload();
                return;
            }
            for (uint i = 2; i <= 21; i++)
            {
                var componentNode = statusOther->GetComponentByNodeId(i);
                if (componentNode == null || componentNode->AtkResNode == null) { continue; }
                this.collisionNodeMap.TryAdd((nint)componentNode->AtkResNode, new()
                {
                    HudSection = HudSection.StatusOther,
                    Index = i - 2,
                });
            }
            this.otherLoaded = true;
        }

        // Conditional Enhancements
        if (!this.conditionalEnhancementsLoaded)
        {
            var statusConditionalEnhancements = (AtkUnitBase*)gameGui.GetAddonByName("_StatusCustom3");
            if (statusConditionalEnhancements == null)
            {
                this.logger.Error("Could not load _StatusCustom3 addon.");
                Unload();
                return;
            }
            for (uint i = 2; i <= 9; i++)
            {
                var componentNode = statusConditionalEnhancements->GetComponentByNodeId(i);
                if (componentNode == null || componentNode->AtkResNode == null) { continue; }
                this.collisionNodeMap.TryAdd((nint)componentNode->AtkResNode, new()
                {
                    HudSection = HudSection.StatusConditionalEnhancements,
                    Index = i - 2,
                });
            }
            this.conditionalEnhancementsLoaded = true;
        }

        // Party List
        var partyList = (AddonPartyList*)gameGui.GetAddonByName("_PartyList");
        if (partyList == null)
        {
            this.logger.Error("Could not load _PartyList addon.");
            Unload();
            return;
        }
        for (var i = 0; i < 8; i++)
        {
            if (this.partyListIndicesLoaded > i) { continue; }

            var partyMember = partyList->PartyMembers[i];
            // PartyMember StatusIcon nodes are not created until the party member exists,
            // so this load will need to be checked every time
            for (var j = 0; j < 10; j++)
            {
                var statusIconNode = partyMember.StatusIcons[j];
                if (statusIconNode.Value == null || statusIconNode.Value->AtkResNode == null) { continue; }
                this.collisionNodeMap.TryAdd((nint)statusIconNode.Value->AtkResNode, new()
                {
                    HudSection = HudSection.PartyList1Status + i,
                    Index = (uint)j,
                });

                // If just one status icon node exists, we assume all nodes are loaded
                this.partyListIndicesLoaded = Math.Max(i + 1, this.partyListIndicesLoaded);
            }
        }

        foreach (var n in this.collisionNodeMap)
        {
            this.logger.Trace("Node {0} -> {1}:{2}", n.Key.ToString("X"), n.Value.HudSection, n.Value.Index);
        }
    }

    public void Unload()
    {
        this.collisionNodeMap.Clear();
        this.enhancementsLoaded = false;
        this.enfeeblementsLoaded = false;
        this.otherLoaded = false;
        this.conditionalEnhancementsLoaded = false;
        this.partyListIndicesLoaded = 0;
    }

    public bool TryGetAsHudElement(nint nodeAddress, out HudElement hudElement)
    {
        Load();
        return this.CollisionNodeMap.TryGetValue(nodeAddress, out hudElement);
    }

    public bool TryGetAsHudElement(AtkResNode* nodePtr, out HudElement hudElement)
    {
        Load();
        return this.CollisionNodeMap.TryGetValue((nint)nodePtr, out hudElement);
    }

    public bool IsConditionalEnhancementsEnabled()
    {
        var addon = (AtkUnitBase*)this.gameGui.GetAddonByName("_StatusCustom3");
        if (addon == null) { return false; }
        return addon->IsVisible;
    }

    public bool IsOwnEnhancementsPrioritized()
    {
        var addon = (AtkUnitBase*)this.gameGui.GetAddonByName("_StatusCustom0");
        if (addon == null) { return false; }
        return addon->Param == 256;
    }

    public bool IsOthersEnhancementsDisplayedInOthers()
    {
        var addon = (AtkUnitBase*)this.gameGui.GetAddonByName("_StatusCustom0");
        if (addon == null) { return false; }
        return addon->Param == 512;
    }
}
