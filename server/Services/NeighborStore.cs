using System.Collections.Concurrent;
using BalloonMesh.Server.Protocol;

namespace BalloonMesh.Server.Services;

public sealed record NeighborObservation(ushort NeighborAddress, int RssiDbm, DateTimeOffset ReceivedAt);

/// <summary>
/// Stores the neighbour observations attached to (balloon, batch).  Multiple
/// gateways can deliver the same report; equal observations are collapsed and
/// a stronger RSSI wins when the same neighbour is reported more than once.
/// </summary>
public sealed class NeighborStore
{
    private readonly ConcurrentDictionary<(ushort Src, byte Batch), ConcurrentDictionary<ushort, NeighborObservation>> _batches = new();

    public void Add(ushort source, byte batchId, IEnumerable<NeighborEntry> entries, DateTimeOffset receivedAt)
    {
        var batch = _batches.GetOrAdd((source, batchId), static _ => new ConcurrentDictionary<ushort, NeighborObservation>());
        foreach (var entry in entries)
        {
            batch.AddOrUpdate(
                entry.MeshAddress,
                _ => new NeighborObservation(entry.MeshAddress, entry.RssiDbm, receivedAt),
                (_, existing) => entry.RssiDbm > existing.RssiDbm
                    ? new NeighborObservation(entry.MeshAddress, entry.RssiDbm, receivedAt)
                    : existing);
        }
    }

    public IReadOnlyList<NeighborObservation> Get(ushort source, byte batchId) =>
        _batches.TryGetValue((source, batchId), out var batch)
            ? batch.Values.OrderByDescending(item => item.RssiDbm).ToArray()
            : [];

    public void Remove(ushort source, byte batchId) => _batches.TryRemove((source, batchId), out _);
}
