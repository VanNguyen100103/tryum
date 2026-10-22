namespace Tryum.Application.Common.Interfaces;

public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default);
}
