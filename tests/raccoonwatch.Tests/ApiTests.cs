using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Raccoonwatch.Api;
using Raccoonwatch.Cli;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.Tests;

/// <summary>Unit tests for the local control API and its companion CLI.</summary>
public sealed class ApiTests
{
    [Fact]
    public void ApiProtocol_RoundTripsRequestAndResponse()
    {
        var id = Guid.NewGuid();
        var request = new ApiRequest
        {
            Method = ApiMethods.Start,
            Id = id.ToString()
        };

        var decoded = ApiProtocol.DeserializeRequest(ApiProtocol.SerializeRequest(request));
        Assert.NotNull(decoded);
        Assert.Equal(ApiMethods.Start, decoded.Method);
        Assert.Equal(id.ToString(), decoded.Id);

        var response = ApiResponse.Ok(new[] { new ProcessInfoDto { Id = id, DisplayName = "Renamed" } });
        var decodedResponse = ApiProtocol.DeserializeResponse(ApiProtocol.SerializeResponse(response))!;
        Assert.True(decodedResponse.Success);
        var items = decodedResponse.GetData<ProcessInfoDto[]>()!;
        Assert.Single(items);
        Assert.Equal(id, items[0].Id);
    }

    [Fact]
    public async Task NamedPipeServerAndClient_ExchangeRequest()
    {
        var pipe = UniquePipe();
        var handler = new EchoHandler();
        using var server = new NamedPipeApiServer(pipe, handler);
        await server.StartAsync();

        var client = new ApiClient(pipe);
        var response = await client.SendAsync(new ApiRequest { Method = ApiMethods.Ping });

        Assert.True(response.Success);
        Assert.Equal(ApiMethods.Ping, handler.LastMethod);
        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task NamedPipeServer_RepliesWithErrorForMalformedRequest()
    {
        var pipe = UniquePipe();
        using var server = new NamedPipeApiServer(pipe, new EchoHandler());
        await server.StartAsync();

        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
        await client.ConnectAsync(3000);
        using (var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true })
        {
            await writer.WriteLineAsync("this is not json");
        }

        using (var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true))
        {
            var response = ApiProtocol.DeserializeResponse(await reader.ReadLineAsync() ?? string.Empty);
            Assert.NotNull(response);
            Assert.False(response.Success);
            Assert.Contains("Unreadable", response.Error, StringComparison.OrdinalIgnoreCase);
        }

        await server.StopAsync();
    }

    [Fact]
    public async Task Handler_ListAndStatus_IncludeStatusAndUptime()
    {
        var id = Guid.NewGuid();
        var settings = new FakeSettings();
        settings.Settings.Processes.Add(Definition(id));
        var processes = new FakeProcessManager();
        processes.States[id] = new RuntimeState
        {
            DefinitionId = id,
            Status = ProcessStatus.Running,
            ProcessId = 1234,
            StartedAt = DateTimeOffset.Now.AddMinutes(-2)
        };
        var handler = CreateHandler(settings, processes);

        var list = await handler.HandleAsync(new ApiRequest { Method = ApiMethods.List });
        var items = list.GetData<ProcessInfoDto[]>()!;
        Assert.Single(items);
        Assert.Equal(ProcessStatus.Running.ToString(), items[0].Status);
        Assert.InRange(items[0].UptimeSeconds!.Value, 100, 140);

        var status = await handler.HandleAsync(
            new ApiRequest { Method = ApiMethods.GetStatus, Id = id.ToString() });
        Assert.True(status.Success);
        Assert.Equal(id, status.GetData<ProcessInfoDto>()!.Id);
    }

    [Fact]
    public async Task Handler_StartStopRestart_DelegateToProcessManager()
    {
        var id = Guid.NewGuid();
        var settings = new FakeSettings();
        settings.Settings.Processes.Add(Definition(id));
        var processes = new FakeProcessManager();
        var handler = CreateHandler(settings, processes);

        await handler.HandleAsync(new ApiRequest { Method = ApiMethods.Start, Id = id.ToString() });
        await handler.HandleAsync(new ApiRequest { Method = ApiMethods.Stop, Id = id.ToString() });
        await handler.HandleAsync(new ApiRequest { Method = ApiMethods.Restart, Id = id.ToString() });

        Assert.Equal([id], processes.Started);
        Assert.Equal([id], processes.Stopped);
        Assert.Equal([id], processes.Restarted);
    }

    [Fact]
    public async Task Handler_WhenProcessManagerFails_ReturnsFailure()
    {
        var id = Guid.NewGuid();
        var settings = new FakeSettings();
        settings.Settings.Processes.Add(Definition(id));
        var processes = new FakeProcessManager { Result = ProcessResult.Failed("Access denied.") };
        var handler = CreateHandler(settings, processes);

        var response = await handler.HandleAsync(
            new ApiRequest { Method = ApiMethods.Start, Id = id.ToString() });

        Assert.False(response.Success);
        Assert.Equal("Access denied.", response.Error);
    }

    [Fact]
    public async Task Handler_InvalidRequests_ReturnFailures()
    {
        var handler = CreateHandler(new FakeSettings(), new FakeProcessManager());

        var unknownMethod = await handler.HandleAsync(new ApiRequest { Method = "explode" });
        var missingId = await handler.HandleAsync(new ApiRequest { Method = ApiMethods.Start });
        var unknownId = await handler.HandleAsync(
            new ApiRequest { Method = ApiMethods.Start, Id = Guid.NewGuid().ToString() });

        Assert.False(unknownMethod.Success);
        Assert.False(missingId.Success);
        Assert.False(unknownId.Success);
        Assert.Contains("not found", unknownId.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_Update_ReportsUpdaterFailure()
    {
        var id = Guid.NewGuid();
        var settings = new FakeSettings();
        settings.Settings.Processes.Add(Definition(id));
        var runner = new FakeUpdateRunner { Result = UpdateRunResult.Failed("Updater crashed.") };
        var handler = CreateHandler(settings, new FakeProcessManager(), runner);

        var response = await handler.HandleAsync(
            new ApiRequest { Method = ApiMethods.Update, Id = id.ToString() });

        Assert.False(response.Success);
        Assert.Equal("Updater crashed.", response.Error);
        Assert.Equal(id, runner.LastId);
    }

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(125, "2m 5s")]
    [InlineData(3725, "1h 2m")]
    [InlineData(90061, "1d 1h")]
    public void UptimeFormatter_ProducesCompactOutput(double seconds, string expected) =>
        Assert.Equal(expected, UptimeFormatter.Format(seconds));

    [Fact]
    public void CliOptions_ParsesValidArguments()
    {
        var id = Guid.NewGuid();
        var options = CliOptions.Parse(["--id", id.ToString(), "--json-output"]);

        Assert.Null(options.Error);
        Assert.Equal(id.ToString(), options.Id);
        Assert.True(options.JsonOutput);
    }

    [Fact]
    public void CliOptions_RejectsInvalidArguments()
    {
        Assert.NotNull(CliOptions.Parse(["--id"]).Error);
        Assert.NotNull(CliOptions.Parse(["--id", "not-a-guid"]).Error);
        Assert.NotNull(CliOptions.Parse(["--unknown"]).Error);
    }

    private static ApiRequestHandler CreateHandler(
        FakeSettings settings,
        FakeProcessManager processes,
        FakeUpdateRunner? runner = null) =>
        new(settings, processes, runner ?? new FakeUpdateRunner(), NullLogger<ApiRequestHandler>.Instance);

    private static ProcessDefinition Definition(Guid id, string name = "Demo") => new()
    {
        Id = id,
        DisplayName = name,
        Kind = ProcessKind.Executable,
        CommandPath = @"C:\tools\demo.exe",
        Arguments = "--flag"
    };

    private static string UniquePipe() => $"rwtest-{Guid.NewGuid():N}";

    private sealed class EchoHandler : IApiRequestHandler
    {
        public string? LastMethod { get; private set; }

        public Task<ApiResponse> HandleAsync(ApiRequest request, CancellationToken cancellationToken = default)
        {
            LastMethod = request.Method;
            return Task.FromResult(ApiResponse.Ok(new { method = request.Method }));
        }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();

        public event EventHandler? Changed;

        public void Load()
        {
        }

        public void Save()
        {
        }

        public IReadOnlyList<string> Validate(ProcessDefinition definition, Guid? ignoreId = null) => [];

        public void AddOrUpdate(ProcessDefinition definition)
        {
            var index = Settings.Processes.FindIndex(p => p.Id == definition.Id);
            if (index >= 0)
            {
                Settings.Processes[index] = definition;
            }
            else
            {
                Settings.Processes.Add(definition);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(Guid id) => Settings.Processes.RemoveAll(p => p.Id == id);
    }

    private sealed class FakeProcessManager : IProcessManager
    {
        public Dictionary<Guid, RuntimeState> States { get; } = [];

        public List<Guid> Started { get; } = [];

        public List<Guid> Stopped { get; } = [];

        public List<Guid> Restarted { get; } = [];

        public ProcessResult Result { get; set; } = ProcessResult.Ok();

        public event EventHandler<ProcessStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public RuntimeState GetState(Guid definitionId) =>
            States.TryGetValue(definitionId, out var state) ? state : RuntimeState.Initial(definitionId);

        public IReadOnlyDictionary<Guid, RuntimeState> GetStates() => States;

        public Task<ProcessResult> StartAsync(ProcessDefinition definition)
        {
            Started.Add(definition.Id);
            return Task.FromResult(Result);
        }

        public Task<ProcessResult> StopAsync(Guid definitionId)
        {
            Stopped.Add(definitionId);
            return Task.FromResult(Result);
        }

        public Task<ProcessResult> RestartAsync(ProcessDefinition definition)
        {
            Restarted.Add(definition.Id);
            return Task.FromResult(Result);
        }

        public Task StartAllAsync(IEnumerable<ProcessDefinition> definitions) => Task.CompletedTask;

        public Task StartAutostartAsync(IEnumerable<ProcessDefinition> definitions) => Task.CompletedTask;

        public Task StopAllAsync() => Task.CompletedTask;

        public Task ShutdownAsync() => Task.CompletedTask;
    }

    private sealed class FakeUpdateRunner : IUpdateCommandRunner
    {
        public UpdateRunResult Result { get; set; } = UpdateRunResult.Ok(0);

        public Guid? LastId { get; private set; }

        public Task<UpdateRunResult> RunAsync(
            ProcessDefinition definition,
            CancellationToken cancellationToken = default)
        {
            LastId = definition.Id;
            return Task.FromResult(Result);
        }
    }
}
