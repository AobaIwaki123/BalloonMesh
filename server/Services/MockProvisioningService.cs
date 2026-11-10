using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;

namespace BalloonMesh.Server.Services;

/// <summary>
/// Development-only mock of the future BLE Mesh onboarding flow.  The real
/// Provisioner (key distribution, model bind, group subscribe) is separate;
/// this service only exercises the UI and SignalR contract.
/// </summary>
public sealed class MockProvisioningService
{
    private readonly object _gate = new();
    private readonly List<MockProvisioningDevice> _devices;
    private readonly IMonitorEvents _notifier;
    private readonly ILogger<MockProvisioningService> _logger;

    public MockProvisioningService(IMonitorEvents notifier, ILogger<MockProvisioningService> logger)
    {
        _notifier = notifier;
        _logger = logger;
        _devices = CreateInitial();
    }

    public IReadOnlyList<MockProvisioningDevice> Snapshot()
    {
        lock (_gate)
        {
            return _devices.OrderBy(device => device.Id, StringComparer.Ordinal).ToArray();
        }
    }

    public bool Provision(string id)
    {
        bool shouldFail;
        lock (_gate)
        {
            var index = _devices.FindIndex(device =>
                string.Equals(device.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }
            var device = _devices[index];
            if (device.State is "provisioned" or "provisioning")
            {
                return false;
            }
            shouldFail = id == "mock-b-03";
            _devices[index] = device with
            {
                State = "provisioning",
                Progress = 5,
                Error = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        _ = SimulateAsync(id, shouldFail);
        _ = NotifyAsync();
        return true;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _devices.Clear();
            _devices.AddRange(CreateInitial());
        }
        _ = NotifyAsync();
    }

    private async Task SimulateAsync(string id, bool shouldFail)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Update(id, device => device with
        {
            State = "provisioning",
            Progress = 35,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await NotifyAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(1600));
        Update(id, device => device with
        {
            State = "provisioning",
            Progress = 70,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await NotifyAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(1800));
        if (shouldFail)
        {
            Update(id, device => device with
            {
                State = "failed",
                Progress = 70,
                Error = "Provisioning timed out (mock)",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            _logger.LogWarning("mock provisioning failed for {Id}", id);
        }
        else
        {
            ushort? address = null;
            lock (_gate)
            {
                address = NextAddressLocked();
            }
            Update(id, device => device with
            {
                State = "provisioned",
                Progress = 100,
                MeshAddress = address,
                Error = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            _logger.LogInformation("mock provisioning completed for {Id} as 0x{Address:X4}",
                id, address ?? 0);
        }
        await NotifyAsync();
    }

    private void Update(string id, Func<MockProvisioningDevice, MockProvisioningDevice> update)
    {
        lock (_gate)
        {
            var index = _devices.FindIndex(device =>
                string.Equals(device.Id, id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _devices[index] = update(_devices[index]);
            }
        }
    }

    private ushort? NextAddressLocked()
    {
        var used = _devices
            .Where(device => device.MeshAddress is not null)
            .Select(device => device.MeshAddress!.Value)
            .ToHashSet();
        for (var candidate = 0x0140; candidate <= 0x01FF; candidate++)
        {
            if (!used.Contains((ushort)candidate))
            {
                return (ushort)candidate;
            }
        }
        return null;
    }

    private Task NotifyAsync() => _notifier.OnboardingUpdatedAsync(Snapshot());

    private static List<MockProvisioningDevice> CreateInitial() =>
    [
        new("mock-b-01", "B-Mock-01", "balloon",
            "B1A11000000111223344001122334455", null, "unprovisioned", 0, null, DateTimeOffset.UtcNow),
        new("mock-b-02", "B-Mock-02", "balloon",
            "B1A11000000111223344001122334466", 0x0141, "provisioned", 100, null, DateTimeOffset.UtcNow),
        new("mock-b-03", "B-Mock-03", "balloon",
            "B1A11000000111223344001122334477", null, "failed", 70,
            "Provisioning timed out (mock)", DateTimeOffset.UtcNow),
        new("mock-b-04", "B-Mock-04", "balloon",
            "B1A11000000111223344001122334488", 0x0142, "provisioning", 60, null, DateTimeOffset.UtcNow),
        new("mock-b-05", "B-Mock-05", "balloon",
            "B1A11000000111223344001122334499", null, "unprovisioned", 0, null, DateTimeOffset.UtcNow),
        new("mock-gw-01", "GW-Mock-01", "gateway",
            "B1A110000001112233440011223344AA", null, "unprovisioned", 0, null, DateTimeOffset.UtcNow)
    ];
}
