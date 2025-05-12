using Dalamud.Utility;
using System.Collections.Generic;

namespace SmartPings;

public readonly struct StatusSheet
{
    public struct Status
    {
        public uint Id;
        public string Name;
        public string Description;
        public uint Icon;
        public int ParamModifier;
        public uint VfxRowId;
        public ushort Log;
        public byte Unknown0;
        public byte MaxStacks;
        public byte StatusCategory;
        public byte PartyListPriority;
        public byte CanIncreaseRewards;
        public byte ParamEffect;
        public byte TargetType;
        public byte Flags;
        public byte Flag2;
        public byte Unknown_70_1;
        public sbyte Unknown2;
        public bool LockMovement;
        public bool LockActions;
        public bool LockControl;
        public bool Transfiguration;
        public bool IsGaze;
        public bool CanDispel;
        public bool InflictedByActor;
        public bool IsPermanent;
        public bool CanStatusOff;
        public bool IsFcBuff;
        public bool Invisibility;
    }

    public int Count => this.statusesById.Count;

    private readonly Dictionary<uint, Status> statusesById = [];
    private readonly Dictionary<uint, Status> statusesByIcon = [];

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
                ParamModifier = status.ParamModifier,
                VfxRowId = status.VFX.RowId,
                Log = status.Log,
                Unknown0 = status.Unknown0,
                MaxStacks = status.MaxStacks,
                StatusCategory = status.StatusCategory,
                PartyListPriority = status.PartyListPriority,
                CanIncreaseRewards = status.CanIncreaseRewards,
                ParamEffect = status.ParamEffect,
                TargetType = status.TargetType,
                Flags = status.Flags,
                Flag2 = status.Flag2,
                Unknown_70_1 = status.Unknown_70_1,
                Unknown2 = status.Unknown2,
                LockMovement = status.LockMovement,
                LockActions = status.LockActions,
                LockControl = status.LockControl,
                Transfiguration = status.Transfiguration,
                IsGaze = status.IsGaze,
                CanDispel = status.CanDispel,
                InflictedByActor = status.InflictedByActor,
                IsPermanent = status.IsPermanent,
                CanStatusOff = status.CanStatusOff,
                IsFcBuff = status.IsFcBuff,
                Invisibility = status.Invisibility,
            };

            this.statusesById.Add(status.RowId, statusStruct);
            this.statusesByIcon.TryAdd(status.Icon, statusStruct);
        }
    }

    public bool TryGetStatusById(uint id, out Status status)
    {
        return this.statusesById.TryGetValue(id, out status);
    }

    public bool TryGetStatusByIcon(uint icon, out Status status)
    {
        return this.statusesByIcon.TryGetValue(icon, out status);
    }
}
