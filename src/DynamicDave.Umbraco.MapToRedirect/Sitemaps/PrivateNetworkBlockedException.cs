namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

/// <summary>Thrown when a request target resolves to a blocked (private, loopback or link-local) address.</summary>
internal sealed class PrivateNetworkBlockedException(string host)
    : Exception($"'{host}' resolves to a blocked (private, loopback or link-local) address.");
