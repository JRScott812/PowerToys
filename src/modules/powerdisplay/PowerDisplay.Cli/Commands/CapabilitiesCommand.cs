// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Common.Services;

namespace PowerDisplay.Cli.Commands;

public static class CapabilitiesCommand
{
    public static async Task<int> RunAsync(
        IMonitorManager monitorManager,
        IReadOnlySet<string> hiddenMonitorIds,
        int? monitorNumber,
        string? monitorId,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        var monitors = await monitorManager.DiscoverMonitorsAsync(cancellationToken);
        monitors = MonitorFiltering.ExcludeHidden(monitors, hiddenMonitorIds);

        var (monitor, exit) = MonitorFiltering.ResolveSelected(monitors, monitorNumber, monitorId, "capabilities", output);
        if (monitor is null)
        {
            return exit;
        }

        var caps = monitor.VcpCapabilitiesInfo;
        var vcpCodes = new List<CliVcpCodeInfo>();

        if (caps is not null)
        {
            foreach (var code in caps.GetSortedVcpCodes())
            {
                List<string>? discreteValues = null;
                if (code.HasDiscreteValues)
                {
                    discreteValues = new List<string>(code.SupportedValues.Count);
                    foreach (var v in code.SupportedValues)
                    {
                        discreteValues.Add(SetCommand.FormatDiscrete(code.Code, v));
                    }
                }

                vcpCodes.Add(new CliVcpCodeInfo
                {
                    Code = code.FormattedCode,
                    Name = code.Name,
                    Continuous = code.IsContinuous,
                    DiscreteValues = discreteValues,
                });
            }
        }

        output.WriteCapabilitiesResult(new CliCapabilitiesResult
        {
            Monitor = SetCommand.ToRef(monitor),
            CommunicationMethod = monitor.CommunicationMethod,
            RawCapabilities = monitor.CapabilitiesRaw,
            Model = caps?.Model,
            MccsVersion = caps?.MccsVersion,
            VcpCodes = vcpCodes,
        });

        return CliExitCodes.Ok;
    }
}
