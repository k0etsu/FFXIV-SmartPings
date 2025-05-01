using System;
using System.Reactive.Disposables;

namespace carPingus.Extensions;

public static class ObservableExtensions
{
    public static void DisposeWith(this IDisposable disposable, CompositeDisposable disposables) => 
        disposables.Add(disposable);
}
