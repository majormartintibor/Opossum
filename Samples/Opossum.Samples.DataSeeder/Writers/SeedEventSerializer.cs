using Opossum.Core;

namespace Opossum.Samples.DataSeeder.Writers;

/// <summary>
/// Serializes and deserializes <see cref="SequencedEvent"/> objects using exactly the same
/// JSON format as Opossum's internal <c>JsonEventSerializer</c>, ensuring the files produced
/// by <see cref="DirectEventWriter"/> are byte-for-byte compatible with those written by the
/// event store itself.
/// </summary>
internal sealed class SeedEventSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new PolymorphicEventConverter() }
    };

    public string Serialize(SequencedEvent sequencedEvent) =>
        JsonSerializer.Serialize(sequencedEvent, _options);

    public SequencedEvent Deserialize(string json) =>
        JsonSerializer.Deserialize<SequencedEvent>(json, _options)
            ?? throw new JsonException("Failed to deserialize SequencedEvent — result was null");

    /// <summary>
    /// Mirrors the <c>PolymorphicEventConverter</c> in <c>JsonEventSerializer</c>.
    /// Writes a <c>$type</c> property containing the assembly-qualified type name and then
    /// copies all concrete type properties into the same JSON object.
    /// </summary>
    private sealed class PolymorphicEventConverter : JsonConverter<IEvent>
    {
        public override IEvent? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("$type", out var typeProp))
                throw new JsonException("Missing $type property for polymorphic IEvent deserialization");

            var typeName = typeProp.GetString()
                ?? throw new JsonException("$type property cannot be null or empty");

            var type = Type.GetType(typeName)
                ?? throw new JsonException($"Could not resolve type: {typeName}");

            return (IEvent?)JsonSerializer.Deserialize(root.GetRawText(), type, options);
        }

        public override void Write(
            Utf8JsonWriter writer,
            IEvent value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            var actualType = value.GetType();
            writer.WriteStartObject();
            writer.WriteString("$type", actualType.AssemblyQualifiedName);

            // Serialize to MemoryStream using the concrete type (does not re-trigger this
            // converter), then copy each property into the outer writer.
            using var ms = new MemoryStream(256);
            JsonSerializer.Serialize(ms, value, actualType, options);
            var reader = new Utf8JsonReader(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var property in doc.RootElement.EnumerateObject())
                property.WriteTo(writer);

            writer.WriteEndObject();
        }
    }
}
