// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Globalization;
using PowerDisplay.Contracts;
using PowerDisplay.Models;

namespace PowerDisplay.Cli.Commands;

public static class ProfilesCommand
{
    /// <summary>
    /// Lists the saved PowerDisplay profiles (read from profiles.json, the same file the GUI writes).
    /// Takes the already-loaded collection so the disk read stays in <see cref="Program"/>, mirroring
    /// how settings are read once up front.
    /// </summary>
    public static int Run(PowerDisplayProfiles profiles, ICliOutput output)
    {
        var infos = new List<CliProfileInfo>(profiles.Profiles.Count);
        foreach (var profile in profiles.Profiles)
        {
            infos.Add(new CliProfileInfo
            {
                Name = profile.Name,
                MonitorCount = profile.MonitorSettings?.Count ?? 0,
                LastModified = profile.LastModified.ToString("o", CultureInfo.InvariantCulture),
            });
        }

        output.WriteProfileListResult(new CliProfileListResult { Profiles = infos });
        return CliExitCodes.Ok;
    }
}
