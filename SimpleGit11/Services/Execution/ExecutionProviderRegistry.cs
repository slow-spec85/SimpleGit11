using System;
using System.Collections.Generic;

namespace SimpleGit11.Services.Execution;

public sealed class ExecutionProviderRegistry : IExecutionProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IExecutionProvider> _providers;

    public ExecutionProviderRegistry(IEnumerable<IExecutionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Dictionary<string, IExecutionProvider> providerMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (IExecutionProvider provider in providers)
        {
            if (!providerMap.TryAdd(provider.Id, provider))
            {
                throw new InvalidOperationException($"Execution provider '{provider.Id}' is registered more than once.");
            }
        }

        _providers = providerMap;
    }

    public IExecutionProvider GetRequiredProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out IExecutionProvider? provider)
            ? provider
            : throw new InvalidOperationException($"Execution provider '{providerId}' is not registered.");
    }
}
