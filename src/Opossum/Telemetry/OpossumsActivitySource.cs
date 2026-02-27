namespace Opossum.Telemetry;

/// <summary>
/// Provides the <see cref="System.Diagnostics.ActivitySource"/> name used by Opossum for distributed tracing.
/// </summary>
/// <remarks>
/// To receive Opossum traces in your OpenTelemetry pipeline, register the source name with your tracer provider:
/// <code>
/// tracerProviderBuilder.AddSource(OpossumTelemetry.ActivitySourceName);
/// </code>
/// No additional packages are required — Opossum emits traces via <see cref="System.Diagnostics.ActivitySource"/>
/// and is compatible with any <see cref="System.Diagnostics.ActivityListener"/>-based consumer.
/// When no listener is attached the overhead is a single null-check per operation.
/// </remarks>
public static class OpossumTelemetry
{
    /// <summary>The name of the Opossum <see cref="System.Diagnostics.ActivitySource"/>.</summary>
    public const string ActivitySourceName = "Opossum";
}

/// <summary>
/// Internal <see cref="ActivitySource"/> and operation-name constants shared across all Opossum components.
/// </summary>
internal static class OpossumsActivity
{
    internal static readonly ActivitySource Source = new(OpossumTelemetry.ActivitySourceName, "0.2.0-preview.2");

    // Activity operation names follow the "Component.Operation" convention.
    internal const string Append = "EventStore.Append";
    internal const string Read = "EventStore.Read";
    internal const string ReadLast = "EventStore.ReadLast";
    internal const string ProjectionRebuild = "Projection.Rebuild";
}
