// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Trips;

/// <summary>The three schemas a shipped trip purpose starts with.</summary>
public sealed record TripStarterSections(string? FieldData, string? Logistics, string? Safety)
{
    public string? Of(TripSection section) => section switch
    {
        TripSection.FieldData => FieldData,
        TripSection.Logistics => Logistics,
        TripSection.Safety => Safety,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}

/// <summary>
/// What a shipped trip purpose asks a report to record, before anyone has edited it. These are
/// starting points, not a specification: a club edits them, and a purpose whose sections have
/// been edited is never written back over. Deliberately modest — a schema an administrator
/// trims is friendlier than one they must first delete to get anything done.
/// <para>
/// Two conventions the rest of the system reads:
/// </para>
/// <para>
/// A person who is not a caver on the trip — a permit holder, a key holder, a landowner — is a
/// <c>*_caver_id</c> field holding a roster identity, with a <c>*_note</c> beside it for
/// somebody the roster does not know. A phone number typed into free text is personal data
/// about a non-user that no disclosure rule inspects; a roster reference inherits the
/// disclosure the person already has. The pattern on the identity field is what makes the
/// difference real rather than a naming convention — a bare name is refused.
/// </para>
/// <para>
/// A cost is an amount, a currency and a note about what it covered, never a bare number: this
/// is not an accounting system, and a single money figure invites summing across currencies.
/// The three are flat sibling keys rather than a nested object because the form that renders
/// these schemas draws a flat object, and a nested one would store correctly and show nothing.
/// </para>
/// </summary>
public static class TripTypeStarterSchemas
{
    // The roster identity fields carry the identifier's own shape, written out per field rather
    // than factored into a definition and referenced: the schema text is what an administrator
    // edits by hand, and a $ref pointing elsewhere in the document is one more thing to
    // understand before editing.
    private const string GeneralFieldData =
        """
        {"type":"object","properties":{
          "conditions":{"type":"string","title":"Conditions underground"},
          "water_level":{"type":"string","title":"Water level",
            "enum":["low","normal","high","flood"]},
          "objective_reached":{"type":"boolean","title":"Objective reached"},
          "leads_found":{"type":"string","title":"Leads found"}
        }}
        """;

    private const string SurveyFieldData =
        """
        {"type":"object","properties":{
          "survey_grade":{"type":"string","title":"Survey grade",
            "enum":["1","2","3","4","5","6","X"]},
          "instrument":{"type":"string","title":"Instrument"},
          "loops_closed":{"type":"integer","title":"Loops closed","minimum":0},
          "sketch_completed":{"type":"boolean","title":"Sketch completed"},
          "new_passage_note":{"type":"string","title":"New passage"}
        }}
        """;

    private const string ExplorationFieldData =
        """
        {"type":"object","properties":{
          "conditions":{"type":"string","title":"Conditions underground"},
          "water_level":{"type":"string","title":"Water level",
            "enum":["low","normal","high","flood"]},
          "objective_reached":{"type":"boolean","title":"Objective reached"},
          "leads_found":{"type":"string","title":"Leads found"},
          "leads_remaining":{"type":"integer","title":"Leads still open","minimum":0}
        }}
        """;

    private const string Logistics =
        """
        {"type":"object","properties":{
          "permit_reference":{"type":"string","title":"Permit reference"},
          "permit_holder_caver_id":{"type":"string","title":"Permit holder",
            "pattern":"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"},
          "permit_holder_note":{"type":"string","title":"Permit holder, if not on the roster"},
          "key_holder_caver_id":{"type":"string","title":"Key holder",
            "pattern":"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"},
          "key_holder_note":{"type":"string","title":"Key holder, if not on the roster"},
          "landowner_caver_id":{"type":"string","title":"Landowner contact",
            "pattern":"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"},
          "landowner_note":{"type":"string","title":"Landowner contact, if not on the roster"},
          "callout_contact_caver_id":{"type":"string","title":"Callout contact",
            "pattern":"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"},
          "callout_contact_note":{"type":"string","title":"Callout contact, if not on the roster"},
          "access_notes":{"type":"string","title":"Access notes"},
          "cost_amount":{"type":"number","title":"Cost","minimum":0},
          "cost_currency":{"type":"string","title":"Currency"},
          "cost_note":{"type":"string","title":"What the cost covered"}
        }}
        """;

    private const string Safety =
        """
        {"type":"object","properties":{
          "incident_summary":{"type":"string","title":"What happened"},
          "incident_severity":{"type":"string","title":"Severity",
            "enum":["near_miss","minor","serious","rescue"]},
          "equipment_failure":{"type":"boolean","title":"Equipment failed"},
          "lessons_learned":{"type":"string","title":"Lessons learned"},
          "reported_to":{"type":"string","title":"Reported to"}
        }}
        """;

    /// <summary>
    /// The starting sections for a shipped code. Logistics and safety are the same for every
    /// purpose — a permit is a permit and a near miss is a near miss — while what a trip finds
    /// underground is the part that differs by what the trip was for. A code this does not know
    /// (a club's own row) starts with the general field data and the shared other two.
    /// </summary>
    public static TripStarterSections For(string code) => code switch
    {
        "survey" => new(SurveyFieldData, Logistics, Safety),
        "exploration" => new(ExplorationFieldData, Logistics, Safety),
        _ => new(GeneralFieldData, Logistics, Safety),
    };
}
