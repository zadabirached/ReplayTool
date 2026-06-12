using Dapper;
using Npgsql;
using NpgsqlTypes;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Events;

namespace ReplayTool.Infrastructure.Database;

public class JobServiceRepository : IJobServiceRepository
{
    private readonly string _connectionString;

    public JobServiceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> OrderExistsAsync(string orderId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(1) FROM ""CustomerOrder"" WHERE ""OrderId"" = @OrderId",
            new { OrderId = orderId });
        return count > 0;
    }

    public async Task<int?> FindJobPkAsync(Guid businessJobId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QuerySingleOrDefaultAsync<int?>(
            @"SELECT ""Id"" FROM ""Job"" WHERE ""JobId"" = @JobId",
            new { JobId = businessJobId });
    }

    public async Task<int> UpsertJobAsync(JobData job)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var existingId = await conn.QuerySingleOrDefaultAsync<int?>(
            @"SELECT ""Id"" FROM ""Job"" WHERE ""JobId"" = @JobId",
            new { job.JobId });

        if (existingId.HasValue)
        {
            await using var cmd = BuildUpdateJobCommand(conn, job, existingId.Value);
            await cmd.ExecuteNonQueryAsync();
            return existingId.Value;
        }
        else
        {
            await using var cmd = BuildInsertJobCommand(conn, job);
            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }
    }

    public async Task InsertCustomerOrderAsync(CustomerOrderEvent evt, int jobFk)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = BuildInsertCustomerOrderCommand(conn, evt, jobFk);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Job command builders ---

    private static NpgsqlCommand BuildInsertJobCommand(NpgsqlConnection conn, JobData job)
    {
        var cmd = new NpgsqlCommand(@"
            INSERT INTO ""Job"" (
                ""JobId"", ""AutomationId"", ""AutomationName"", ""AutomationChainingId"",
                ""AreaId"", ""FleetId"", ""TenantId"", ""JobType"", ""JobPriority"", ""Tags"",
                ""IsCompleted"", ""CreatedBy"", ""CreatedOn"", ""UpdatedBy"", ""UpdatedOn"")
            VALUES (
                @JobId, @AutomationId, @AutomationName, @AutomationChainingId,
                @AreaId, @FleetId, @TenantId, @JobType, @JobPriority, @Tags,
                @IsCompleted, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
            RETURNING ""Id""", conn);

        AddParam(cmd, "JobId",               NpgsqlDbType.Uuid,                    job.JobId);
        AddParam(cmd, "AutomationId",        NpgsqlDbType.Uuid,                    job.AutomationId);
        AddParam(cmd, "AutomationName",      NpgsqlDbType.Text,                    job.AutomationName);
        AddParam(cmd, "AutomationChainingId",NpgsqlDbType.Integer,                 job.AutomationChainingId);
        AddParam(cmd, "AreaId",              NpgsqlDbType.Text,                    job.AreaId);
        AddParam(cmd, "FleetId",             NpgsqlDbType.Uuid,                    job.FleetId);
        AddParam(cmd, "TenantId",            NpgsqlDbType.Uuid,                    job.TenantId);
        AddParam(cmd, "JobType",             NpgsqlDbType.Text,                    job.JobType);
        AddParam(cmd, "JobPriority",         NpgsqlDbType.Integer,                 (object)job.JobPriority);
        AddParam(cmd, "Tags",                NpgsqlDbType.Array | NpgsqlDbType.Text, job.Tags);
        AddParam(cmd, "IsCompleted",         NpgsqlDbType.Boolean,                 (object)job.IsCompleted);
        AddParam(cmd, "CreatedBy",           NpgsqlDbType.Text,                    job.CreatedBy);
        AddParam(cmd, "CreatedOn",           NpgsqlDbType.TimestampTz,             (object)job.CreatedOn);
        AddParam(cmd, "UpdatedBy",           NpgsqlDbType.Text,                    job.UpdatedBy);
        AddParam(cmd, "UpdatedOn",           NpgsqlDbType.TimestampTz,             job.UpdatedOn);
        return cmd;
    }

    private static NpgsqlCommand BuildUpdateJobCommand(NpgsqlConnection conn, JobData job, int pk)
    {
        var cmd = new NpgsqlCommand(@"
            UPDATE ""Job"" SET
                ""AutomationId""        = @AutomationId,
                ""AutomationName""      = @AutomationName,
                ""AutomationChainingId""= @AutomationChainingId,
                ""AreaId""              = @AreaId,
                ""FleetId""             = @FleetId,
                ""JobType""             = @JobType,
                ""JobPriority""         = @JobPriority,
                ""Tags""                = @Tags,
                ""IsCompleted""         = @IsCompleted,
                ""UpdatedBy""           = @UpdatedBy,
                ""UpdatedOn""           = @UpdatedOn
            WHERE ""Id"" = @Id", conn);

        AddParam(cmd, "Id",              NpgsqlDbType.Integer,                 (object)pk);
        AddParam(cmd, "AutomationId",    NpgsqlDbType.Uuid,                    job.AutomationId);
        AddParam(cmd, "AutomationName",  NpgsqlDbType.Text,                    job.AutomationName);
        AddParam(cmd, "AutomationChainingId", NpgsqlDbType.Integer,            job.AutomationChainingId);
        AddParam(cmd, "AreaId",          NpgsqlDbType.Text,                    job.AreaId);
        AddParam(cmd, "FleetId",         NpgsqlDbType.Uuid,                    job.FleetId);
        AddParam(cmd, "JobType",         NpgsqlDbType.Text,                    job.JobType);
        AddParam(cmd, "JobPriority",     NpgsqlDbType.Integer,                 (object)job.JobPriority);
        AddParam(cmd, "Tags",            NpgsqlDbType.Array | NpgsqlDbType.Text, job.Tags);
        AddParam(cmd, "IsCompleted",     NpgsqlDbType.Boolean,                 (object)job.IsCompleted);
        AddParam(cmd, "UpdatedBy",       NpgsqlDbType.Text,                    job.UpdatedBy);
        AddParam(cmd, "UpdatedOn",       NpgsqlDbType.TimestampTz,             job.UpdatedOn);
        return cmd;
    }

    // --- CustomerOrder command builder ---

    private static NpgsqlCommand BuildInsertCustomerOrderCommand(
        NpgsqlConnection conn, CustomerOrderEvent evt, int jobFk)
    {
        var cmd = new NpgsqlCommand(@"
            INSERT INTO ""CustomerOrder"" (
                ""JobId"", ""OrderId"", ""MissionId"", ""MissionName"", ""Status"",
                ""ProgressionRate"", ""OrderType"", ""Sequence"", ""IsTemplate"", ""IsCompleted"",
                ""DeviceId"", ""DeviceName"", ""DeviceRegistryName"", ""DeviceType"",
                ""AssignedBy"", ""AssignmentType"", ""AssignedOn"", ""QueuedAt"",
                ""ReassignmentCount"", ""ReassignedDeviceName"", ""ReassignedDeviceRegistryName"",
                ""StartTime"", ""EndTime"", ""EstimatedEndTime"", ""EstimatedRemainingDistance"",
                ""UpdatedByOrderTrackingOn"", ""CancellationReason"", ""CancellationDescription"",
                ""Errors"", ""OperatingModes"", ""BundleId"", ""RobotOrderId"",
                ""PreviousRobotOrderIds"", ""TenantId"", ""CreatedBy"", ""CreatedOn"",
                ""UpdatedBy"", ""UpdatedOn"")
            VALUES (
                @JobId, @OrderId, @MissionId, @MissionName, @Status,
                @ProgressionRate, @OrderType, @Sequence, @IsTemplate, @IsCompleted,
                @DeviceId, @DeviceName, @DeviceRegistryName, @DeviceType,
                @AssignedBy, @AssignmentType, @AssignedOn, @QueuedAt,
                @ReassignmentCount, @ReassignedDeviceName, @ReassignedDeviceRegistryName,
                @StartTime, @EndTime, @EstimatedEndTime, @EstimatedRemainingDistance,
                @UpdatedByOrderTrackingOn, @CancellationReason, @CancellationDescription,
                @Errors, @OperatingModes, @BundleId, @RobotOrderId,
                @PreviousRobotOrderIds, @TenantId, @CreatedBy, @CreatedOn,
                @UpdatedBy, @UpdatedOn)", conn);

        AddParam(cmd, "JobId",                       NpgsqlDbType.Integer,                   (object)jobFk);
        AddParam(cmd, "OrderId",                     NpgsqlDbType.Text,                      (object)evt.OrderId);
        AddParam(cmd, "MissionId",                   NpgsqlDbType.Uuid,                      evt.MissionId);
        AddParam(cmd, "MissionName",                 NpgsqlDbType.Text,                      evt.MissionName);
        AddParam(cmd, "Status",                      NpgsqlDbType.Text,                      evt.Status);
        AddParam(cmd, "ProgressionRate",             NpgsqlDbType.Integer,                   (object)evt.ProgressionRate);
        AddParam(cmd, "OrderType",                   NpgsqlDbType.Varchar,                   evt.OrderType);
        AddParam(cmd, "Sequence",                    NpgsqlDbType.Integer,                   (object)evt.Sequence);
        AddParam(cmd, "IsTemplate",                  NpgsqlDbType.Boolean,                   (object)evt.IsTemplate);
        AddParam(cmd, "IsCompleted",                 NpgsqlDbType.Boolean,                   (object)evt.IsCompleted);
        AddParam(cmd, "DeviceId",                    NpgsqlDbType.Uuid,                      evt.DeviceId);
        AddParam(cmd, "DeviceName",                  NpgsqlDbType.Text,                      evt.DeviceName);
        AddParam(cmd, "DeviceRegistryName",          NpgsqlDbType.Text,                      evt.DeviceRegistryName);
        AddParam(cmd, "DeviceType",                  NpgsqlDbType.Text,                      evt.DeviceType);
        AddParam(cmd, "AssignedBy",                  NpgsqlDbType.Text,                      evt.AssignedBy);
        AddParam(cmd, "AssignmentType",              NpgsqlDbType.Text,                      evt.AssignmentType);
        AddParam(cmd, "AssignedOn",                  NpgsqlDbType.TimestampTz,               evt.AssignedOn);
        AddParam(cmd, "QueuedAt",                    NpgsqlDbType.TimestampTz,               evt.QueuedAt);
        AddParam(cmd, "ReassignmentCount",           NpgsqlDbType.Integer,                   (object)evt.ReassignmentCount);
        AddParam(cmd, "ReassignedDeviceName",        NpgsqlDbType.Text,                      evt.ReassignedDeviceName);
        AddParam(cmd, "ReassignedDeviceRegistryName",NpgsqlDbType.Text,                      evt.ReassignedDeviceRegistryName);
        AddParam(cmd, "StartTime",                   NpgsqlDbType.TimestampTz,               evt.StartTime);
        AddParam(cmd, "EndTime",                     NpgsqlDbType.TimestampTz,               evt.EndTime);
        AddParam(cmd, "EstimatedEndTime",            NpgsqlDbType.TimestampTz,               evt.EstimatedEndTime);
        AddParam(cmd, "EstimatedRemainingDistance",  NpgsqlDbType.Double,                    evt.EstimatedRemainingDistance);
        AddParam(cmd, "UpdatedByOrderTrackingOn",    NpgsqlDbType.TimestampTz,               evt.UpdatedByOrderTrackingOn);
        AddParam(cmd, "CancellationReason",          NpgsqlDbType.Text,                      evt.CancellationReason);
        AddParam(cmd, "CancellationDescription",     NpgsqlDbType.Varchar,                   evt.CancellationDescription);
        AddParam(cmd, "Errors",                      NpgsqlDbType.Jsonb,                     evt.Errors);
        AddParam(cmd, "OperatingModes",              NpgsqlDbType.Jsonb,                     evt.OperatingModes);
        AddParam(cmd, "BundleId",                    NpgsqlDbType.Text,                      evt.BundleId);
        AddParam(cmd, "RobotOrderId",                NpgsqlDbType.Text,                      evt.RobotOrderId);
        AddParam(cmd, "PreviousRobotOrderIds",       NpgsqlDbType.Array | NpgsqlDbType.Text, evt.PreviousRobotOrderIds);
        AddParam(cmd, "TenantId",                    NpgsqlDbType.Uuid,                      (object)evt.TenantId);
        AddParam(cmd, "CreatedBy",                   NpgsqlDbType.Text,                      evt.CreatedBy);
        AddParam(cmd, "CreatedOn",                   NpgsqlDbType.TimestampTz,               (object)evt.CreatedOn);
        AddParam(cmd, "UpdatedBy",                   NpgsqlDbType.Text,                      evt.UpdatedBy);
        AddParam(cmd, "UpdatedOn",                   NpgsqlDbType.TimestampTz,               evt.UpdatedOn);
        return cmd;
    }

    private static void AddParam(NpgsqlCommand cmd, string name, NpgsqlDbType type, object? value)
    {
        var p = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };
        cmd.Parameters.Add(p);
    }
}
