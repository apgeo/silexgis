// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.TestData;

/// <summary>The password a run's generated accounts share: checked, then kept only as a hash.</summary>
public sealed record TestDataPasswordResult(string? Hash, IReadOnlyList<string> Problems);

/// <summary>
/// Turns the password an administrator gives a run into the hash every generated account is
/// written with, after holding it to the rules every account's password is held to — so a
/// generated account is never easier to get into than one somebody made.
/// </summary>
public sealed class TestDataAccountPassword(UserManager<SilexGisUser> users)
{
    public async Task<TestDataPasswordResult> HashAsync(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var probe = new SilexGisUser { UserName = "testdata@" + TestDataPeoplePlanner.EmailDomain };
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, probe, password);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }

        return problems.Count > 0
            ? new TestDataPasswordResult(null, problems)
            : new TestDataPasswordResult(users.PasswordHasher.HashPassword(probe, password), []);
    }
}
