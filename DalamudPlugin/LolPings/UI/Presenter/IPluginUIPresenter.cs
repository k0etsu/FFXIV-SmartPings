using LolPings.UI.View;

namespace LolPings.UI.Presenter;

public interface IPluginUIPresenter
{
    IPluginUIView View { get; }

    void SetupBindings();
}
