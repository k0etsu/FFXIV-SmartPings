using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
    private readonly Configuration configuration;
    private readonly ServerConnection serverConnection;
    private readonly ILogger logger;

    private readonly StatusSheet statusSheet;
    private readonly List<Status> statuses = [];

    public GuiPingHandler(
        IClientState clientState,
        IDataManager dataManager,
        IChatGui chatGui,
        IFramework framework,
        Chat chat,
        XivHudNodeMap hudNodeMap,
        Configuration configuration,
        ServerConnection serverConnection,
        ILogger logger)
    {
        this.chatGui = chatGui;
        this.framework = framework;
        this.chat = chat;
        this.hudNodeMap = hudNodeMap;
        this.configuration = configuration;
        this.serverConnection = serverConnection;
        this.logger = logger;

        //XivAlexander will crash if an ExcelSheet instance is accessed outside of the thread it is created in.
        this.statusSheet = new StatusSheet(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(clientState.ClientLanguage));
        if (this.statusSheet.Count == 0)
        {
            this.logger.Error("Could not load Status Excel Sheet. UI pings will not work.");
        }
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
                this.framework.Run(() =>
                {
                    // This method must be called on a framework thread or else XIV will crash.
                    this.chat.SendMessage(msg.ToString());
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

        //// The PartyMembers array always has 10 slots, but accessing an index at or above PartyMemberCount
        //// will crash XivAlexander
        //for (var i = 0; i < AgentHUD.Instance()->PartyMemberCount; i++)
        //{
        //    var partyMember = AgentHUD.Instance()->PartyMembers[i];
        //    // These include Other statuses
        //    // These seem randomly sorted, but statuses with the same PartyListPriority are
        //    // sorted relative to each other
        //    foreach (var status in partyMember.Object->StatusManager.Status)
        //    {
        //        if (status.StatusId == 0) { continue; }
        //        this.statusSheet.TryGetStatusById(status.StatusId, out var s);
        //        this.logger.Info("Party member {0}, index {1}, has status {2}",
        //           partyMember.Object->NameString, partyMember.Index, JsonConvert.SerializeObject(s).ToString());
        //    }
        //}

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

        // Fill status list with relevant statuses to sort
        foreach (var s in allStatuses)
        {
            if (!this.statusSheet.TryGetStatusById(s.StatusId, out var statusInfo)) { continue; }

            // Intentionally putting switch inside foreach instead of outside for code clarity
            switch (type)
            {
                case StatusType.SelfEnhancement:
                    // Conditional Enhancements are treated as Enhancements if their Addon is disabled
                    if (!statusInfo.IsEnhancement &&
                        (!statusInfo.IsConditionalEnhancement || this.hudNodeMap.IsConditionalEnhancementsEnabled()))
                    {
                        continue;
                    }
                    break;

                case StatusType.SelfEnfeeblement:
                    if (!statusInfo.IsEnfeeblement) { continue; }
                    break;

                case StatusType.SelfOther:
                    if (!statusInfo.IsOther) { continue; }
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
