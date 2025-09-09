using System;                      // for StringComparer, Guid
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;      // <-- needed
using BlazorWP.Data;

namespace BlazorWP.Tests;

public sealed class InMemoryLocalStore : ILocalStore
{
    // storeName -> list of (Key, Value)
    private readonly Dictionary<string, List<(object Key, object? Value)>> _stores =
        new(StringComparer.Ordinal);

    public Task InitializeAsync() => Task.CompletedTask;

    // Return nullable to match interface and reality of "not found"
    public Task<T?> GetByKeyAsync<T>(string storeName, object key)
    {
        if (_stores.TryGetValue(storeName, out var list))
        {
            var found = list.FirstOrDefault(p => Equals(p.Key, key)).Value;
            return Task.FromResult(found is T t ? t : default(T?));
        }

        return Task.FromResult<T?>(default);
    }

    public Task<IReadOnlyList<T>> GetAllAsync<T>(string storeName)
    {
        var list = _stores.TryGetValue(storeName, out var pairs)
            ? pairs.Select(p => p.Value).OfType<T>().ToList()
            : new List<T>();

        return Task.FromResult<IReadOnlyList<T>>(list);
    }

    public Task AddAsync<T>(string storeName, T item)
    {
        if (!_stores.TryGetValue(storeName, out var list))
        {
            list = new List<(object Key, object? Value)>();
            _stores[storeName] = list;
        }

        // Auto-assign a key since AddAsync has no explicit key
        list.Add((Guid.NewGuid(), item));
        return Task.CompletedTask;
    }

    public Task PutAsync<T>(string storeName, T item)
    {
        // For this test double, treat Put as an Add (idempotency not required here)
        return AddAsync(storeName, item);
    }

    public Task DeleteAsync(string storeName, object key)
    {
        if (_stores.TryGetValue(storeName, out var list))
        {
            var idx = list.FindIndex(p => Equals(p.Key, key));
            if (idx >= 0) list.RemoveAt(idx);
        }
        return Task.CompletedTask;
    }
}
