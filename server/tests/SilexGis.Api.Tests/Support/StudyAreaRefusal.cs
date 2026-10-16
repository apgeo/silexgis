// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Common;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The assertion that a refused study area was refused by the one shared gate: its status, its
/// code and its sentence, which no route writes for itself.
/// </summary>
public static class StudyAreaRefusal
{
    public static async Task ShouldBeTheSharedRefusalAsync(HttpResponseMessage response, string because)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{because}: {payload}");
        var root = JsonDocument.Parse(payload).RootElement;
        root.GetProperty("code").GetString().ShouldBe("feature.not_found", because);
        root.GetProperty("detail").GetString().ShouldBe(StudyAreaGate.NotFoundDetail, because);
    }
}
