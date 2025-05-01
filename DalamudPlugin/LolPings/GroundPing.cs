using System.Numerics;

namespace LolPings;

public class GroundPing
{
    public enum Type : int
    {
        None = 0,
        Question = 3,
    }

    public Type PingType;
    public long StartTimestamp;
    public string? Author;
    public string? MapId;
    public Vector3 WorldPosition;
    public float DrawDuration;
}
