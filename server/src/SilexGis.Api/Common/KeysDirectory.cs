// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// The directory holding what must outlive the process: the data-protection key ring and the
/// token certificates. One answer for both, so they can never end up on different volumes.
/// </summary>
public static class KeysDirectory
{
    public static string Resolve(IConfiguration configuration) =>
        Path.GetFullPath(
            configuration.GetValue<string>("Keys:Path") ?? Path.Combine("data", "keys"),
            AppContext.BaseDirectory);
}
