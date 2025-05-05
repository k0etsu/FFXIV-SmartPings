using System;

namespace SmartPings;

public interface IDalamudHook : IDisposable
{
    void HookToDalamud();
}
