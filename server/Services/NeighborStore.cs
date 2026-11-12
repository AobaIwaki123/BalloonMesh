using System.Collections.Concurrent;
using BalloonMesh.Protocol;

namespace BalloonMesh.Server.Services;

public sealed record NeighborObservation(ushort NeighborAddress, int RssiDbm, DateTimeOffset ReceivedAt);

/// <summary>
/// Stores the neighbour observations attached to (balloon, batch).  Multiple
/// gateways can deliver the same report; equal observations are collapsed and
/// a stronger RSSI wins when the same neighbour is reported more than once.
/// Batches whose last report is older than <see cref="StaleAfter"/> are
/// swept so that reports for missing/already-closed FTM batches cannot leak.
/// </summary>
public sealed class NeighborStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(ushort Src, byte Batch), ConcurrentDictionary<ushort, NeighborObservation>> _batches = new();
    private readonly ConcurrentDictionary<(ushort Src, byte Batch), DateTimeOffset> _lastSeen = new();

    /// <summary>Returns true when an entry was added or a stronger RSSI won.</summary>
    public bool Add(ushort source, byte batchId, IEnumerable<NeighborEntry> entries,
        DateTimeOffset receivedAt)
    {
        var key = (source, batchId);
        _lastSeen[key] = receivedAt;
        Sweep(now: receivedAt);
        var batch = _batches.GetOrAdd(key, static _ =>
            new ConcurrentDictionary<ushort, NeighborObservation>());
        var changed = false;
        foreach (var entry in entries)
        {
            if (!batch.TryGetValue(entry.MeshAddress, out var existing))
            {
                batch[entry.MeshAddress] = new NeighborObservation(
                    entry.MeshAddress, entry.RssiDbm, receivedAt);
                changed = true;
            }
            else if (entry.RssiDbm > existing.RssiDbm)
            {
                batch[entry.MeshAddress] = new NeighborObservation(
                    entry.MeshAddress, entry.RssiDbm, receivedAt);
                changed = true;
            }
        }
        return changed;
    }

    public IReadOnlyList<NeighborObservation> Get(ushort source, byte batchId) =>
        _batches.TryGetValue((source, batchId), out var batch)
            ? batch.Values.OrderByDescending(item => item.RssiDbm).ToArray()
            : [];

    public void Remove(ushort source, byte batchId)
    {
        var key = (source, batchId);
        _batches.TryRemove(key, out _);
        _lastSeen.TryRemove(key, out _);
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in _lastSeen)
        {
            if (now - pair.Value <= StaleAfter)
            {
                continue;
            }
            if (_lastSeen.TryRemove(pair.Key, out _))
            {
                _batches.TryRemove(pair.Key, out _);
            }
        }
    }
}
