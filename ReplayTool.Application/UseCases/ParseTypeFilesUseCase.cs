using System.Text.Json;
using System.Text.Json.Serialization;
using ReplayTool.Application.Interfaces;

namespace ReplayTool.Application.UseCases;

public class ParseTypeFilesUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    // Parses DateTime strings as UTC regardless of the Kind stored in JSON.
    private static readonly JsonSerializerOptions _parseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new UtcDateTimeConverter(),
        },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ParseTypeFilesUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    // Returns null when the case is not found.
    public async Task<CaseParseResult?> ExecuteAsync(Guid caseId)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        var (_, folder) = result.Value;

        var customerOrders = await ParseFileAsync<Domain.Events.CustomerOrderEvent>(
            folder, CaseFileType.CustomerOrder, normalizeTimestamps: false);

        var routingResponses = await ParseFileAsync<Domain.Events.OrdersRoutingEventV2>(
            folder, CaseFileType.RoutingResponses, normalizeTimestamps: true);

        var assignmentSolutions = await ParseFileAsync<Domain.Events.AssignmentSolutionV2Event>(
            folder, CaseFileType.AssignmentSolution, normalizeTimestamps: true);

        return new CaseParseResult(customerOrders, routingResponses, assignmentSolutions);
    }

    private async Task<FileParseResult<T>?> ParseFileAsync<T>(
        string folder, string type, bool normalizeTimestamps)
    {
        var path = Path.Combine(folder, CaseFileType.Filename(type));
        if (!await _storage.FileExistsAsync(path))
            return null;

        var content = await _storage.ReadFileAsync(path);
        return ParseContent<T>(content, normalizeTimestamps);
    }

    internal static FileParseResult<T> ParseContent<T>(string content, bool normalizeTimestamps)
    {
        content = content.Trim();

        var events = new List<T>();
        var errors = new List<ParseError>();

        if (content.StartsWith('['))
        {
            // JSON array — parse each element individually so one bad item doesn't fail the rest.
            JsonElement[] elements;
            try
            {
                elements = JsonSerializer.Deserialize<JsonElement[]>(content, _parseOptions)
                    ?? [];
            }
            catch (JsonException ex)
            {
                errors.Add(new ParseError(0, $"Array JSON is malformed: {ex.Message}"));
                return new FileParseResult<T> { Errors = errors };
            }

            for (var i = 0; i < elements.Length; i++)
            {
                try
                {
                    var evt = elements[i].Deserialize<T>(_parseOptions);
                    if (evt is null)
                    {
                        errors.Add(new ParseError(i, "Deserialized to null."));
                        continue;
                    }
                    if (normalizeTimestamps) NormalizeUtc(evt);
                    events.Add(evt);
                }
                catch (JsonException ex)
                {
                    errors.Add(new ParseError(i, ex.Message));
                }
            }
        }
        else
        {
            // Single JSON object.
            try
            {
                var evt = JsonSerializer.Deserialize<T>(content, _parseOptions);
                if (evt is null)
                {
                    errors.Add(new ParseError(0, "Deserialized to null."));
                }
                else
                {
                    if (normalizeTimestamps) NormalizeUtc(evt);
                    events.Add(evt);
                }
            }
            catch (JsonException ex)
            {
                errors.Add(new ParseError(0, ex.Message));
            }
        }

        return new FileParseResult<T> { Events = events, Errors = errors };
    }

    // Walk all DateTime properties on the event and force DateTimeKind.Utc.
    private static void NormalizeUtc<T>(T evt)
    {
        if (evt is null) return;
        foreach (var prop in typeof(T).GetProperties())
        {
            if (prop.PropertyType == typeof(DateTime) && prop.CanWrite)
            {
                var dt = (DateTime)prop.GetValue(evt)!;
                if (dt.Kind != DateTimeKind.Utc)
                    prop.SetValue(evt, DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            }
            else if (prop.PropertyType == typeof(DateTime?) && prop.CanWrite)
            {
                var dt = (DateTime?)prop.GetValue(evt);
                if (dt.HasValue && dt.Value.Kind != DateTimeKind.Utc)
                    prop.SetValue(evt, DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc));
            }
        }
    }

    // Converter that always produces DateTimeKind.Utc from any ISO-8601 string.
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dt = reader.GetDateTime();
            return dt.Kind == DateTimeKind.Utc
                ? dt
                : DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("O"));
    }
}
