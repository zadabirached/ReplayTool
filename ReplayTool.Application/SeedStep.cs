namespace ReplayTool.Application;

public enum SeedStepResult { Inserted, Skipped, Failed }

public record SeedStep(string Phase, string OrderId, SeedStepResult Result, string? Error = null);
