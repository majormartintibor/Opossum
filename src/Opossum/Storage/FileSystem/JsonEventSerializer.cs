using System.Text.Json;
using System.Text.Json.Serialization;
using Opossum.Core;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Handles JSON serialization and deserialization of SequencedEvent objects.
/// Supports polymorphic IEvent types through type information preservation.
/// </summary>
internal sealed class JsonEventSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false, // Minified for performance (40% smaller files, faster I/O)
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new PolymorphicEventConverter()
        }
    };

    /// <summary>
    /// Serializes a SequencedEvent to JSON string.
    /// </summary>
    /// <param name="sequencedEvent">The event to serialize</param>
    /// <returns>JSON string representation</returns>
    /// <exception cref="ArgumentNullException">When sequencedEvent is null</exception>
    public string Serialize(SequencedEvent sequencedEvent)
    {
        ArgumentNullException.ThrowIfNull(sequencedEvent);
        ArgumentNullException.ThrowIfNull(sequencedEvent.Event);

        return JsonSerializer.Serialize(sequencedEvent, SerializerOptions);
    }

    /// <summary>
    /// Deserializes a JSON string to SequencedEvent.
    /// </summary>
    /// <param name="json">The JSON string to deserialize</param>
    /// <returns>Deserialized SequencedEvent</returns>
    /// <exception cref="ArgumentNullException">When json is null or empty</exception>
    /// <exception cref="JsonException">When JSON is invalid or cannot be deserialized</exception>
    public SequencedEvent Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentNullException(nameof(json), "JSON string cannot be null or empty");
        }

        var sequencedEvent = JsonSerializer.Deserialize<SequencedEvent>(json, SerializerOptions);

        if (sequencedEvent == null)
        {
            throw new JsonException("Failed to deserialize SequencedEvent - result was null");
        }

        return sequencedEvent;
    }

    /// <summary>
    /// Custom JSON converter for polymorphic IEvent types.
    /// Preserves the actual type information during serialization.
    /// </summary>
    private class PolymorphicEventConverter : JsonConverter<IEvent>
    {
        public override IEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // For deserialization, we rely on the type information stored in the JSON
            // The actual IEvent implementation is stored as a nested object with type metadata
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // Get the type name from the JSON
            if (!root.TryGetProperty("$type", out var typeProperty))
            {
                throw new JsonException("Missing $type property for polymorphic event deserialization");
            }

            var typeName = typeProperty.GetString();
            if (string.IsNullOrEmpty(typeName))
            {
                throw new JsonException("$type property cannot be null or empty");
            }

            // Resolve the type
            var eventType = Type.GetType(typeName);
            if (eventType == null)
            {
                throw new JsonException($"Could not resolve type: {typeName}");
            }

            // Deserialize to the actual type
            var rawText = root.GetRawText();
            var result = JsonSerializer.Deserialize(rawText, eventType, options);

            return result as IEvent;
        }

        public override void Write(Utf8JsonWriter writer, IEvent value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Write the actual type information along with the object
            var actualType = value.GetType();

            writer.WriteStartObject();

            // Write type metadata
            writer.WriteString("$type", actualType.AssemblyQualifiedName);

            // Write all properties of the actual type
            var json = JsonSerializer.Serialize(value, actualType, options);
            using var doc = JsonDocument.Parse(json);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
