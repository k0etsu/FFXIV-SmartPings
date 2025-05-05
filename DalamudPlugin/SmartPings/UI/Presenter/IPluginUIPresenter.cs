using SmartPings.UI.View;

namespace SmartPings.UI.Presenter;

public interface IPluginUIPresenter
{
    IPluginUIView View { get; }

    void SetupBindings();
}
