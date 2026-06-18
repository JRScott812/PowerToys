// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;

namespace PowerDisplay.Cli.Properties;

/// <summary>
/// Strongly-typed accessor for the CLI's localizable human-readable strings (Resources.resx,
/// localized into satellite assemblies by the build pipeline).
/// Only prose lives here — help text, error messages/hints, and text-mode labels. The machine
/// contract (JSON keys, error <c>code</c> strings, <c>status</c> strings, exit codes, VCP names)
/// stays as invariant literals elsewhere and is never routed through this class.
/// </summary>
internal static class Resources
{
    private static readonly ResourceManager Manager =
        new("PowerDisplay.Cli.Properties.Resources", typeof(Resources).Assembly);

    // Resolves against the current UI culture; falls back to the resource key if a string is missing.
    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    internal static string Text_NoMonitorsDiscovered => Get(nameof(Text_NoMonitorsDiscovered));
}
