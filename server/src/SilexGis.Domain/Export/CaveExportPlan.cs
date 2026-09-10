// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Export;

/// <summary>
/// What the server decided to put in the file for one cave. Reached only through
/// <see cref="CaveExportPlanner.Resolve"/>.
/// </summary>
public enum CaveExportPosition
{
    /// <summary>
    /// The cave's surveyed position. Reached only by a cave whose position this
    /// installation does not protect at all — never by anything the request said, because
    /// the request cannot name it, and never by who is asking, because a file outlives the
    /// account that took it.
    /// </summary>
    Exact = 0,

    /// <summary>The protection-grid position, flagged in the file as approximate.</summary>
    Grid = 1,

    /// <summary>The cave, with no coordinates at all.</summary>
    None = 2,

    /// <summary>Not in the file. Counted, so the file can say some caves are missing.</summary>
    Omitted = 3,
}

/// <summary>
/// What an exporter said about the caves whose position this installation protects: one
/// answer for a named cave, one answer for all of them, and the answer this caller has
/// already given before.
/// </summary>
/// <param name="PerCave">
/// A treatment for a single cave, by feature id. Wins over the other two.
/// </param>
/// <param name="AppliedToAll">
/// One treatment for every cave that needs one — so a large export is one decision rather
/// than thousands.
/// </param>
/// <param name="StoredAnswer">
/// The answer this caller settled on earlier, so somebody who has already decided is not
/// asked again. Consulted last, and only for caves the request says nothing about.
/// </param>
public sealed record CaveExportChoices(
    IReadOnlyDictionary<Guid, string>? PerCave = null,
    string? AppliedToAll = null,
    string? StoredAnswer = null);

/// <summary>Why an export was refused before a byte of it was written.</summary>
/// <param name="Code">The stable refusal code.</param>
/// <param name="CaveIds">
/// The caves the refusal is about, in a stable order. Only ever caves the caller may
/// already read — a refusal never names an id that was not in scope.
/// </param>
/// <param name="NamedTreatment">
/// The treatment code that was not understood, when that is what went wrong.
/// </param>
public sealed record CaveExportRefusal(
    string Code,
    IReadOnlyList<Guid> CaveIds,
    string? NamedTreatment = null);

/// <summary>
/// What the file will contain, decided for every cave in scope before any of it is
/// written.
/// </summary>
/// <param name="Positions">What position, if any, each cave in scope gets.</param>
/// <param name="Treatments">
/// The treatment applied to each cave that needed one — what the file states and what the
/// audit trail records. A cave that exports as it stands is absent from this map: nothing
/// was chosen for it.
/// </param>
/// <param name="Omitted">
/// The caves left out, in a stable order. The count is what the file has to declare: a
/// recipient who cannot tell that something was withheld has been misled by a file
/// containing only true statements.
/// </param>
public sealed record CaveExportPlan(
    IReadOnlyDictionary<Guid, CaveExportPosition> Positions,
    IReadOnlyDictionary<Guid, ProtectedPositionTreatment> Treatments,
    IReadOnlyList<Guid> Omitted);

/// <summary>Either a plan or a refusal, never both and never neither.</summary>
public sealed record CaveExportPlanResult(CaveExportPlan? Plan, CaveExportRefusal? Refusal)
{
    /// <summary>Whether the export must not run.</summary>
    public bool Refused => Refusal is not null;
}

/// <summary>
/// Turns what an exporter asked for into what the file will say about every cave in scope,
/// or into a refusal.
/// </summary>
/// <remarks>
/// <para>
/// This does not decide whether a position is protected — that rule is written once,
/// elsewhere, and is asked rather than restated. What is decided here is what this channel
/// does with the answer, and the channel is unusual in that its output is a file: it leaves
/// the installation, and afterwards no permission check stands between it and whoever ends
/// up holding it. That is why the vocabulary is closed and why a request that does not
/// cover a cave is refused rather than defaulted. A silent default would be a policy nobody
/// stated, applied to a coordinate somebody is protecting.
/// </para>
/// <para>
/// It is also why what needs a decision is the cave's protection and not the caller's
/// rights. Every other surface in this application asks "may this account see the exact
/// position", answers it per request, and is done; here the answer is written into bytes
/// that are copied, forwarded and kept long after the account that asked lost the right, so
/// "an administrator asked for it" is not a safeguard the file carries with it. A protected
/// cave therefore needs a treatment even from its own owner.
/// </para>
/// <para>
/// Two things are deliberately outside it. Which caves the caller may read at all is
/// settled before this is called, by the visibility filter, and is a different question
/// from protection. And whether a geometry can be snapped at all — a shape that is not a
/// plain point cannot be moved to a grid without disclosing its outline — is settled by the
/// writer against the geometry it holds; a plan says what was chosen, not what the geometry
/// turned out to allow.
/// </para>
/// </remarks>
public static class CaveExportPlanner
{
    /// <summary>A cave whose position is protected was given no treatment.</summary>
    public const string TreatmentMissingCode = "export.treatment_missing";

    /// <summary>
    /// A treatment was named that is not one of the three. Every spelling of "the exact
    /// position" arrives here, which is the whole reason the code exists.
    /// </summary>
    public const string TreatmentUnknownCode = "export.treatment_unknown";

    /// <summary>
    /// Resolves the plan.
    /// </summary>
    /// <param name="caveIds">
    /// The caves in scope — already narrowed to what this caller may read.
    /// </param>
    /// <param name="protectedCaveIds">
    /// Of those, the ones whose position this installation protects — exactly the set
    /// needing a decision. It is the cave's own protection state and deliberately not
    /// anything about the caller: an administrator, the cave's owner and the holder of an
    /// exact-location grant all have to choose, because the file they take away carries no
    /// permission check with it and cannot be un-taken once it has travelled. It is read
    /// from the model rather than from the request as well: a client cannot make a cave
    /// protected, and cannot make one unprotected either.
    /// </param>
    /// <param name="choices">What the exporter asked for.</param>
    public static CaveExportPlanResult Resolve(
        IReadOnlyCollection<Guid> caveIds,
        IReadOnlySet<Guid> protectedCaveIds,
        CaveExportChoices choices)
    {
        var scope = new HashSet<Guid>(caveIds);
        var positions = new Dictionary<Guid, CaveExportPosition>(scope.Count);
        var treatments = new Dictionary<Guid, ProtectedPositionTreatment>();
        var omitted = new List<Guid>();
        var missing = new List<Guid>();

        // A treatment named for a cave outside the scope is ignored rather than refused,
        // and the refusal never names one either. Answering "no such cave here" would tell
        // the asker which ids exist and are readable, which is a question the visibility
        // filter has already declined to answer.
        foreach (var id in caveIds.Distinct().OrderBy(id => id))
        {
            var named = choices.PerCave is not null && choices.PerCave.TryGetValue(id, out var perCave)
                ? perCave
                : null;

            if (named is not null && !ProtectedPositionTreatments.TryParse(named, out _))
            {
                return Unknown(named, [id]);
            }

            var needsDecision = protectedCaveIds.Contains(id);

            if (named is null && !needsDecision)
            {
                // Nothing about this cave's position is protected and nothing was said about
                // it, so there is nothing to choose: it exports as it stands.
                positions[id] = CaveExportPosition.Exact;
                continue;
            }

            string? code;
            if (named is not null)
            {
                // An answer for this cave by name wins, and it is honoured even for a cave
                // that needed no decision: every one of the three can only put less in the
                // file than the exact position would, never more.
                code = named;
            }
            else
            {
                code = choices.AppliedToAll ?? choices.StoredAnswer;
            }

            if (code is null)
            {
                missing.Add(id);
                continue;
            }

            if (!ProtectedPositionTreatments.TryParse(code, out var treatment))
            {
                // Reached by an applied-to-all or a stored answer that names something
                // outside the vocabulary. It is refused rather than skipped over: a stored
                // answer nobody can read is not a reason to guess at a coordinate.
                return Unknown(code, []);
            }

            treatments[id] = treatment;
            positions[id] = treatment switch
            {
                ProtectedPositionTreatment.GridPosition => CaveExportPosition.Grid,
                ProtectedPositionTreatment.NoPosition => CaveExportPosition.None,
                _ => CaveExportPosition.Omitted,
            };

            if (treatment == ProtectedPositionTreatment.Omit)
            {
                omitted.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            return new CaveExportPlanResult(
                null, new CaveExportRefusal(TreatmentMissingCode, missing));
        }

        return new CaveExportPlanResult(
            new CaveExportPlan(positions, treatments, omitted), null);
    }

    private static CaveExportPlanResult Unknown(string named, IReadOnlyList<Guid> caveIds) =>
        new(null, new CaveExportRefusal(TreatmentUnknownCode, caveIds, named));
}
