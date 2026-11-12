using CommandCapture.Voice;

namespace CommandCapture.Voice.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task UnknownCommand_IsDropped()
    {
        var handler = new RecordingHandler("capture_still");
        var dispatcher = Create(handler, capacity: 4);
        var messages = new List<string>();
        dispatcher.Status += messages.Add;

        var running = dispatcher.RunAsync(CancellationToken.None);
        Assert.False(dispatcher.TrySubmit(Command("other")));
        dispatcher.Complete();
        await running;

        Assert.Empty(handler.Calls);
        Assert.Contains(messages, message => message.Contains("unknown command other", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cooldown_DropsTheSecondCommand()
    {
        var handler = new RecordingHandler("capture_still");
        var clock = new ManualClock();
        var dispatcher = Create(handler, capacity: 4, clock);
        var messages = new List<string>();
        dispatcher.Status += messages.Add;

        var running = dispatcher.RunAsync(CancellationToken.None);
        Assert.True(dispatcher.TrySubmit(Command("capture_still")));
        Assert.True(dispatcher.TrySubmit(Command("capture_still")));
        dispatcher.Complete();
        await running;

        Assert.Equal(["capture_still"], handler.Calls);
        Assert.Contains(messages, message => message.Contains("during cooldown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BusyQueue_DropsWhenFull()
    {
        var handler = new RecordingHandler("capture_still")
        {
            Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var dispatcher = Create(handler, capacity: 1);
        var messages = new List<string>();
        dispatcher.Status += messages.Add;

        var running = dispatcher.RunAsync(CancellationToken.None);
        Assert.True(dispatcher.TrySubmit(Command("capture_still")));
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(dispatcher.TrySubmit(Command("capture_still")));
        Assert.False(dispatcher.TrySubmit(Command("capture_still")));
        handler.Release.SetResult();
        dispatcher.Complete();
        await running;

        Assert.Contains(messages, message => message.Contains("while busy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandlerException_DoesNotStopTheNextCommand()
    {
        var first = new RecordingHandler("capture_still") { Throw = true };
        var second = new RecordingHandler("start_recording");
        var catalog = new CommandCatalog(
        [
            new VoiceDirective("capture_still", ["capture image"], 1),
            new VoiceDirective("start_recording", ["start recording"], 1),
        ]);
        var dispatcher = new CommandDispatcher(catalog, [first, second], new ManualClock());
        var messages = new List<string>();
        dispatcher.Status += messages.Add;

        var running = dispatcher.RunAsync(CancellationToken.None);
        Assert.True(dispatcher.TrySubmit(Command("capture_still")));
        Assert.True(dispatcher.TrySubmit(Command("start_recording")));
        dispatcher.Complete();
        await running;

        Assert.Equal(["start_recording"], second.Calls);
        Assert.Contains(messages, message => message.Contains("capture_still failed: boom", StringComparison.Ordinal));
    }

    private static CommandDispatcher Create(RecordingHandler handler, int capacity, IClock? clock = null)
    {
        var catalog = new CommandCatalog([new VoiceDirective(handler.CommandName, ["phrase"], 800)]);
        return new CommandDispatcher(catalog, [handler], clock ?? new ManualClock(), capacity);
    }

    private static VoiceCommand Command(string name) =>
        new(name, name, 1f, DateTimeOffset.UnixEpoch, VoiceCommand.EmptySlots);

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class RecordingHandler : IVoiceDirectiveHandler
    {
        public RecordingHandler(string name) => CommandName = name;

        public string CommandName { get; }

        public List<string> Calls { get; } = [];

        public bool Throw { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? Release { get; set; }

        public async Task HandleAsync(VoiceCommand command, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Release is not null)
            {
                await Release.Task;
            }

            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            Calls.Add(command.Name);
        }
    }
}
