using System;
using WindowsInput.Events;

namespace LolPings.Input;

public class InputManager
{
    private readonly Configuration configuration;
    private readonly InputEventSource inputEventSource;
    private readonly IAudioDeviceController audioDeviceController;

    private bool listenerSubscribed;

    public InputManager(
        Configuration configuration,
        InputEventSource inputEventSource,
        IAudioDeviceController audioDeviceController)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));
        this.audioDeviceController = audioDeviceController ?? throw new ArgumentNullException(nameof(audioDeviceController));

        UpdateListeners();
    }

    public void UpdateListeners()
    {
    }

    private void OnInputKeyDown(KeyDown k)
    {
        //if (k.Key == this.configuration.MuteMicKeybind)
        //{
        //    this.audioDeviceController.MuteMic = !this.audioDeviceController.MuteMic;
        //}
        //if (k.Key == this.configuration.DeafenKeybind)
        //{
        //    this.audioDeviceController.Deafen = !this.audioDeviceController.Deafen;
        //}
    }
}
