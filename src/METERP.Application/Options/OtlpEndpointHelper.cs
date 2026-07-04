namespace METERP.Application.Options;

/// <summary>
/// Builds OTLP HTTP/protobuf collector URLs for trace and metric export.
/// </summary>
public static class OtlpEndpointHelper
{
    public static Uri BuildHttpProtobufEndpoint(string endpoint, string signal)
    {
        var trimmed = endpoint.TrimEnd('/');
        if (trimmed.EndsWith($"/v1/{signal}", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed);

        return new Uri($"{trimmed}/v1/{signal}");
    }
}