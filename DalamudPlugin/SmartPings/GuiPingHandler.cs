using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SmartPings.Data;
using SmartPings.Log;
using SmartPings.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartPings;

public unsafe class GuiPingHandler
{
    private const ushort BLUE = 542;
    private const ushort LIGHT_BLUE = 529;
    private const ushort YELLOW = 25;
    private const ushort GREEN = 43;
    private const ushort RED = 518;

    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly Chat chat;
    private readonly XivHudNodeMap hudNodeMap;
    private readonly ServerConnection serverConnection;
    private readonly Configuration configuration;
    private readonly StatusSheet statusSheet;
    private readonly ILogger logger;

    private readonly List<Status> statuses = [];

    public GuiPingHandler(
        IClientState clientState,
        IDataManager dataManager,
        IChatGui chatGui,
        IFramework framework,
        Chat chat,
        XivHudNodeMap hudNodeMap,
        ServerConnection serverConnection,
        Configuration configuration,
        StatusSheet statusSheet,
        ILogger logger)
    {
        this.chatGui = chatGui;
        this.framework = framework;
        this.chat = chat;
        this.hudNodeMap = hudNodeMap;
        this.serverConnection = serverConnection;
        this.configuration = configuration;
        this.statusSheet = statusSheet;
        this.logger = logger;
    }

    public bool TryPingUi()
    {
        var collisionNode = AtkStage.Instance()->AtkCollisionManager->IntersectingCollisionNode;

        if (collisionNode == null) { return false; }
        // World UI such as Nameplates have this flag
        if (collisionNode->NodeFlags.HasFlag(NodeFlags.UseDepthBasedPriority)) { return false; }

        this.logger.Debug("Mouse over collision node {0}", ((nint)collisionNode).ToString("X"));

        if (TryGetStatusOfCollisionNode(collisionNode, out var foundStatus))
        {
            var msg = new SeStringBuilder();

            // Target name
            var localPlayerName = GetLocalPlayerName();
            if (localPlayerName != foundStatus.OwnerName)
            {
                msg.AddUiForeground($"{foundStatus.OwnerName}: ", LIGHT_BLUE);
            }

            // Status name
            msg.AddUiForeground($"{foundStatus.Name}", foundStatus.IsEnfeeblement ? RED : YELLOW);

            // Timer
            if (foundStatus.RemainingTime > 0)
            {
                msg.AddUiForeground(" - ", YELLOW);
                var remainingTime = foundStatus.RemainingTime >= 1 ?
                    MathF.Floor(foundStatus.RemainingTime).ToString() :
                    foundStatus.RemainingTime.ToString("F1");
                msg.AddUiForeground($"{remainingTime}s", GREEN);
            }

            if (this.configuration.SendGuiPingsToXivChat)
            {
                var msgString = msg.ToString();
                this.framework.Run(() =>
                {
                    // This method must be called on a framework thread or else XIV will crash.
                    this.chat.SendMessage(msgString);
                });
            }

            if (this.configuration.SendGuiPingsToCustomServer)
            {
                msg = new SeStringBuilder()
                    .AddUiForeground($"({localPlayerName}) ", BLUE)
                    .Append(msg.Build());

                var xivMsg = new XivChatEntry
                {
                    Type = XivChatType.Echo,
                    Message = msg.Build(),
                };
                this.chatGui.Print(xivMsg);

                this.serverConnection.SendChatMessage(xivMsg);
            }
        }

        return true;
    }

    private string GetLocalPlayerName()
    {
        // Accessing IClientState in a non-framework thread will crash XivAlexander, so this is a
        // different way of getting the local player name

        if (0 < AgentHUD.Instance()->PartyMemberCount)
        {
            return AgentHUD.Instance()->PartyMembers[0].Name.ExtractText();
        }
        return string.Empty;
    }

    private uint GetLocalPlayerId()
    {
        // Accessing IClientState in a non-framework thread will crash XivAlexander, so this is a
        // different way of getting the local player id
        if (0 < AgentHUD.Instance()->PartyMemberCount)
        {
            return AgentHUD.Instance()->PartyMembers[0].EntityId;
        }
        return default;
    }

    // To determine what status was clicked on, we need to go from AtkImageNode (inherits AtkCollisionNode) to Status information.
    // An AtkImageNode of a status only holds the image used for the status.
    // One tested method to retrieve status from AtkImageNode is to find the status by image name.
    // However, this does not work for all statuses, as stackable statuses use a different image per stack count,
    // and a search using a stack-image fails to find the original status.
    // So, the method employed here is to use the address of the clicked AtkImageNode.
    // The game instantiates every UI slot that a status can go in, and then sets the visibility and texture of
    // each specific slot that a status should go in when statuses update.
    // The game also holds the statuses each character has in arrays, but not necessarily in the order that they are displayed in the UI.
    // We can, however, reconstruct the order they're expected to go in the UI,
    // as it's been found through testing that statuses are displayed first in PartyListPriority, and then in array order.
    // The final solution then, is to create a map of all UI nodes that are expected to hold some status to
    // exactly the status that should be in that UI node.
    // Upon clicking one of these UI nodes, we can determine what status should belong in there given the
    // existing statuses and their predicted display order, and pull the status information from the character status arrays.

    private bool TryGetStatusOfCollisionNode(AtkCollisionNode* collisionNode,
        out Status status)
    {
        status = default;

        // Search for the node in our node map to determine if it's a relevant HUD element
        if (!this.hudNodeMap.TryGetAsHudElement((nint)collisionNode, out var hudElement)) { return false; }

        switch (hudElement.HudSection)
        {
            case XivHudNodeMap.HudSection.StatusEnhancements:
                if (0 < AgentHUD.Instance()->PartyMemberCount)
                {
                    var character = AgentHUD.Instance()->PartyMembers[0];
                    var statuses = character.Object->StatusManager.Status;
                    // Find the corresponding status given our predicted UI display order
                    if (TryGetStatus(statuses, StatusType.SelfEnhancement, hudElement.Index, out status))
                    {
                        status.OwnerName = character.Name.ExtractText();
                        return true;
                    }
                }
                break;

            case XivHudNodeMap.HudSection.StatusEnfeeblements:
                if (0 < AgentHUD.Instance()->PartyMemberCount)
                {
                    var character = AgentHUD.Instance()->PartyMembers[0];
                    var statuses = character.Object->StatusManager.Status;
                    if (TryGetStatus(statuses, StatusType.SelfEnfeeblement, hudElement.Index, out status))
                    {
                        status.OwnerName = character.Name.ExtractText();
                        return true;
                    }
                }
                break;

            case XivHudNodeMap.HudSection.StatusOther:
                if (0 < AgentHUD.Instance()->PartyMemberCount)
                {
                    var character = AgentHUD.Instance()->PartyMembers[0];
                    var statuses = character.Object->StatusManager.Status;
                    if (TryGetStatus(statuses, StatusType.SelfOther, hudElement.Index, out status))
                    {
                        status.OwnerName = character.Name.ExtractText();
                        return true;
                    }
                }
                break;

            case XivHudNodeMap.HudSection.StatusConditionalEnhancements:
                if (0 < AgentHUD.Instance()->PartyMemberCount)
                {
                    var character = AgentHUD.Instance()->PartyMembers[0];
                    var statuses = character.Object->StatusManager.Status;
                    if (TryGetStatus(statuses, StatusType.SelfConditionalEnhancement, hudElement.Index, out status))
                    {
                        status.OwnerName = character.Name.ExtractText();
                        return true;
                    }
                }
                break;

            case XivHudNodeMap.HudSection.PartyList1Status:
            case XivHudNodeMap.HudSection.PartyList2Status:
            case XivHudNodeMap.HudSection.PartyList3Status:
            case XivHudNodeMap.HudSection.PartyList4Status:
            case XivHudNodeMap.HudSection.PartyList5Status:
            case XivHudNodeMap.HudSection.PartyList6Status:
            case XivHudNodeMap.HudSection.PartyList7Status:
            case XivHudNodeMap.HudSection.PartyList8Status:
            case XivHudNodeMap.HudSection.PartyList9Status:
                var partyMemberIndex = hudElement.HudSection - XivHudNodeMap.HudSection.PartyList1Status;
                if (partyMemberIndex < AgentHUD.Instance()->PartyMemberCount)
                {
                    // Find party member by UI index
                    foreach (var partyMember in AgentHUD.Instance()->PartyMembers)
                    {
                        if (partyMember.Index == partyMemberIndex)
                        {
                            var statuses = partyMember.Object->StatusManager.Status;
                            if (TryGetStatus(statuses, StatusType.PartyListStatus, hudElement.Index, out status))
                            {
                                status.OwnerName = partyMember.Name.ExtractText();
                                return true;
                            }
                        }
                    }
                }
                break;
        }

        return false;
    }

    private bool TryGetStatus(Span<FFXIVClientStructs.FFXIV.Client.Game.Status> allStatuses, StatusType type, uint index,
        out Status status)
    {
        status = default;

        // Early out cases
        // Cannot search for a conditional enhancement if conditional enhancements are not enabled
        if (type == StatusType.SelfConditionalEnhancement && !this.hudNodeMap.IsConditionalEnhancementsEnabled()) { return false; }

        this.statuses.Clear();

        var isConditionalEnhancementsEnabled = this.hudNodeMap.IsConditionalEnhancementsEnabled();
        var isOwnEnhancementsPrioritized = this.hudNodeMap.IsOwnEnhancementsPrioritized();
        var isOthersEnhancementsDisplayedInOthers = this.hudNodeMap.IsOthersEnhancementsDisplayedInOthers();
        var localPlayerId = GetLocalPlayerId();

        // Fill status list with relevant statuses to sort
        foreach (var s in allStatuses)
        {
            if (s.StatusId == 0) { continue; }
            if (!this.statusSheet.TryGetStatusById(s.StatusId, out var statusInfo)) { continue; }

            statusInfo.IsOwnEnhancement = s.SourceId == localPlayerId;

            // Intentionally putting switch inside foreach instead of outside for code clarity
            switch (type)
            {
                case StatusType.SelfEnhancement:
                    // Conditional Enhancements are treated as Enhancements if their Addon is disabled
                    if (!statusInfo.IsEnhancement &&
                        (!statusInfo.IsConditionalEnhancement || isConditionalEnhancementsEnabled))
                    {
                        continue;
                    }
                    // Enhancements applied by others are treated as Other if the HUD config option is set
                    if (isOthersEnhancementsDisplayedInOthers && !statusInfo.IsOwnEnhancement) { continue; }
                    break;

                case StatusType.SelfEnfeeblement:
                    if (!statusInfo.IsEnfeeblement) { continue; }
                    break;

                case StatusType.SelfOther:
                    // Enhancements applied by others are treated as Other if the HUD config option is set
                    if (!statusInfo.IsOther &&
                        (!isOthersEnhancementsDisplayedInOthers || statusInfo.IsOwnEnhancement))
                    {
                        continue;
                    }
                    break;

                case StatusType.SelfConditionalEnhancement:
                    if (!statusInfo.IsConditionalEnhancement) { continue; }
                    break;

                case StatusType.PartyListStatus:
                    // Other statuses are not displayed in the party list
                    if (statusInfo.IsOther) { continue; }
                    break;
            }

            statusInfo.RemainingTime = s.RemainingTime;
            this.statuses.Add(statusInfo);
        }

        var sortedStatuses = this.statuses.OrderByDescending(s => s.PartyListPriority);
        if (isOwnEnhancementsPrioritized && type == StatusType.SelfEnhancement)
        {
            sortedStatuses = sortedStatuses.ThenByDescending(s => s.IsOwnEnhancement);
        }
        if (isOthersEnhancementsDisplayedInOthers && type == StatusType.SelfOther)
        {
            sortedStatuses = sortedStatuses.ThenBy(s => s.IsOther);
        }

        var i = 0;
        foreach (var s in sortedStatuses)
        {
            if (index == i)
            {
                status = s;
                return true;
            }
            else
            {
                i++;
            }
        }

        return false;
    }
}
