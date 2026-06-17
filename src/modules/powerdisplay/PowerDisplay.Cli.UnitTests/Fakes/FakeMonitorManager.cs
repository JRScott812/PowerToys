// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Common.Models;
using PowerDisplay.Common.Services;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests.Fakes;

internal sealed class FakeMonitorManager : IMonitorManager
{
    private readonly List<Monitor> _monitors;

    public FakeMonitorManager(params Monitor[] monitors) => _monitors = new List<Monitor>(monitors);

    public bool FailWrites { get; set; }

    public string FailureMessage { get; set; } = "device did not respond";

    public List<(string Op, string Id, int Value)> Writes { get; } = new();

    public void SetMaxCompatibilityMode(bool enabled)
    {
        // No-op: command RunAsync paths under test never call this (Program wires max-compat
        // onto the real manager before dispatch), so there is nothing to record here.
    }

    public Task<IReadOnlyList<Monitor>> DiscoverMonitorsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Monitor>>(_monitors);

    public Task<MonitorOperationResult> SetBrightnessAsync(string id, int v, CancellationToken ct = default) => Record("brightness", id, v);

    public Task<MonitorOperationResult> SetContrastAsync(string id, int v, CancellationToken ct = default) => Record("contrast", id, v);

    public Task<MonitorOperationResult> SetVolumeAsync(string id, int v, CancellationToken ct = default) => Record("volume", id, v);

    public Task<MonitorOperationResult> SetColorTemperatureAsync(string id, int v, CancellationToken ct = default) => Record("color-temperature", id, v);

    public Task<MonitorOperationResult> SetInputSourceAsync(string id, int v, CancellationToken ct = default) => Record("input-source", id, v);

    public Task<MonitorOperationResult> SetPowerStateAsync(string id, int v, CancellationToken ct = default) => Record("power-state", id, v);

    public Task<MonitorOperationResult> SetRotationAsync(string id, int v, CancellationToken ct = default) => Record("orientation", id, v);

    private Task<MonitorOperationResult> Record(string op, string id, int value)
    {
        Writes.Add((op, id, value));
        return Task.FromResult(FailWrites
            ? MonitorOperationResult.Failure(FailureMessage)
            : MonitorOperationResult.Success());
    }
}
