using System;
using Avalonia.Threading;
using GreenSwamp.Alpaca.Client.ViewModels;

namespace GreenSwamp.Alpaca.Client;

/// <summary>
/// Avalonia-backed <see cref="IUiDispatcher"/> implementation (architecture §11 open item -
/// resolved). Lives in the application project - the only project permitted to reference
/// Avalonia.Threading for this purpose (ViewModels must not depend on Avalonia directly).
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
