using System.Text.Json;
using System.Text.Json.Serialization;
using ReplayTool.Application;
using ReplayTool.Application.Interfaces;
using ReplayTool.Application.UseCases;
using ReplayTool.Domain.Entities;
using ReplayTool.Domain.Events;

namespace ReplayTool.Tests;

public class InsertCustomerOrdersUseCaseTests
{
    private const string StorageRoot = "cases";
    private const string LocalDb = "Host=localhost;Database=jobservice;Username=postgres;Password=postgres";

    private readonly Guid _caseId = Guid.NewGuid();

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private (Mock<IFileStorage> Storage, Mock<IJobServiceRepository> Repo, InsertCustomerOrdersUseCase Sut)
        Build(string customerOrderJson)
    {
        var caseJson = JsonSerializer.Serialize(new Case
        {
            Id = _caseId,
            Name = "test",
            Status = CaseStatus.Draft,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, _serializerOptions);

        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        storage.Setup(s => s.ReadFileAsync(It.Is<string>(p => p.EndsWith("case.json")))).ReturnsAsync(caseJson);
        storage.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("JobService-CustomerOrder.Topic")))).ReturnsAsync(true);
        storage.Setup(s => s.ReadFileAsync(It.Is<string>(p => p.Contains("JobService-CustomerOrder.Topic")))).ReturnsAsync(customerOrderJson);

        var repo = new Mock<IJobServiceRepository>();
        repo.Setup(r => r.OrderExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        repo.Setup(r => r.UpsertJobAsync(It.IsAny<JobData>())).ReturnsAsync(1);
        repo.Setup(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), It.IsAny<int>())).Returns(Task.CompletedTask);

        var sut = new InsertCustomerOrdersUseCase(
            storage.Object, StorageRoot, repo.Object, LocalDb, allowRemoteDb: false);

        return (storage, repo, sut);
    }

    [Fact]
    public async Task SingleEvent_WithJobData_InsertsSuccessfully()
    {
        var evt = MakeEvent("order-1", withJob: true);
        var (_, repo, sut) = Build(JsonSerializer.Serialize(evt));

        var steps = await sut.ExecuteAsync(_caseId);

        steps.Should().ContainSingle(s => s.OrderId == "order-1" && s.Result == SeedStepResult.Inserted);
        repo.Verify(r => r.UpsertJobAsync(It.IsAny<JobData>()), Times.Once);
        repo.Verify(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), 1), Times.Once);
    }

    [Fact]
    public async Task DuplicateOrderId_InFile_SecondIsSkipped()
    {
        var evt = MakeEvent("order-dup", withJob: true);
        var (_, repo, sut) = Build(JsonSerializer.Serialize(new[] { evt, evt }));

        var steps = await sut.ExecuteAsync(_caseId);

        steps.Should().HaveCount(2);
        steps.Should().ContainSingle(s => s.Result == SeedStepResult.Inserted);
        steps.Should().ContainSingle(s => s.Result == SeedStepResult.Skipped && s.Error!.Contains("Duplicate"));
        repo.Verify(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task OrderAlreadyInDatabase_IsSkipped()
    {
        var evt = MakeEvent("order-exists", withJob: true);
        var (_, repo, sut) = Build(JsonSerializer.Serialize(evt));
        repo.Setup(r => r.OrderExistsAsync("order-exists")).ReturnsAsync(true);

        var steps = await sut.ExecuteAsync(_caseId);

        steps.Should().ContainSingle(s =>
            s.Result == SeedStepResult.Skipped && s.Error!.Contains("database"));
        repo.Verify(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task EventWithoutJobData_ExistingJob_LooksUpByBusinessId()
    {
        var evt = MakeEvent("order-2", withJob: false);
        var (_, repo, sut) = Build(JsonSerializer.Serialize(evt));
        repo.Setup(r => r.FindJobPkAsync(evt.JobId)).ReturnsAsync(42);

        var steps = await sut.ExecuteAsync(_caseId);

        steps.Should().ContainSingle(s => s.Result == SeedStepResult.Inserted);
        repo.Verify(r => r.FindJobPkAsync(evt.JobId), Times.Once);
        repo.Verify(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), 42), Times.Once);
    }

    [Fact]
    public async Task EventWithoutJobData_JobNotFound_StepFails()
    {
        var evt = MakeEvent("order-3", withJob: false);
        var (_, repo, sut) = Build(JsonSerializer.Serialize(evt));
        repo.Setup(r => r.FindJobPkAsync(It.IsAny<Guid>())).ReturnsAsync((int?)null);

        var steps = await sut.ExecuteAsync(_caseId);

        steps.Should().ContainSingle(s => s.Result == SeedStepResult.Failed);
        repo.Verify(r => r.InsertCustomerOrderAsync(It.IsAny<CustomerOrderEvent>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task NonLocalDb_WithoutOverride_ThrowsInvalidOperationException()
    {
        var storage = new Mock<IFileStorage>();
        var repo = new Mock<IJobServiceRepository>();
        var sut = new InsertCustomerOrdersUseCase(
            storage.Object, StorageRoot, repo.Object,
            "Host=prod.example.com;Database=jobservice;Username=postgres;Password=secret",
            allowRemoteDb: false);

        var act = () => sut.ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not local*");
    }

    private static CustomerOrderEvent MakeEvent(string orderId, bool withJob)
    {
        var jobId = Guid.NewGuid();
        return new CustomerOrderEvent
        {
            OrderId = orderId,
            JobId = jobId,
            TenantId = Guid.NewGuid(),
            CreatedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Job = withJob ? new JobData
            {
                JobId = jobId,
                TenantId = Guid.NewGuid(),
                CreatedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            } : null
        };
    }
}
