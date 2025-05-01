using System;

namespace LolPings;

public interface IDalamudHook : IDisposable
{
    void HookToDalamud();
}
