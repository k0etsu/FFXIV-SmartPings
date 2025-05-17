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

        // Collision nodes that nodes are mapped to
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

        PartyList1CollisionNode = 31,
        PartyList2CollisionNode = 32,
        PartyList3CollisionNode = 33,
        PartyList4CollisionNode = 34,
        PartyList5CollisionNode = 35,
        PartyList6CollisionNode = 36,
        PartyList7CollisionNode = 37,
        PartyList8CollisionNode = 38,
        PartyList9CollisionNode = 39,

        // Non-collision nodes that are mapped to a node
        PartyList1Hp = 41,
        PartyList2Hp = 42,
        PartyList3Hp = 43,
        PartyList4Hp = 44,
        PartyList5Hp = 45,
        PartyList6Hp = 46,
        PartyList7Hp = 47,
        PartyList8Hp = 48,
        PartyList9Hp = 49,

        PartyList1Mp = 51,
        PartyList2Mp = 52,
        PartyList3Mp = 53,
        PartyList4Mp = 54,
        PartyList5Mp = 55,
        PartyList6Mp = 56,
        PartyList7Mp = 57,
        PartyList8Mp = 58,
        PartyList9Mp = 59,
    }

    public struct HudElement
    {
        public HudSection HudSection;
        public uint Index; // 0-indexed
    }

    public IReadOnlyDictionary<nint, HudElement> CollisionNodeMap => this.collisionNodeMap;
    public IReadOnlyDictionary<HudElement, nint> ElementNodeMap => this.elementNodeMap;

    private readonly IGameGui gameGui;
    private readonly ILogger logger;

    private readonly Dictionary<nint, HudElement> collisionNodeMap = [];
    private readonly List<nint> partyListStatusNodes = [];
    private readonly Dictionary<HudElement, nint> elementNodeMap = [];

    private bool enhancementsLoaded;
    private bool enfeeblementsLoaded;
    private bool otherLoaded;
    private bool conditionalEnhancementsLoaded;
    private bool partyListLoaded;

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
        // The status icon nodes in the party list are prone to changing, so load the party list every time
        // Also, these status icon nodes may not always be in the StatusIcons array, except for the first node
        foreach (var n in this.partyListStatusNodes)
        {
            this.collisionNodeMap.Remove(n);
        }
        this.partyListStatusNodes.Clear();
        for (var i = 0; i < partyList->PartyMembers.Length && i < 8; i++)
        {
            var partyMember = partyList->PartyMembers[i];
            var statusIconNode = partyMember.StatusIcons[0].Value;
            uint j = 0;
            while (statusIconNode != null)
            {
                if (statusIconNode->AtkResNode != null)
                {
                    var nodePtr = (nint)statusIconNode->AtkResNode;
                    this.collisionNodeMap.TryAdd(nodePtr, new()
                    {
                        HudSection = HudSection.PartyList1Status + i,
                        Index = j,
                    });
                    this.partyListStatusNodes.Add(nodePtr);
                }
                var ownerNode = statusIconNode->OwnerNode;
                if (ownerNode == null) { break; }
                var nextSiblingNode = ownerNode->NextSiblingNode;
                if (nextSiblingNode == null) { break; }
                statusIconNode = (AtkComponentIconText*)nextSiblingNode->GetComponent();
                j++;
            }

            if (!this.partyListLoaded)
            {
                var partyCollisionNode = partyMember.PartyMemberComponent->UldManager.RootNode;
                if (partyCollisionNode != null)
                {
                    this.collisionNodeMap.TryAdd((nint)partyCollisionNode, new()
                    {
                        HudSection = HudSection.PartyList1CollisionNode + i,
                    });
                }
                if (partyMember.HPGaugeBar != null && partyMember.HPGaugeBar->OwnerNode != null)
                {
                    this.elementNodeMap.TryAdd(new()
                    {
                        HudSection = HudSection.PartyList1Hp + i,
                    },
                    (nint)partyMember.HPGaugeBar->OwnerNode);
                }
                if (partyMember.MPGaugeBar != null && partyMember.MPGaugeBar->OwnerNode != null)
                {
                    this.elementNodeMap.TryAdd(new()
                    {
                        HudSection = HudSection.PartyList1Mp + i,
                    },
                    (nint)partyMember.MPGaugeBar->OwnerNode);
                }
            }
        }
        this.partyListLoaded = true;
    }

    public void Unload()
    {
        this.collisionNodeMap.Clear();
        this.partyListStatusNodes.Clear();
        this.enhancementsLoaded = false;
        this.enfeeblementsLoaded = false;
        this.otherLoaded = false;
        this.conditionalEnhancementsLoaded = false;
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

    public bool TryGetHudElementNode(HudElement hudElement, out nint nodeAddress)
    {
        Load();
        return this.ElementNodeMap.TryGetValue(hudElement, out nodeAddress);
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
