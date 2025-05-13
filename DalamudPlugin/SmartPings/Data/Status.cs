namespace SmartPings.Data;

public struct Status
{
    public uint Id;
    public string Name;
    public string Description;
    public uint Icon;
    //public int ParamModifier;
    //public uint VfxRowId;
    //public ushort Log;
    //public byte Unknown0;
    public byte MaxStacks;
    public byte StatusCategory;
    public byte PartyListPriority;
    public byte CanIncreaseRewards;
    //public byte ParamEffect;
    //public byte TargetType;
    //public byte Flags;
    //public byte Flag2;
    //public byte Unknown_70_1;
    //public sbyte Unknown2;
    //public bool LockMovement;
    //public bool LockActions;
    //public bool LockControl;
    //public bool Transfiguration;
    //public bool IsGaze;
    //public bool CanDispel;
    //public bool InflictedByActor;
    //public bool IsPermanent;
    //public bool CanStatusOff;
    public bool IsFcBuff;
    //public bool Invisibility;

    public float RemainingTime;
    public string OwnerName;
    public bool IsOwnEnhancement;

    public readonly bool IsEnhancement => StatusCategory == 1 && CanIncreaseRewards == 0;
    public readonly bool IsEnfeeblement => StatusCategory == 2;
    public readonly bool IsOther => StatusCategory == 1 && CanIncreaseRewards == 1;
    public readonly bool IsConditionalEnhancement => StatusCategory == 1 && CanIncreaseRewards == 2;
}

public enum StatusType
{
    None = 0,

    SelfEnhancement = 1,
    SelfEnfeeblement = 2,
    SelfOther = 3,
    SelfConditionalEnhancement = 4,

    PartyListStatus = 10,
}
