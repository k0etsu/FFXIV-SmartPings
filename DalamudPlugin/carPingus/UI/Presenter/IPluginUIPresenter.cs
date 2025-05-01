using carPingus.UI.View;

namespace carPingus.UI.Presenter;

public interface IPluginUIPresenter
{
    IPluginUIView View { get; }

    void SetupBindings();
}
