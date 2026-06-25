using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ReplayTool.Application;
using ReplayTool.Domain.Entities;
using ReplayTool.Domain.Events;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace ReplayTool.Tests.IntegrationTests;

// Proves the full replay loop against real infrastructure: create case -> upload the three
// typed files -> trigger a run -> assert the DB inserts, the topic publishes (in Timestamp
// order), and that the inter-publish gaps approximate the original deltas.
//
// ReplayTool and JobService are independent solutions with no project reference between them,
// so this test does not boot JobService.API. Instead it applies just the Job/CustomerOrder
// columns ReplayTool's Seed phase writes to (mirroring JobService's migration), and observes
// the two published topics with a small standalone MassTransit consumer bus rather than a real
// JobService consumer.
[Trait("Category", "Integration")]
public class ReplayEndToEndTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:latest").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _storageRoot = null!;

    private ServiceProvider _observerProvider = null!;
    private IBusControl _observerBus = null!;

    private readonly ConcurrentQueue<(string Id, DateTime ReceivedAt)> _observed = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
        await ApplyJobServiceSchemaAsync();

        _storageRoot = Path.Combine(Path.GetTempPath(), "replay-e2e-" + Guid.NewGuid());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("STORAGE_ROOT", _storageRoot);
                builder.UseSetting("JOBSERVICE_DB", _postgres.GetConnectionString());
                builder.UseSetting("RabbitMQ:Host", _rabbitMq.Hostname);
                builder.UseSetting("RabbitMQ:Port", _rabbitMq.GetMappedPublicPort(5672).ToString());
                builder.UseSetting("RabbitMQ:Username", "guest");
                builder.UseSetting("RabbitMQ:Password", "guest");
            });

        _client = _factory.CreateClient();

        await StartObserverBusAsync();

        // Give both buses time to finish connecting/binding before the test publishes anything.
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();

        if (_observerBus is not null) await _observerBus.StopAsync();
        if (_observerProvider is not null) await _observerProvider.DisposeAsync();

        await Task.WhenAll(_postgres.StopAsync(), _rabbitMq.StopAsync());

        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public async Task FullReplayLoop_InsertsOrders_PublishesInTimestampOrder_PreservesGaps()
    {
        var tenantId = Guid.NewGuid();

        // --- Create the case ---
        var createResponse = await _client.PostAsJsonAsync("/cases", new { name = "E2E Case" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var @case = await createResponse.Content.ReadFromJsonAsync<CaseResponse>(ApiJsonOptions);
        var caseId = @case!.Id;

        // --- Build and upload the three typed files ---
        // Three customer-order events but only two unique OrderIds — proves dedup end to end.
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var customerOrders = new[]
        {
            BuildCustomerOrder("order-1", jobA, tenantId),
            BuildCustomerOrder("order-2", jobB, tenantId),
            BuildCustomerOrder("order-1", jobA, tenantId), // duplicate OrderId
        };

        // Merged timeline (ascending Timestamp): routing-1 (0ms), assignment-1 (600ms),
        // routing-2 (1200ms), assignment-2 (1800ms).
        var t0 = DateTime.UtcNow;
        var routingEvents = new[]
        {
            BuildRoutingEvent("corr-1", tenantId, t0),
            BuildRoutingEvent("corr-2", tenantId, t0.AddMilliseconds(1200)),
        };
        var assignmentEvents = new[]
        {
            BuildAssignmentEvent("area-1", tenantId, t0.AddMilliseconds(600)),
            BuildAssignmentEvent("area-1", tenantId, t0.AddMilliseconds(1800)),
        };

        var expectedEventIds = new[]
        {
            "routing:corr-1",
            $"assignment:area-1@{t0.AddMilliseconds(600):O}",
            "routing:corr-2",
            $"assignment:area-1@{t0.AddMilliseconds(1800):O}",
        };
        var expectedOffsetsMs = new[] { 0, 600, 1200, 1800 };

        await PutFileAsync(caseId, CaseFileType.CustomerOrder, customerOrders);
        await PutFileAsync(caseId, CaseFileType.RoutingResponses, routingEvents);
        await PutFileAsync(caseId, CaseFileType.AssignmentSolution, assignmentEvents);

        // --- Trigger a real (non-dry) run, exact original gaps ---
        var triggerResponse = await _client.PostAsJsonAsync(
            $"/cases/{caseId}/runs", new { dryRun = false, speedFactor = 1.0, mode = "Normal" });
        triggerResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var triggered = await triggerResponse.Content.ReadFromJsonAsync<Run>(ApiJsonOptions);

        var finalRun = await PollUntilFinishedAsync(caseId, triggered!.Id, TimeSpan.FromSeconds(20));

        // --- Assert the run completed with every step published ---
        finalRun.Status.Should().Be(RunStatus.Completed);

        var replaySteps = finalRun.Steps.Where(s => s.Phase == "Replay").ToList();
        replaySteps.Should().HaveCount(4);
        replaySteps.Should().OnlyContain(s => s.Result == RunStepResult.Published);
        replaySteps.Select(s => s.EventId).Should().Equal(expectedEventIds);

        // --- Assert Phase 1: each unique order produced exactly one CustomerOrder row ---
        var insertedOrderIds = await GetInsertedOrderIdsAsync(tenantId);
        insertedOrderIds.Should().BeEquivalentTo(["order-1", "order-2"]);

        // --- Assert Phase 2: events really arrived on the broker, in Timestamp order ---
        var observedInOrder = await WaitForObservedEventsAsync(expectedEventIds.Length, TimeSpan.FromSeconds(15));
        observedInOrder.Select(o => o.Id).Should().Equal(expectedEventIds);

        // --- Assert timing: inter-publish gaps approximate the original deltas ---
        for (var i = 1; i < observedInOrder.Count; i++)
        {
            var actualGapMs = (observedInOrder[i].ReceivedAt - observedInOrder[0].ReceivedAt).TotalMilliseconds;
            var expectedGapMs = expectedOffsetsMs[i] - expectedOffsetsMs[0];
            actualGapMs.Should().BeApproximately(expectedGapMs, precision: 350,
                because: $"step {i} should fire roughly {expectedGapMs}ms after the first publish");
        }
    }

    // --- Sample data builders ---

    private static CustomerOrderEvent BuildCustomerOrder(string orderId, Guid jobId, Guid tenantId) => new()
    {
        OrderId = orderId,
        JobId = jobId,
        TenantId = tenantId,
        Status = "Active",
        CreatedOn = DateTime.UtcNow,
        Job = new JobData
        {
            JobId = jobId,
            TenantId = tenantId,
            AreaId = "area-1",
            JobType = "Standard",
            CreatedOn = DateTime.UtcNow,
        },
    };

    private static OrdersRoutingEventV2 BuildRoutingEvent(string correlationId, Guid tenantId, DateTime timestamp) => new()
    {
        AreaId = "area-1",
        TenantId = tenantId.ToString(),
        CorrelationId = correlationId,
        Timestamp = timestamp,
        RobotRoutes = new Dictionary<string, OrdersRoutingEventV2.Route>(),
    };

    private static AssignmentSolutionV2Event BuildAssignmentEvent(string areaId, Guid tenantId, DateTime timestamp) => new()
    {
        TenantId = tenantId.ToString(),
        AreaId = areaId,
        Timestamp = timestamp,
        Decisions = [],
    };

    // --- HTTP helpers ---

    private async Task PutFileAsync<T>(Guid caseId, string type, T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PutAsync($"/cases/{caseId}/files/{Uri.EscapeDataString(type)}", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Run> PollUntilFinishedAsync(Guid caseId, Guid runId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/cases/{caseId}/runs/{runId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var run = await response.Content.ReadFromJsonAsync<Run>(ApiJsonOptions);

            if (run!.Status is RunStatus.Completed or RunStatus.Failed)
                return run;

            await Task.Delay(200);
        }

        throw new TimeoutException($"Run {runId} did not finish within {timeout}.");
    }

    // --- DB assertion helper ---

    private async Task<List<string>> GetInsertedOrderIdsAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""OrderId"" FROM ""CustomerOrder"" WHERE ""TenantId"" = @TenantId";
        cmd.Parameters.AddWithValue("TenantId", tenantId);

        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    // --- Schema setup (mirrors JobService's migration for the two tables ReplayTool writes to) ---

    private async Task ApplyJobServiceSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE "Job" (
                "Id" SERIAL PRIMARY KEY,
                "JobId" uuid NOT NULL,
                "AutomationId" uuid NULL,
                "AutomationName" text NULL,
                "AutomationChainingId" integer NULL,
                "AreaId" text NULL,
                "FleetId" uuid NULL,
                "TenantId" uuid NOT NULL,
                "JobType" text NULL,
                "JobPriority" integer NOT NULL DEFAULT 0,
                "Tags" text[] NULL,
                "IsCompleted" boolean NOT NULL DEFAULT false,
                "CreatedBy" text NULL,
                "CreatedOn" timestamptz NOT NULL,
                "UpdatedBy" text NULL,
                "UpdatedOn" timestamptz NULL
            );

            CREATE TABLE "CustomerOrder" (
                "Id" SERIAL PRIMARY KEY,
                "JobId" integer NOT NULL REFERENCES "Job"("Id"),
                "OrderId" text NOT NULL,
                "MissionId" uuid NULL,
                "MissionName" text NULL,
                "Status" text NULL,
                "ProgressionRate" integer NOT NULL DEFAULT 0,
                "OrderType" varchar(21) NULL,
                "Sequence" integer NOT NULL DEFAULT 0,
                "IsTemplate" boolean NOT NULL DEFAULT false,
                "IsCompleted" boolean NOT NULL DEFAULT false,
                "DeviceId" uuid NULL,
                "DeviceName" text NULL,
                "DeviceRegistryName" text NULL,
                "DeviceType" text NULL,
                "AssignedBy" text NULL,
                "AssignmentType" text NULL,
                "AssignedOn" timestamptz NULL,
                "QueuedAt" timestamptz NULL,
                "ReassignmentCount" integer NOT NULL DEFAULT 0,
                "ReassignedDeviceName" text NULL,
                "ReassignedDeviceRegistryName" text NULL,
                "StartTime" timestamptz NULL,
                "EndTime" timestamptz NULL,
                "EstimatedEndTime" timestamptz NULL,
                "EstimatedRemainingDistance" double precision NULL,
                "UpdatedByOrderTrackingOn" timestamptz NULL,
                "CancellationReason" text NULL,
                "CancellationDescription" varchar(240) NULL,
                "Errors" jsonb NULL,
                "OperatingModes" jsonb NULL,
                "BundleId" text NULL,
                "RobotOrderId" text NULL,
                "PreviousRobotOrderIds" text[] NULL,
                "TenantId" uuid NOT NULL,
                "CreatedBy" text NULL,
                "CreatedOn" timestamptz NOT NULL,
                "UpdatedBy" text NULL,
                "UpdatedOn" timestamptz NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Observer bus: a standalone MassTransit consumer that binds to the same two fanout
    // exchanges JobService consumes, so the test can see publishes without booting JobService ---

    private async Task StartObserverBusAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_observed);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<RoutingObserverConsumer>();
            x.AddConsumer<AssignmentObserverConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(_rabbitMq.Hostname, _rabbitMq.GetMappedPublicPort(5672), "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });

                cfg.ReceiveEndpoint("e2e-test-routing-observer", e =>
                {
                    e.Bind("routingResponses/v2");
                    e.ConfigureConsumer<RoutingObserverConsumer>(ctx);
                });

                cfg.ReceiveEndpoint("e2e-test-assignment-observer", e =>
                {
                    e.Bind("anytask/solution/v2");
                    e.ConfigureConsumer<AssignmentObserverConsumer>(ctx);
                });
            });
        });

        _observerProvider = services.BuildServiceProvider(validateScopes: true);
        _observerBus = _observerProvider.GetRequiredService<IBusControl>();
        await _observerBus.StartAsync();
    }

    private async Task<List<(string Id, DateTime ReceivedAt)>> WaitForObservedEventsAsync(int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_observed.Count < expectedCount && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        return _observed.OrderBy(o => o.ReceivedAt).ToList();
    }

    private class RoutingObserverConsumer : IConsumer<OrdersRoutingEventV2>
    {
        private readonly ConcurrentQueue<(string Id, DateTime ReceivedAt)> _sink;
        public RoutingObserverConsumer(ConcurrentQueue<(string Id, DateTime ReceivedAt)> sink) => _sink = sink;

        public Task Consume(ConsumeContext<OrdersRoutingEventV2> context)
        {
            _sink.Enqueue(($"routing:{context.Message.CorrelationId}", DateTime.UtcNow));
            return Task.CompletedTask;
        }
    }

    private class AssignmentObserverConsumer : IConsumer<AssignmentSolutionV2Event>
    {
        private readonly ConcurrentQueue<(string Id, DateTime ReceivedAt)> _sink;
        public AssignmentObserverConsumer(ConcurrentQueue<(string Id, DateTime ReceivedAt)> sink) => _sink = sink;

        public Task Consume(ConsumeContext<AssignmentSolutionV2Event> context)
        {
            _sink.Enqueue(($"assignment:{context.Message.AreaId}@{context.Message.Timestamp:O}", DateTime.UtcNow));
            return Task.CompletedTask;
        }
    }
}
