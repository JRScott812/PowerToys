// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ManagedCommon;

namespace PowerDisplay.Cli.Settings;

/// <summary>
/// Reads the PowerDisplay <c>settings.json</c> the GUI writes, returning only the two
/// fields the CLI honours (max-compatibility, hidden monitor ids). AOT-safe: parses via
/// source-gen and pulls in no Settings.UI.Library dependency. Any failure (missing file,
/// malformed JSON) degrades to <see cref="CliRuntimeSettings.Default"/>.
/// </summary>
public static class CliSettingsReader
{
    private const string SettingsModuleFolder = "PowerDisplay";
    private const string SettingsFileName = "settings.json";

    public static CliRuntimeSettings Read(string? settingsPath = null)
    {
        var path = settingsPath ?? DefaultSettingsPath();

        try
        {
            if (!File.Exists(path))
            {
                return CliRuntimeSettings.Default;
            }

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, CliSettingsJsonContext.Default.CliPdSettingsDto);
            var props = dto?.Properties;
            if (props is null)
            {
                return CliRuntimeSettings.Default;
            }

            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (props.Monitors is not null)
            {
                foreach (var m in props.Monitors)
                {
                    if (m.IsHidden && !string.IsNullOrEmpty(m.Id))
                    {
                        hidden.Add(m.Id);
                    }
                }
            }

            return new CliRuntimeSettings(props.MaxCompatibilityMode, hidden);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.LogWarning($"PowerDisplay CLI: could not read settings ({path}): {ex.Message}");
            return CliRuntimeSettings.Default;
        }
    }

    // Mirrors the GUI's settings location: %LOCALAPPDATA%\Microsoft\PowerToys\PowerDisplay\settings.json
    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Microsoft", "PowerToys", SettingsModuleFolder, SettingsFileName);
    }
}
