// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace PowerDisplay.Cli.Settings;

internal sealed class CliPdMonitorDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; init; }
}
