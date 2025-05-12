using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using SmartPings.Log;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartPings;

public unsafe class UiPingHandler
{
    private readonly IChatGui chatGui;
    private readonly ILogger logger;
    private readonly XivHudNodeMap hudNodeMap;

    private readonly StatusSheet statusSheet;

    public UiPingHandler(
        IClientState clientState,
        IDataManager dataManager,
        IChatGui chatGui,
        XivHudNodeMap hudNodeMap,
        ILogger logger)
    {
        this.chatGui = chatGui;
        this.hudNodeMap = hudNodeMap;
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

        this.logger.Info("Mouse over collision node {0}", ((nint)collisionNode).ToString("X"));

        // Statuses here are sorted first by PartyListPriority, and then by time-acquired
        //foreach (var statusId in AgentHUD.Instance()->Status->StatusIds)
        //{
        //    if (statusId == 0) { continue; }
        //    this.logger.Info("AgentHUD: Status id {0}", statusId);
        //    if (this.statusSheet.TryGetStatusById(statusId, out var status))
        //    {
        //        this.logger.Info("AgentHUD: Status {0}", JsonConvert.SerializeObject(status).ToString());
        //    }
        //}
        // Same order as HUDAgent, doesn't provide extra info
        //HudNumberArray.Instance()->StatusIconIds
        // Seemingly random statuses, not useful
        //Hud2NumberArray.Instance()->TargetStatusIconIds

        // On the UI, own statuses are always sorted by PartyListPriority then time-acquired
        // _StatusCustom0 contains Enhancements, which have StatusCategory 1, CanIncreaseRewards 0
        // _StatusCustom1 contains Enfeeblements, which have StatusCategory 2
        // _StatusCustom2 contains Other, which have StatusCategory 1, CanIncreaseRewards 1
        // _StatusCustom3 contains Conditional Enhancements, which have StatusCategory 1, CanIncreaseRewards 2
        // If _StatusCustom3 does not exist, statuses that would normally go there go to _StatusCustom0 instead

        if (TryGetStatusOfCollisionNode(collisionNode, out var foundStatus, out var ownerName))
        {
            this.logger.Info("Collision node is for status {0}", foundStatus.Name);
            var msg = new StringBuilder($"{ownerName}: {foundStatus.Name}");
            if (foundStatus.RemainingTime > 0)
            {
                msg.Append($", {foundStatus.RemainingTime:F2} seconds remaining");
            }
            chatGui.Print(msg.ToString());
        }

        // The PartyMembers array always has 10 slots, but accessing an index at or above PartyMemberCount
        // will crash XivAlexander
        for (var i = 0; i < AgentHUD.Instance()->PartyMemberCount; i++)
        {
            var partyMember = AgentHUD.Instance()->PartyMembers[i];
            // These include Other statuses
            // These seem randomly sorted, but statuses with the same PartyListPriority are
            // sorted relative to each other
            foreach (var status in partyMember.Object->StatusManager.Status)
            {
                if (status.StatusId == 0) { continue; }
                this.statusSheet.TryGetStatusById(status.StatusId, out var s);
                this.logger.Info("Party member {0}, index {1}, has status {2}",
                   partyMember.Object->NameString, partyMember.Index, JsonConvert.SerializeObject(s).ToString());
            }
        }

        // IClientState.LocalPlayer must be accessed in a Framework thread (or else XivAlexander will crash)
        //framework.Run(() =>
        //{
        //    if (this.clientState.LocalPlayer != null)
        //    {
        //        // These statuses are sorted in time-acquired-order
        //        for (var i = 0; i < this.clientState.LocalPlayer.StatusList.Length; i++)
        //        {
        //            var playerStatus = this.clientState.LocalPlayer.StatusList[i];
        //            if (playerStatus != null && playerStatus.StatusId > 0 &&
        //                this.statusSheet.TryGetStatusById(playerStatus.StatusId, out var status))
        //            {
        //                this.logger.Info("Player has status with index {0}, id {1}, name {2}, icon {3}", i, playerStatus.StatusId, status.Name, status.Icon);
        //            }
        //        }
        //    }
        //    // IGameGui.HoveredAction does not update to 0 when not hovering over anything
        //});
        return true;
    }

    private bool TryGetStatusOfCollisionNode(AtkCollisionNode* collisionNode,
        out Status status, out string? ownerName)
    {
        status = default;
        ownerName = null;
        //if (collisionNode == null) { return false; }
        ////if (this.clientState.LocalPlayer == null) { return false; }

        //var imageNode = collisionNode->GetAsAtkImageNode();
        //if (imageNode == null) { return false; }

        //var componentNode = collisionNode->ParentNode;
        //if (componentNode == null) { return false; }

        //// Found through testing: _StatusCustom component nodes are of Type 1001
        //if ((ushort)componentNode->Type != 1001) { return false; }

        //var partsList = imageNode->PartsList; if (partsList == null) { return false; }
        //var parts = partsList->Parts; if (parts == null) { return false; }
        //var uldAsset = parts->UldAsset; if (uldAsset == null) { return false; }
        //var resource = uldAsset->AtkTexture.Resource; if (resource == null) { return false; }
        //var iconId = resource->IconId;
        //this.logger.Info("Collision node icon id is {0}", iconId);

        if (!this.hudNodeMap.IsLoaded) { this.hudNodeMap.Load(); }

        if (!this.hudNodeMap.TryGetAsHudElement((nint)collisionNode, out var hudElement)) { return false; }

        switch (hudElement.HudSection)
        {
            case XivHudNodeMap.HudSection.StatusEnhancements:
                if (0 < AgentHUD.Instance()->PartyMemberCount)
                {
                    var character = AgentHUD.Instance()->PartyMembers[0];
                    var statuses = character.Object->StatusManager.Status;
                    ownerName = character.Name.ExtractText();
                    if (TryGetStatus(statuses, StatusType.Enhancement, hudElement.Index, out status)) { return true; }
                }
                break;
        }

        //if (this.statusSheet.TryGetStatusByIcon(iconId, out var status))
        //{
        //    this.logger.Info("Collision node status id is {0}", status.Id);
        //    name = status.Name;
        //    return true;
        //}

        return false;
    }

    private bool TryGetStatus(System.Span<FFXIVClientStructs.FFXIV.Client.Game.Status> allStatuses, StatusType type, uint index,
        out Status status)
    {
        status = default;

        var statuses = new List<Status>();
        foreach (var s in allStatuses)
        {
            if (this.statusSheet.TryGetStatusById(s.StatusId, out var sheetStatus))
            {
                sheetStatus.RemainingTime = s.RemainingTime;
                statuses.Add(sheetStatus);
            }
        }

        var sortedStatuses = statuses.OrderByDescending(s => s.PartyListPriority);
        var i = 0;
        switch (type)
        {
            case StatusType.Enhancement:
                foreach (var s in sortedStatuses)
                {
                    // Other
                    if (s.CanIncreaseRewards == 1) { continue; }
                    // Conditional Enhancements
                    if (s.CanIncreaseRewards == 2 && this.hudNodeMap.ConditionalEnhancementsEnabled()) { continue; }
                    // Unknown
                    if (s.CanIncreaseRewards > 2) { continue; }
                    // Debuffs/Unknown
                    if (s.StatusCategory != 1) { continue; }

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
                break;
        }

        return false;
    }
}
