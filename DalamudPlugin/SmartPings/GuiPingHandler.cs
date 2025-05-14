using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
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
using System.Text;

namespace SmartPings;

public unsafe class GuiPingHandler
{
    private const ushort BLUE = 542;
    private const ushort LIGHT_BLUE = 529;
    private const ushort YELLOW = 25;
    private const ushort GREEN = 43;
    private const ushort RED = 518;

    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly Chat chat;
    private readonly XivHudNodeMap hudNodeMap;
    private readonly ServerConnection serverConnection;
    private readonly Configuration configuration;
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
        ILogger logger)
    {
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.chatGui = chatGui;
        this.framework = framework;
        this.chat = chat;
        this.hudNodeMap = hudNodeMap;
        this.serverConnection = serverConnection;
        this.configuration = configuration;
        this.logger = logger;
    }

    public bool TryPingUi()
    {
        var collisionNode = AtkStage.Instance()->AtkCollisionManager->IntersectingCollisionNode;

        if (collisionNode == null) { return false; }
        // World UI such as Nameplates have this flag
        if (collisionNode->NodeFlags.HasFlag(NodeFlags.UseDepthBasedPriority)) { return false; }

        this.logger.Debug("Mouse over collision node {0}", ((nint)collisionNode).ToString("X"));

        this.framework.Run(() =>
        {
            if (TryGetStatusOfCollisionNode(collisionNode, out var foundStatus))
            {
                var echoMsg = new SeStringBuilder();
                var chatMsg = new StringBuilder();

                // Source name -------------
                var localPlayerName = GetLocalPlayerName();
                echoMsg.AddUiForeground($"({localPlayerName}) ", BLUE);

                // Target name -------------
                if (!foundStatus.IsOnSelf)
                {
                    echoMsg.AddUiForeground($"{foundStatus.OwnerName}: ", LIGHT_BLUE);

                    chatMsg.Append($"{foundStatus.OwnerName}: ");
                }

                // Status name --------------
                echoMsg.AddStatusLink(foundStatus.Id);
                // This is how status links are normally constructed
                echoMsg.AddUiForeground(500);
                echoMsg.AddUiGlow(501);
                echoMsg.Append(SeIconChar.LinkMarker.ToIconString());
                echoMsg.AddUiGlowOff();
                echoMsg.AddUiForegroundOff();
                if (foundStatus.IsEnfeeblement)
                {
                    echoMsg.AddUiForeground(518);
                    echoMsg.Append(SeIconChar.Debuff.ToIconString());
                    echoMsg.AddUiForegroundOff();
                }
                else
                {
                    echoMsg.AddUiForeground(517);
                    echoMsg.Append(SeIconChar.Buff.ToIconString());
                    echoMsg.AddUiForegroundOff();
                }
                echoMsg.AddUiForeground($"{foundStatus.Name}", foundStatus.IsEnfeeblement ? RED : YELLOW);
                echoMsg.Append([RawPayload.LinkTerminator]);

                chatMsg.Append("<status>");

                if (foundStatus.MaxStacks > 0)
                {
                    echoMsg.AddUiForeground($" x{foundStatus.Stacks}", foundStatus.IsEnfeeblement ? RED : YELLOW);

                    chatMsg.Append($" x{foundStatus.Stacks}");
                }

                // Timer ---------------
                if (foundStatus.RemainingTime > 0)
                {
                    echoMsg.AddUiForeground(" - ", YELLOW);
                    var remainingTime = foundStatus.RemainingTime >= 1 ?
                        MathF.Floor(foundStatus.RemainingTime).ToString() :
                        foundStatus.RemainingTime.ToString("F1");
                    echoMsg.AddUiForeground($"{remainingTime}s", GREEN);

                    chatMsg.Append($" - {remainingTime}s");
                }

                if (this.configuration.SendGuiPingsToXivChat)
                {
                    AgentChatLog.Instance()->ContextStatusId = foundStatus.Id;
                    // This method must be called on a framework thread or else XIV will crash.
                    this.chat.SendMessage(chatMsg.ToString());
                }

                if (this.configuration.SendGuiPingsToCustomServer)
                {
                    var xivMsg = new XivChatEntry
                    {
                        Type = XivChatType.Echo,
                        Message = echoMsg.Build(),
                    };
                    this.chatGui.Print(xivMsg);

                    this.serverConnection.SendChatMessage(xivMsg);
                }
            }
        });

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
                        status.IsOnSelf = true;
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
                        status.IsOnSelf = true;
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
                        status.IsOnSelf = true;
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
                        status.IsOnSelf = true;
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
                    var partyMember = AgentHUD.Instance()->PartyMembers[partyMemberIndex];
                    var statuses = partyMember.Object->StatusManager.Status;
                    if (TryGetStatus(statuses, StatusType.PartyListStatus, hudElement.Index, out status))
                    {
                        status.OwnerName = partyMember.Name.ExtractText();
                        status.IsOnSelf = partyMemberIndex == 0;
                        return true;
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
            var luminaStatuses = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(this.clientState.ClientLanguage);
            if (!luminaStatuses.TryGetRow(s.StatusId, out var luminaStatus)) { continue; }
            var statusInfo = new Status(luminaStatus)
            {
                SourceIsSelf = s.SourceId == localPlayerId,
                Stacks = s.Param,
            };

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
                    if (isOthersEnhancementsDisplayedInOthers && !statusInfo.SourceIsSelf) { continue; }
                    break;

                case StatusType.SelfEnfeeblement:
                    if (!statusInfo.IsEnfeeblement) { continue; }
                    break;

                case StatusType.SelfOther:
                    // Enhancements applied by others are treated as Other if the HUD config option is set
                    if (!statusInfo.IsOther &&
                        (!isOthersEnhancementsDisplayedInOthers || statusInfo.SourceIsSelf))
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
            sortedStatuses = sortedStatuses.ThenByDescending(s => s.SourceIsSelf);
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
