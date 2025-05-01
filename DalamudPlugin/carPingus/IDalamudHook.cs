using System;

namespace carPingus;

public interface IDalamudHook : IDisposable
{
    void HookToDalamud();
}
