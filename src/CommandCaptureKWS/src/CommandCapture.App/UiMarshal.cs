using Avalonia.Threading;

namespace CommandCapture.App;

public interface IUiMarshal
{
    void Post(Action action);
}

public sealed class AvaloniaUiMarshal : IUiMarshal
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

public sealed class DirectUiMarshal : IUiMarshal
{
    public void Post(Action action) => action();
}
