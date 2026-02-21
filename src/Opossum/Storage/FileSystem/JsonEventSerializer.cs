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

            // Handle namespace migrations for backward compatibility
            if (eventType == null && typeName != null)
            {
                eventType = TryResolveRenamedType(typeName);
            }

            if (eventType == null)
            {
                throw new JsonException($"Could not resolve type: {typeName}");
            }

            // Deserialize to the actual type
            var rawText = root.GetRawText();
            var result = JsonSerializer.Deserialize(rawText, eventType, options);

            return result as IEvent;
        }

        /// <summary>
        /// Attempts to resolve renamed or relocated types for backward compatibility.
        /// Handles namespace migrations where events have been moved to different namespaces.
        /// </summary>
        /// <param name="oldTypeName">The original assembly-qualified type name</param>
        /// <returns>The resolved Type, or null if cannot be resolved</returns>
        private static Type? TryResolveRenamedType(string oldTypeName)
        {
            // Extract just the type name and assembly name
            var parts = oldTypeName.Split(',');
            if (parts.Length < 2) return null;

            var fullTypeName = parts[0].Trim();
            var assemblyName = parts[1].Trim();

            // Get the simple type name (last part after the last dot)
            var lastDotIndex = fullTypeName.LastIndexOf('.');
            if (lastDotIndex == -1) return null;

            var simpleTypeName = fullTypeName.Substring(lastDotIndex + 1);

            // Try to find the type by scanning all loaded assemblies for types with matching simple name
            // This allows events to be moved between namespaces without breaking deserialization
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in loadedAssemblies)
            {
                // Only check assemblies that match the original assembly name
                if (assembly.FullName == null || !assembly.FullName.StartsWith(assemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.Name == simpleTypeName && typeof(IEvent).IsAssignableFrom(type))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, IEvent value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Write the actual type information along with the object
            var actualType = value.GetType();

            writer.WriteStartObject();

            // Write type metadata
            writer.WriteString("$type", actualType.AssemblyQualifiedName);

            // Serialize to MemoryStream to avoid the intermediate string allocation,
            // then parse as bytes and copy each property into the outer writer.
            using var ms = new MemoryStream(256);
            JsonSerializer.Serialize(ms, value, actualType, options);
            var reader = new Utf8JsonReader(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            using var doc = JsonDocument.ParseValue(ref reader);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
