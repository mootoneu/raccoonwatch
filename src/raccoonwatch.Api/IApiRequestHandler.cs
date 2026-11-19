namespace Raccoonwatch.Api;

/// <summary>Dispatches validated API requests to application services.</summary>
public interface IApiRequestHandler
{
    Task<ApiResponse> HandleAsync(ApiRequest request, CancellationToken cancellationToken = default);
}
