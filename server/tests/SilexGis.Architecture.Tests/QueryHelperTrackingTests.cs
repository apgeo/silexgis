// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Shouldly;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// A method that hands back a query for somebody else to build on must not state whether the
/// result is tracked.
/// </summary>
/// <remarks>
/// <para>
/// EF takes the tracking option for the whole composed query, wherever in it the option is stated.
/// A helper that returns <c>IQueryable</c> and says "no tracking" inside itself therefore decides
/// something for every query that uses it, including queries the helper's author never saw: one
/// that loads an entity through the helper and then changes it stores nothing, and still answers
/// 200. The option belongs to the code that materialises the query and knows what it will do with
/// the rows.
/// </para>
/// <para>
/// The helpers that already state it were each read at their call sites and are safe today. They
/// are kept as a list, each to be burned down by moving the option to its callers; the list can
/// only shrink. A new helper cannot join it by accident, and an entry that no longer matches a
/// helper must go, so the list never describes code that is not there.
/// </para>
/// </remarks>
public class QueryHelperTrackingTests
{
    private const string ReadAtItsCallSites =
        "stated inside a helper that returns a query; every call site was read and none changes what "
        + "it loads, so it is safe today. To retire the entry, move the option to the callers.";

    private static readonly HashSet<(string File, string Method)> Known =
    [
        ("server/src/SilexGis.Api/Features/Cabinets/CabinetEndpoints.cs", "FiledOn"),
        ("server/src/SilexGis.Api/Features/History/HistoryEndpoints.cs", "TimelineAsync"),
        ("server/src/SilexGis.Api/Features/ResLinks/ResLinkTargetResolvers.cs", "Readable"),
        ("server/src/SilexGis.Api/Features/Sync/SyncDocumentEndpoints.cs", "ReadableLinksAsync"),
        ("server/src/SilexGis.Api/Features/Sync/SyncEndpoints.cs", "ReadableBy"),
        ("server/src/SilexGis.Api/Features/Sync/SyncPeopleEndpoints.cs", "Listed"),
        ("server/src/SilexGis.Api/Features/Sync/SyncPeopleEndpoints.cs", "SentBy"),
        ("server/src/SilexGis.Api/Features/Sync/SyncRasterEndpoints.cs", "Carried"),
        ("server/src/SilexGis.Api/Features/Sync/SyncRasterEndpoints.cs", "PinsGone"),
        ("server/src/SilexGis.Api/Features/Sync/SyncReviewCurrent.cs", "HandedAsync"),
        ("server/src/SilexGis.Api/Features/Sync/SyncScope.cs", "Gone"),
        ("server/src/SilexGis.Api/Features/Sync/SyncScope.cs", "Readable"),
        ("server/src/SilexGis.Api/Features/Sync/SyncTripEndpoints.cs", "Carried"),
        ("server/src/SilexGis.Api/Features/Sync/SyncTripEndpoints.cs", "PointsGone"),
        ("server/src/SilexGis.Api/Features/TripTracking/TripTrackingEndpoints.cs", "LogOf"),
        ("server/src/SilexGis.Infrastructure/Documents/PhotographReads.cs", "AttachedToExpedition"),
        ("server/src/SilexGis.Infrastructure/Documents/PhotographReads.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Filters/CabinetFilterWorld.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Filters/DocumentFilterWorld.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Filters/FeatureFilterWorld.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Filters/SavedFilterReader.cs", "VisibleRows"),
        ("server/src/SilexGis.Infrastructure/Filters/SurveyModelFilterWorld.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Filters/TripLogFilterWorld.cs", "VisibleAsync"),
        ("server/src/SilexGis.Infrastructure/Geodata/GeoreferencedMapReads.cs", "ShownAsync"),
        ("server/src/SilexGis.Infrastructure/Notifications/EventAudience.cs", "EventIdsTheAccountIsOn"),
        ("server/src/SilexGis.Infrastructure/Notifications/ExpeditionAudience.cs", "ExpeditionIdsTheAccountIsOn"),
        ("server/src/SilexGis.Infrastructure/Notifications/TripAudience.cs", "TripIdsTheAccountIsOn"),
        ("server/src/SilexGis.Infrastructure/Permissions/AccessContextResolver.cs", "MembershipRowsOf"),
        ("server/src/SilexGis.Infrastructure/Permissions/FullAdministrators.cs", "LiveMemberIds"),
        ("server/src/SilexGis.Infrastructure/Persistence/TripRoleLinks.cs", "FeatureIdsNamedBy"),
        ("server/src/SilexGis.Infrastructure/Persistence/TripRoleLinks.cs", "FeatureIdsNamedIn"),
        ("server/src/SilexGis.Infrastructure/Persistence/TripRoleLinks.cs", "PairsIn"),
        ("server/src/SilexGis.Infrastructure/Persistence/TripRoleLinks.cs", "TripIdsNaming"),
        ("server/src/SilexGis.Infrastructure/Persistence/TripRoleLinks.cs", "TripIdsNamingAny"),
    ];

    [Fact]
    public void No_new_helper_that_returns_a_query_states_a_tracking_option()
    {
        var offenders = Scan()
            .Where(o => !Known.Contains(o))
            .Select(o => $"{o.File}: {o.Method}")
            .ToList();

        offenders.ShouldBeEmpty(
            "A method that returns a query for others to compose must not call AsNoTracking, "
            + "AsTracking or AsNoTrackingWithIdentityResolution: EF applies the option to the whole "
            + "composed query, so a caller that loads and changes an entity through it would save "
            + "nothing. State the option where the query is materialised. " + ReadAtItsCallSites);
    }

    [Fact]
    public void Every_entry_in_the_known_list_still_names_a_helper_that_states_one()
    {
        var found = Scan().ToHashSet();
        var stale = Known.Where(k => !found.Contains(k)).Select(k => $"{k.File}: {k.Method}").ToList();

        stale.ShouldBeEmpty(
            "These helpers no longer state a tracking option, or no longer exist; remove them from "
            + "the list so it only describes code that is there.");
    }

    [Fact]
    public void The_scan_sees_a_helper_that_states_the_option_and_leaves_alone_one_that_does_not()
    {
        const string source = """
            class Reads
            {
                public static IQueryable<Guid> Bad(Db db) => db.Rows.AsNoTracking().Select(r => r.Id);

                public static IQueryable<Row> BadBlock(Db db)
                {
                    return db.Rows
                        .AsTracking()
                        .Where(r => r.Live);
                }

                public static async Task<IQueryable<Row>> BadAsync(Db db, Ct ct)
                {
                    return db.Rows.AsNoTrackingWithIdentityResolution();
                }

                public static IQueryable<Row> Fine(Db db) => db.Rows.Where(r => r.Live);

                // IQueryable<Row> Commented(Db db) => db.Rows.AsNoTracking();
                public static List<Row> Materialises(Db db) => db.Rows.AsNoTracking().ToList();

                public static IQueryable<Row> Message(Db db) { Log("AsNoTracking()"); return db.Rows; }
            }
            """;

        QueryHelperScan.Offenders(source).ShouldBe(["Bad", "BadBlock", "BadAsync"], ignoreOrder: true);
    }

    private static IEnumerable<(string File, string Method)> Scan()
    {
        foreach (var (file, source) in TripSoftDeleteGuardTests.Sources())
        {
            foreach (var method in QueryHelperScan.Offenders(source))
            {
                yield return (file, method);
            }
        }
    }
}

/// <summary>Finds methods that return a query and state a tracking option inside themselves.</summary>
internal static partial class QueryHelperScan
{
    [GeneratedRegex(@"\bIQueryable<[^;{}()]*?>\s+(\w+)\s*(?:<[^>()]*>)?\s*\(")]
    private static partial Regex Signature();

    [GeneratedRegex(@"\bAs(No)?Tracking\w*\s*\(")]
    private static partial Regex TrackingOption();

    [GeneratedRegex(@"=>|\{")]
    private static partial Regex BodyStart();

    public static IEnumerable<string> Offenders(string source)
    {
        var code = CSharpText.Split(source).Code;
        foreach (Match signature in Signature().Matches(code))
        {
            var body = BodyOf(code, signature.Index + signature.Length);
            if (body is not null && TrackingOption().IsMatch(body))
            {
                yield return signature.Groups[1].Value;
            }
        }
    }

    /// <summary>The text of a method's body, from just after the "(" that opens its parameter list.</summary>
    private static string? BodyOf(string code, int afterOpeningParenthesis)
    {
        var depth = 1;
        var i = afterOpeningParenthesis;
        while (depth > 0 && i < code.Length)
        {
            depth += code[i] == '(' ? 1 : code[i] == ')' ? -1 : 0;
            i++;
        }

        // Generic constraints may sit between the parameters and the body.
        var start = BodyStart().Match(code, i);
        if (!start.Success)
        {
            return null;
        }

        var end = start.Index;
        if (start.Value == "=>")
        {
            var nesting = 0;
            for (; end < code.Length; end++)
            {
                var c = code[end];
                if (c is '(' or '{' or '[')
                {
                    nesting++;
                }
                else if (c is ')' or '}' or ']')
                {
                    nesting--;
                }
                else if (c == ';' && nesting == 0)
                {
                    break;
                }
            }
        }
        else
        {
            var nesting = 0;
            for (; end < code.Length; end++)
            {
                nesting += code[end] == '{' ? 1 : code[end] == '}' ? -1 : 0;
                if (nesting == 0)
                {
                    break;
                }
            }
        }

        return code[start.Index..Math.Min(end + 1, code.Length)];
    }
}
