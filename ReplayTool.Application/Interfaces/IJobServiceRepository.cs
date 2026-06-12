using ReplayTool.Domain.Events;

namespace ReplayTool.Application.Interfaces;

public interface IJobServiceRepository
{
    Task<bool> OrderExistsAsync(string orderId);
    Task<int?> FindJobPkAsync(Guid businessJobId);
    Task<int> UpsertJobAsync(JobData job);
    Task InsertCustomerOrderAsync(CustomerOrderEvent evt, int jobFk);
}
