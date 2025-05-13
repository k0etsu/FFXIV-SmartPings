using Dalamud.Utility;
using System.Collections.Generic;

namespace SmartPings;

public readonly struct StatusSheet
{
    public int Count => this.statusesById.Count;

    private readonly Dictionary<uint, Status> statusesById = [];

    public StatusSheet(Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Status> statuses)
    {
        if (statuses == null) { return; }

        foreach (var status in statuses)
        {
            var statusStruct = new Status
            {
                Id = status.RowId,
                Name = status.Name.ToDalamudString().TextValue,
                Description = status.Description.ToDalamudString().TextValue,
                Icon = status.Icon,
                //ParamModifier = status.ParamModifier,
                //VfxRowId = status.VFX.RowId,
                //Log = status.Log,
                //Unknown0 = status.Unknown0,
                MaxStacks = status.MaxStacks,
                StatusCategory = status.StatusCategory,
                PartyListPriority = status.PartyListPriority,
                CanIncreaseRewards = status.CanIncreaseRewards,
                //ParamEffect = status.ParamEffect,
                //TargetType = status.TargetType,
                //Flags = status.Flags,
                //Flag2 = status.Flag2,
                //Unknown_70_1 = status.Unknown_70_1,
                //Unknown2 = status.Unknown2,
                //LockMovement = status.LockMovement,
                //LockActions = status.LockActions,
                //LockControl = status.LockControl,
                //Transfiguration = status.Transfiguration,
                //IsGaze = status.IsGaze,
                //CanDispel = status.CanDispel,
                //InflictedByActor = status.InflictedByActor,
                //IsPermanent = status.IsPermanent,
                //CanStatusOff = status.CanStatusOff,
                IsFcBuff = status.IsFcBuff,
                //Invisibility = status.Invisibility,
            };

            this.statusesById.Add(status.RowId, statusStruct);
        }
    }

    public bool TryGetStatusById(uint id, out Status status)
    {
        return this.statusesById.TryGetValue(id, out status);
    }
}
