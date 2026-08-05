// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Search;

/// <summary>
/// One hit: the best-matching page of one document, with the snippet cut from that page.
/// </summary>
/// <param name="PageNumber">
/// Which stored division of the file matched. Read it together with <paramref name="Division"/>:
/// for most formats the whole text is stored as division one and the number means nothing a
/// reader would recognise.
/// </param>
/// <param name="Snippet">
/// The matching stretch of the page, with matched words wrapped in markers. The text keeps the
/// diacritics its author wrote even when the search that found it did not.
/// </param>
/// <param name="TotalDocuments">
/// How many documents match in total, counted by the same statement that returned these rows and
/// therefore under exactly the same rules. Counting separately is how a total ends up announcing
/// rows the list itself declined to show.
/// </param>
public sealed record DocumentContentHit(
    Guid DocumentId,
    string Title,
    Guid FileId,
    string MimeType,
    Guid VersionId,
    int VersionNumber,
    bool IsCurrentVersion,
    int PageNumber,
    PageDivision Division,
    double Rank,
    string Snippet,
    int TotalDocuments);

/// <summary>
/// The content-search query (raw SQL lives only in *Sql.cs files), and deliberately the only
/// place that reads the page index — replacing the matching engine underneath is a change to
/// this one statement and nothing else.
/// <para>
/// Everything deciding which rows exist is inside the statement: the caller's read walk over the
/// documents table, and the rule that a superseded revision is readable only by someone who
/// could replace it. Deciding either afterwards would leave the count, the ranking and the page
/// boundaries computed over rows that caller may not have, which discloses them as surely as
/// printing them would.
/// </para>
/// <para>
/// Positions are not this query's business and it emits none: no coordinate, no geometry, and no
/// mention of anything a document is attached to. A document is found by what it says, by a
/// caller allowed to read it, and whether it happens to hang off a protected cave changes
/// neither of those. What must not be disclosed is the pairing, and nothing here emits one.
/// </para>
/// </summary>
public static class DocumentContentSql
{
    /// <summary>
    /// Snippet markers, deliberately not HTML: the value travels as data and whatever renders it
    /// decides what a marker looks like, so nothing downstream is tempted to trust the text.
    /// </summary>
    private const string HeadlineOptions =
        "StartSel=[[,StopSel=]],MaxFragments=2,FragmentDelimiter= … ,MaxWords=22,MinWords=8";

    /// <summary>
    /// The configuration for text in a language nothing has mapped, and for text whose language
    /// nobody recorded. It folds accents like the others but stems nothing, so a Hungarian report
    /// is found by the words it actually contains rather than not found at all.
    /// </summary>
    private const string FallbackConfiguration = "simple_unaccent";

    /// <summary>
    /// Documents whose text matches <paramref name="query"/>, most relevant first, restricted to
    /// what this caller may read.
    /// </summary>
    /// <param name="includeSuperseded">
    /// Whether to search revisions that have been replaced. Off by default, and never a way past
    /// the rule: even when on, an old revision matches only for a caller who may also write the
    /// document. A version is replaced precisely when something in it had to go, so finding the
    /// removed paragraph by searching for it would make a new upload a retraction that retracts
    /// nothing.
    /// </param>
    /// <param name="reachedByAttachment">
    /// Documents this caller reaches only through an object their files are attached to, already
    /// resolved. Resolving it is a walk over every world those files hang in, so a caller that
    /// has not paid for it passes nothing and gets the narrower result — the same trade the
    /// cabinet listing makes, and stated rather than hidden: a document reachable only because it
    /// hangs off a cave the caller may read is not found by its text, though fetching it by id
    /// still works.
    /// </param>
    public static async Task<IReadOnlyList<DocumentContentHit>> SearchAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        string query,
        int limit,
        int offset,
        bool includeSuperseded,
        IReadOnlyCollection<Guid>? reachedByAttachment,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var buckets = await LanguageBucketsAsync(connection, ct);

        string readSql;
        string versionArm;
        DynamicParameters parameters;
        if (includeSuperseded)
        {
            // The superseded arm is a disjunction inside the same WHERE rather than a filter
            // applied later, so a caller who may write the document simply has more rows to
            // match and everyone else has fewer — including in the count computed beside them.
            var (read, write, shared) = AccessSql.ReadAndWriteFragments(
                ctx, AccessDomain.Documents, "d", reachedByAttachment);
            readSql = read;
            versionArm = $"(v.is_current OR {write})";
            parameters = shared;
        }
        else
        {
            var (read, own) = AccessSql.VisibleToFragment(
                ctx, AccessDomain.Documents, "d", reachedByAttachment);
            readSql = read;
            versionArm = "v.is_current";
            parameters = own;
        }

        parameters.Add("q", query);
        parameters.Add("row_limit", limit);
        parameters.Add("row_offset", offset);
        parameters.Add("headline_options", HeadlineOptions);
        for (var i = 0; i < buckets.Count; i++)
        {
            parameters.Add($"cfg_{i}", buckets[i].Configuration);
            if (buckets[i].Code is { } code)
            {
                parameters.Add($"lang_{i}", code);
            }
        }

        // Two predicates over the same buckets, and both are needed.
        //
        // The first is a plain OR of tests against the page table alone, each with a query that
        // is constant for the whole statement — which is what lets the planner reach the rows
        // through the vector index instead of reading every page in the archive. It is
        // deliberately wider than the truth: it would also accept a Romanian page that happens to
        // match the English reading of the words.
        //
        // The second narrows it back to the exact answer by pairing each parsed query with the
        // languages it was parsed for. That pairing spans two tables, so no index can evaluate
        // it; asked on its own it would cost a full scan, and asked after the rows came back it
        // would not be part of the count at all.
        var indexable = string.Join(
            "\n                       OR ",
            Enumerable.Range(0, buckets.Count).Select(i => $"p.search_vector @@ {TsQuery(i)}"));
        var exact = string.Join(
            "\n                       OR ",
            buckets.Select((b, i) => b.Code is null
                ? $"({UnmappedLanguage(buckets)} AND p.search_vector @@ {TsQuery(i)})"
                : $"(d.language = @lang_{i} AND p.search_vector @@ {TsQuery(i)})"));

        // DISTINCT ON collapses a document to its best page, so one long report cannot fill a
        // page of results with itself, and the total beside the rows counts documents — which is
        // what a number next to a list of documents is read as.
        var sql = $"""
            WITH hits AS MATERIALIZED (
                SELECT DISTINCT ON (d.id)
                       d.id             AS document_id,
                       d.title          AS title,
                       d.language       AS language,
                       f.id             AS file_id,
                       f.mime_type      AS mime_type,
                       v.id             AS version_id,
                       v.version_number AS version_number,
                       v.is_current     AS is_current,
                       p.page_number    AS page_number,
                       p.text           AS page_text,
                       {RankExpression(buckets)} AS hit_rank
                FROM document_pages p
                JOIN files f ON f.id = p.file_id
                JOIN document_versions v ON v.id = f.document_version_id
                JOIN documents d ON d.id = v.document_id
                -- A copy something converted so a page of it could be drawn is not searched.
                -- Its words are the same words as the upload's, read a second time, and
                -- matching them would make whether a document is findable depend on whether an
                -- optional service happens to be deployed here — which would let two
                -- installations running the same version disagree about what exists.
                WHERE f.converted_from_file_id IS NULL
                  AND ({indexable})
                  AND ({exact})
                  AND {versionArm}
                  AND {readSql}
                ORDER BY d.id, hit_rank DESC, p.page_number
            ),
            paged AS (
                SELECT h.*, count(*) OVER () AS total_documents
                FROM hits h
                ORDER BY h.hit_rank DESC, h.title, h.document_id
                LIMIT @row_limit OFFSET @row_offset
            )
            SELECT document_id          AS "DocumentId",
                   title                AS "Title",
                   file_id              AS "FileId",
                   mime_type            AS "MimeType",
                   version_id           AS "VersionId",
                   version_number       AS "VersionNumber",
                   is_current           AS "IsCurrentVersion",
                   page_number          AS "PageNumber",
                   hit_rank::float8     AS "Rank",
                   total_documents::int AS "TotalDocuments",
                   ts_headline(document_search_config(language),
                               document_indexed_text(page_text),
                               websearch_to_tsquery(document_search_config(language), @q),
                               @headline_options) AS "Snippet"
            FROM paged
            ORDER BY hit_rank DESC, title, document_id
            """;

        var rows = await connection.QueryAsync<HitRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return rows
            .Select(r => new DocumentContentHit(
                r.DocumentId,
                r.Title,
                r.FileId,
                r.MimeType,
                r.VersionId,
                r.VersionNumber,
                r.IsCurrentVersion,
                r.PageNumber,
                DocumentPagination.DivisionOf(r.MimeType),
                r.Rank,
                r.Snippet ?? string.Empty,
                r.TotalDocuments))
            .ToList();
    }

    /// <summary>
    /// The search words parsed for one bucket's configuration. Written out rather than passed as
    /// a parameter because a tsquery has to be built by the server; both of its arguments are
    /// constant for the statement, so it is parsed once and can drive an index scan, which a
    /// query built from the document's own language column could not.
    /// <para>
    /// The web-search parser rather than the plain one: it is the only one that cannot raise a
    /// syntax error, which matters when the input is whatever somebody typed into a box.
    /// </para>
    /// </summary>
    private static string TsQuery(int bucket) => $"websearch_to_tsquery(@cfg_{bucket}::regconfig, @q)";

    /// <summary>
    /// A document whose language no bucket claims — unrecorded, or recorded as something this
    /// installation has no mapping for. Written as a chain of inequalities rather than an array
    /// test because the buckets are few and this keeps the fallback arm readable in a plan.
    /// </summary>
    private static string UnmappedLanguage(IReadOnlyList<LanguageBucket> buckets)
    {
        var mapped = buckets
            .Select((b, i) => (b.Code, Index: i))
            .Where(x => x.Code is not null)
            .Select(x => $"d.language <> @lang_{x.Index}")
            .ToList();

        return mapped.Count == 0
            ? "true"
            : $"(d.language IS NULL OR ({string.Join(" AND ", mapped)}))";
    }

    /// <summary>
    /// Relevance under whichever configuration indexed the row, so scores compare like with like.
    /// <c>ts_rank_cd</c> rather than <c>ts_rank</c>: it rewards words occurring near each other,
    /// which in a survey report is the difference between a page about a cave and a page that
    /// mentions it twice, forty paragraphs apart.
    /// </summary>
    private static string RankExpression(IReadOnlyList<LanguageBucket> buckets)
    {
        var fallback = buckets.Select((b, i) => (b.Code, Index: i)).First(x => x.Code is null).Index;
        var arms = buckets
            .Select((b, i) => (b.Code, Index: i))
            .Where(x => x.Code is not null)
            .Select(x => $"WHEN d.language = @lang_{x.Index} THEN ts_rank_cd(p.search_vector, {TsQuery(x.Index)})")
            .ToList();

        if (arms.Count == 0)
        {
            return $"ts_rank_cd(p.search_vector, {TsQuery(fallback)})";
        }

        return $"CASE {string.Join(" ", arms)} "
            + $"ELSE ts_rank_cd(p.search_vector, {TsQuery(fallback)}) END";
    }

    /// <summary>
    /// The buckets to build queries for: every mapping this installation has that names a
    /// configuration the server actually carries, plus the fallback. Asked of the catalogue for
    /// the same reason the resolver function asks it — a row naming a configuration nobody
    /// installed must degrade to language-neutral matching rather than fail the search.
    /// </summary>
    private static async Task<IReadOnlyList<LanguageBucket>> LanguageBucketsAsync(
        DbConnection connection, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<LanguageBucket>(
            new CommandDefinition(
                """
                SELECT l.code AS "Code", l.configuration AS "Configuration"
                FROM text_search_languages l
                JOIN pg_ts_config c ON c.cfgname = l.configuration
                                   AND pg_ts_config_is_visible(c.oid)
                ORDER BY l.code
                """,
                cancellationToken: ct));

        return [.. rows, new LanguageBucket(null, FallbackConfiguration)];
    }

}

/// <summary>A configured language and the text-search configuration that indexes it.</summary>
internal sealed record LanguageBucket(string? Code, string Configuration);

/// <summary>The row shape the content query returns, before the format's own page rules apply.</summary>
internal sealed record HitRow(
    Guid DocumentId,
    string Title,
    Guid FileId,
    string MimeType,
    Guid VersionId,
    int VersionNumber,
    bool IsCurrentVersion,
    int PageNumber,
    double Rank,
    int TotalDocuments,
    string? Snippet);
