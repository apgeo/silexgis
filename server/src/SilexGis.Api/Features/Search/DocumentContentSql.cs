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
/// <param name="FileId">
/// The file somebody put here — the one a reader downloads. Where an optional converter has made
/// a portable copy so the document can be shown as pictures of pages, the words were read out of
/// that copy and the number below counts its pages, but the copy is a way of drawing the document
/// and never the document itself, so it is not what this names.
/// </param>
/// <param name="MimeType">
/// The format of the upload, which is what the document is. Not necessarily the format the words
/// were read out of.
/// </param>
/// <param name="PageNumber">
/// Which division of the artifact this installation draws matched — the upload itself, or the
/// portable copy made of it where one exists. Read it together with <paramref name="Division"/>:
/// where the drawn artifact numbers nothing, the whole text is stored as division one and the
/// number means nothing a reader would recognise.
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
    /// Which artifact of a revision wins when both of them matched: the portable copy an
    /// optional converter made, ahead of the upload it was made from.
    /// <para>
    /// The reason is that a hit's page number and the picture the viewer draws for that number
    /// have to come from the same artifact, because nothing keeps two paginations in step. A
    /// workbook whose first sheet prints across ten pages puts sheet three nowhere near page
    /// three, so a hit read out of the upload and a page drawn from the copy disagree by an
    /// amount nobody can compute — and the reader is given no sign that they were sent to the
    /// wrong place. Preferring the artifact that gets drawn removes the disagreement rather than
    /// trying to correct for it, and as a side effect gives a word-processing document real page
    /// numbers wherever a converter is deployed, which it has never had.
    /// </para>
    /// <para>
    /// It is a preference and deliberately not an exclusion. A rendering is not a superset of
    /// what it was rendered from: a print-to-portable-document drops a deck's speaker notes,
    /// leaves a hidden worksheet out entirely, and clips a wide cell at the column boundary. Had
    /// the upload's rows been excluded whenever a copy carried any, deploying the converter
    /// would have quietly made that content unfindable — the same archive answering a smaller
    /// set of questions, with nothing to show for it. So both artifacts are matched, whichever
    /// of them matched decides the hit, and the copy only wins where it matched too.
    /// </para>
    /// <para>
    /// The number a hit then carries always came out of the artifact that matched, and
    /// <see cref="DocumentPagination.DivisionOf"/> names it after that same artifact — so a hit
    /// carried by the upload of a convertible format reports a sheet, a slide, or nothing that
    /// numbers at all, never a page. No format a converter touches paginates itself, which is
    /// what makes that safe: a page number reported by this query is always a page of the
    /// artifact whose pictures the reader is shown.
    /// </para>
    /// <para>
    /// An installation running no converter holds no copies at all, so every document is matched
    /// exactly as it was before any of this existed. What a converter changes is how precisely a
    /// match can be pointed at inside a document — never whether the document is found.
    /// </para>
    /// </summary>
    private const string DrawnArtifactFirst = "(f.converted_from_file_id IS NULL)";

    /// <summary>
    /// Whether reach through an attached object could conceivably admit this document — its
    /// current file hangs on something, or this caller uploaded the revision serving it.
    /// <para>
    /// It is the same test the batch reach walk applies to its candidates, written here so the
    /// database applies it before an id leaves the server. Everything it drops is a document the
    /// walk would have loaded, examined and discarded, so dropping it here costs nothing and
    /// changes no answer.
    /// </para>
    /// <para>
    /// "The file a document serves" is the first file of its current revision, ordered the way
    /// every other read of that fact orders it, so this cannot come to disagree with the walk
    /// about which file decides.
    /// </para>
    /// </summary>
    private const string ReachCouldAdmit = """
        EXISTS (SELECT 1
                FROM document_versions cv
                WHERE cv.document_id = d.id
                  AND cv.is_current
                  AND (cv.uploaded_by = @reach_user::uuid
                       OR EXISTS (SELECT 1
                                  FROM attachments a
                                  WHERE a.file_id = (SELECT cf.id
                                                     FROM files cf
                                                     WHERE cf.document_version_id = cv.id
                                                     ORDER BY cf.created_at, cf.id
                                                     LIMIT 1))))
        """;

    /// <summary>
    /// The upload behind whichever artifact matched: itself, or the file its portable copy was
    /// made from. A hit names the document's own file however the words were reached.
    /// </summary>
    private const string UploadBehindMatch =
        "JOIN files u ON u.id = COALESCE(f.converted_from_file_id, f.id)";

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
    /// resolved — <see cref="WithheldMatchIdsAsync"/> names the ones worth resolving. It is a
    /// term of this statement rather than a filter over its results, so the total, the ranking
    /// and the page boundaries are computed over exactly the rows the caller is shown.
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
        var plan = await PlanAsync(connection, ctx, query, includeSuperseded, reachedByAttachment, ct);
        var (readSql, versionArm, indexable, exact, buckets, parameters) = plan;

        parameters.Add("row_limit", limit);
        parameters.Add("row_offset", offset);
        parameters.Add("headline_options", HeadlineOptions);

        // DISTINCT ON collapses a document to one row, so one long report cannot fill a page of
        // results with itself, and the total beside the rows counts documents — which is what a
        // number next to a list of documents is read as. Which row survives is decided by the
        // artifact preference first and relevance second: where a portable copy of the upload
        // matched, its page is the one reported, because its pages are the ones drawn.
        var sql = $"""
            WITH hits AS MATERIALIZED (
                SELECT DISTINCT ON (d.id)
                       d.id             AS document_id,
                       d.title          AS title,
                       d.language       AS language,
                       u.id             AS file_id,
                       u.mime_type      AS mime_type,
                       f.mime_type      AS drawn_mime_type,
                       v.id             AS version_id,
                       v.version_number AS version_number,
                       v.is_current     AS is_current,
                       p.page_number    AS page_number,
                       p.text           AS page_text,
                       {RankExpression(buckets)} AS hit_rank
                FROM document_pages p
                JOIN files f ON f.id = p.file_id
                {UploadBehindMatch}
                JOIN document_versions v ON v.id = f.document_version_id
                JOIN documents d ON d.id = v.document_id
                WHERE ({indexable})
                  AND ({exact})
                  AND {versionArm}
                  AND {readSql}
                ORDER BY d.id, {DrawnArtifactFirst}, hit_rank DESC, p.page_number
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
                   drawn_mime_type      AS "DrawnMimeType",
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

                // What the number counts is a property of the artifact the number came out of,
                // not of the upload: where a converter has laid an office document out, the words
                // were read off pages that exist, and calling them anything else would throw away
                // the only precise answer the installation has.
                DocumentPagination.DivisionOf(r.DrawnMimeType),
                r.Rank,
                r.Snippet ?? string.Empty,
                r.TotalDocuments))
            .ToList();
    }

    /// <summary>
    /// The documents whose text matches <paramref name="query"/> and which this caller's entries,
    /// ownership and read audience do <em>not</em> admit — the only documents where reach through
    /// an attached object can change the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reach is the last band of the document read rule and the only one that never denies, so
    /// asking about a document any earlier band already admitted would buy nothing, and asking
    /// about a document nothing matched would buy nothing either. Narrowing the question to the
    /// documents that match and are withheld is what keeps a walk over every world a file hangs
    /// in from being paid over the whole archive on every keystroke.
    /// </para>
    /// <para>
    /// Its answer feeds <see cref="SearchAsync"/> as a term of that statement. Nothing here is
    /// shown to anybody: these ids are candidates for a question, and the walk that follows
    /// decides which of them survive.
    /// </para>
    /// <para>
    /// The candidates are narrowed twice over, and both narrowings matter because what follows
    /// this is a walk in memory. First to what matched and is withheld; then to the documents
    /// where reach could possibly say yes at all — the ones whose current file hangs on
    /// something, plus the ones whose revision this caller uploaded. That second test is the
    /// same one the walk applies, spelled here so the database applies it instead of shipping an
    /// id per withheld match across the wire for the walk to discard. It is a narrowing and not
    /// a bound: an archive that really does hang everything off caves still pays per matching
    /// document rather than per row shown, which is a cost nothing here removes.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<Guid>> WithheldMatchIdsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        string query,
        bool includeSuperseded,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var connection = db.Database.GetDbConnection();
        var plan = await PlanAsync(connection, ctx, query, includeSuperseded, reachedByAttachment: null, ct);

        // The revision arm is deliberately absent. Reach is resolved against the file a document
        // currently serves, whichever revision matched, so a superseded page is still a reason to
        // ask the question — and what an old revision then shows is decided by the walk in the
        // statement that follows, not by this list of candidates.
        //
        // Neither artifact of a revision is excluded, because the statement this feeds excludes
        // neither: a document found by a word only its portable copy carries has to appear here
        // too, or the two statements would disagree about which documents exist and the total
        // would count rows the list could not show.
        plan.Parameters.Add("reach_user", ctx.UserId);
        var sql = $"""
            SELECT DISTINCT d.id
            FROM document_pages p
            JOIN files f ON f.id = p.file_id
            JOIN document_versions v ON v.id = f.document_version_id
            JOIN documents d ON d.id = v.document_id
            WHERE ({plan.Indexable})
              AND ({plan.Exact})
              AND NOT {plan.ReadSql}
              AND {ReachCouldAdmit}
            """;

        var rows = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, plan.Parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>
    /// Everything the matching statements share: the caller's walk over the documents table, the
    /// revision arm, and the two language predicates. Built once so the query that finds the rows
    /// and the query that asks which withheld rows are worth a second look cannot come to match
    /// text differently — a difference there would show as a document found by one and not the
    /// other with no rule between them.
    /// </summary>
    private static async Task<MatchPlan> PlanAsync(
        DbConnection connection,
        AccessContext ctx,
        string query,
        bool includeSuperseded,
        IReadOnlyCollection<Guid>? reachedByAttachment,
        CancellationToken ct)
    {
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

        return new MatchPlan(readSql, versionArm, indexable, exact, buckets, parameters);
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

/// <summary>
/// The parts of a content query that both matching statements share: the caller's read walk over
/// the documents table, the revision arm, the two language predicates, the buckets they were built
/// from, and the parameter set they all reference.
/// </summary>
internal sealed record MatchPlan(
    string ReadSql,
    string VersionArm,
    string Indexable,
    string Exact,
    IReadOnlyList<LanguageBucket> Buckets,
    DynamicParameters Parameters);

/// <summary>A configured language and the text-search configuration that indexes it.</summary>
internal sealed record LanguageBucket(string? Code, string Configuration);

/// <summary>
/// The row shape the content query returns, before the format's own page rules apply.
/// <c>MimeType</c> is the upload's format and <c>DrawnMimeType</c> the format of the artifact the
/// page rows were read out of — the same value unless a converter made a portable copy.
/// </summary>
internal sealed record HitRow(
    Guid DocumentId,
    string Title,
    Guid FileId,
    string MimeType,
    string DrawnMimeType,
    Guid VersionId,
    int VersionNumber,
    bool IsCurrentVersion,
    int PageNumber,
    double Rank,
    int TotalDocuments,
    string? Snippet);
