using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace SmartPings.Input;

public class KeyStateWrapper : IDisposable
{
    public event Action<VirtualKey>? OnKeyUp;
    public event Action<VirtualKey>? OnKeyDown;

    private readonly IKeyState keyState;
    private readonly IFramework framework;

    private readonly Dictionary<VirtualKey, bool> keyStates = [];

    public KeyStateWrapper(
        IKeyState keyState,
        IFramework framework)
    {
        this.keyState = keyState;
        this.framework = framework;

        this.framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        GC.SuppressFinalize(this);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        foreach (var key in this.keyState.GetValidVirtualKeys())
        {
            var keyState = this.keyState.GetRawValue(key) != 0;
            if (!this.keyStates.TryGetValue(key, out var oldState))
            {
                this.keyStates.Add(key, keyState);
            }
            else
            {
                if (oldState != keyState)
                {
                    this.keyStates[key] = keyState;
                    if (keyState)
                    {
                        this.OnKeyDown?.Invoke(key);
                    }
                    else
                    {
                        this.OnKeyUp?.Invoke(key);
                    }
                }
            }
        }
    }
}
