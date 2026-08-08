// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// The rule set a fresh installation starts with, so a first import is useful before anybody
/// has configured anything.
///
/// <para>
/// Terms are written with their proper diacritics and are not repeated without them:
/// comparison folds both sides, so <c>peșteră</c> already matches <c>pestera</c>, <c>peşteră</c>
/// and <c>PESTERA</c>. Repeating the manglings would suggest the list has to enumerate them,
/// which is exactly the maintenance burden the folding exists to remove.
/// </para>
/// <para>
/// Order is the seed's main content. Every rule below is reachable, and the ones that would
/// otherwise be swallowed come first: <c>Peștera de la Izbuc</c> is a cave, not a spring, and
/// it is a cave because the cave rule is above the spring rule — not because anything here
/// weighs the two matches against each other.
/// </para>
/// </summary>
public static class TermRuleSeeds
{
    /// <summary>Name of the shipped set. Matching on it is how re-seeding finds the row it owns.</summary>
    public const string DefaultSetName = "Default detection rules (RO/EN)";

    public static IReadOnlyList<TermRule> Default { get; } =
    [
        Rule("cave-ro", "Cave — Romanian", ImportTargetKind.Cave,
            caveType: "cave", strip: TermStripMode.Leading,
            ro: ["peșteră", "peștera", "peșterile", "grotă", "grota"],
            en: []),

        Rule("cave-abbrev", "Cave — abbreviated prefix", ImportTargetKind.Cave,
            caveType: "cave", strip: TermStripMode.Leading, mode: TermMatchMode.Prefix,
            // Prefix rather than whole word on purpose: a bare "p." anywhere in a label is
            // far more often an abbreviation of something else than it is a cave.
            any: ["p.", "pst.", "pes.", "peșt."]),

        Rule("cave-en", "Cave — English", ImportTargetKind.Cave,
            caveType: "cave", strip: TermStripMode.Anywhere,
            ro: [],
            en: ["cave", "cavern"]),

        Rule("pit-ro", "Pit / aven — Romanian", ImportTargetKind.Cave,
            caveType: "pit", strip: TermStripMode.Leading,
            ro: ["aven", "avenul", "abis", "abisul"],
            en: [],
            any: ["av."]),

        Rule("pit-en", "Pit / shaft — English", ImportTargetKind.Cave,
            caveType: "pit", strip: TermStripMode.Anywhere,
            ro: [],
            en: ["shaft", "pothole", "pit"]),

        Rule("shelter", "Rock shelter", ImportTargetKind.Cave,
            caveType: "rock_shelter", strip: TermStripMode.Leading,
            ro: ["adăpost", "adăpostul", "abri"],
            en: ["rock shelter", "shelter"]),

        Rule("mine", "Mine / adit", ImportTargetKind.Cave,
            caveType: "mine", strip: TermStripMode.Leading,
            ro: ["mină", "mina", "galerie", "galeria"],
            en: ["mine", "adit"]),

        Rule("entrance", "Cave entrance", ImportTargetKind.CaveEntrance,
            entranceType: "natural", strip: TermStripMode.Leading,
            ro: ["intrare", "intrarea"],
            en: ["entrance"],
            any: ["int."]),

        Rule("sinkhole", "Sinkhole / doline", ImportTargetKind.SurfaceFeature,
            featureType: "sinkhole", strip: TermStripMode.Leading,
            ro: ["dolină", "dolina", "doline"],
            en: ["sinkhole", "doline", "swallow hole"]),

        Rule("ponor", "Ponor / sink", ImportTargetKind.SurfaceFeature,
            featureType: "water_flow", strip: TermStripMode.Leading,
            ro: ["ponor", "ponorul", "sorb"],
            en: ["sink", "swallet"]),

        Rule("spring", "Spring / resurgence", ImportTargetKind.SurfaceFeature,
            featureType: "water_flow", strip: TermStripMode.Leading,
            ro: ["izbuc", "izbucul", "izvor", "izvorul", "resurgență", "resurgenta"],
            en: ["spring", "resurgence", "rising"]),

        Rule("lake", "Lake / pond", ImportTargetKind.SurfaceFeature,
            featureType: "lake", strip: TermStripMode.Leading,
            ro: ["lac", "lacul", "tău", "tau"],
            en: ["lake", "pond"]),

        Rule("fracture", "Fault / fracture", ImportTargetKind.SurfaceFeature,
            featureType: "fracture_line", strip: TermStripMode.Leading,
            ro: ["falie", "fractură", "fractura"],
            en: ["fault", "fracture"]),

        Rule("peak", "Peak", ImportTargetKind.SurfaceFeature,
            featureType: "peak", strip: TermStripMode.Leading,
            ro: ["vârf", "vârful"],
            en: ["peak", "summit"]),

        Rule("bivouac", "Bivouac / camp", ImportTargetKind.SurfaceFeature,
            featureType: "bivouac", strip: TermStripMode.Leading,
            ro: ["bivuac", "tabără", "tabara"],
            en: ["bivouac", "camp"]),

        Rule("digging", "Digging site", ImportTargetKind.SurfaceFeature,
            featureType: "desobstruction", strip: TermStripMode.Leading,
            ro: ["desobstrucție", "desobstructie", "săpătură", "sapatura"],
            en: ["dig", "digging site"]),

        Rule("lead", "Continuation / lead", ImportTargetKind.SurfaceFeature,
            featureType: "continuation", strip: TermStripMode.Leading,
            ro: ["continuare", "continuarea"],
            en: ["continuation", "lead", "going"]),
    ];

    /// <summary>The shipped set as the document an export writes and an import reads.</summary>
    public static TermRuleDocument DefaultDocument { get; } = new()
    {
        Name = DefaultSetName,
        Description = "Romanian and English naming habits: what a waypoint called Peștera, P., aven, "
            + "izbuc, ponor or doline is proposed to become.",
        Rules = Default,
    };

    private static TermRule Rule(
        string id,
        string name,
        ImportTargetKind target,
        string? caveType = null,
        string? entranceType = null,
        string? featureType = null,
        TermStripMode strip = TermStripMode.None,
        TermMatchMode mode = TermMatchMode.WholeWord,
        string[]? ro = null,
        string[]? en = null,
        string[]? any = null)
    {
        var terms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (ro is { Length: > 0 })
        {
            terms["ro"] = ro;
        }

        if (en is { Length: > 0 })
        {
            terms["en"] = en;
        }

        if (any is { Length: > 0 })
        {
            terms[TermRule.AnyLanguage] = any;
        }

        return new TermRule
        {
            Id = id,
            Name = name,
            MatchMode = mode,
            MatchName = true,
            Terms = terms,
            Target = target,
            CaveTypeCode = caveType,
            EntranceTypeCode = entranceType,
            FeatureTypeCode = featureType,
            Strip = strip,
        };
    }
}
