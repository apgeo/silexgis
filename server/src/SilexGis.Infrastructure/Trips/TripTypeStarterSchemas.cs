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
/// <para>
/// What a party needs before it sets off — where it gathers, who drives, what gear is taken,
/// whether the permit is in hand — lives here rather than in columns of its own, because
/// nothing queries any of it: it is read by the people going and printed into the report, and
/// a jsonb key costs a string literal where a column costs a migration. The one planning fact
/// that is not here is where the party meets, which is a geometry a map must find.
/// </para>
/// <para>
/// The two permit questions have three states and not two, and whatever reads them later must
/// read them that way: the key absent means nobody has answered, <c>false</c> means somebody
/// answered "no", and only the first is an unanswered question. A readiness check that treated
/// an absent key as "no permit needed" would clear a trip nobody had thought about. The form
/// that draws these bags therefore shows an unanswered question as neither ticked nor unticked
/// and offers a way back to that state, so both answers and the absence of one are reachable.
/// </para>
/// <para>
/// The gear note is free text and is not derived from anything the caves hold: this system
/// stores no rigging data at all — not a pitch, not an anchor, not a rope length per drop — so
/// there is nothing to seed such a list from, and a structured gear list that nothing could
/// fill would be worse than a note somebody writes.
/// </para>
/// <para>
/// The weather note sits beside the trip's own weather column and does not replace it: the
/// column records what the weather turned out to be, and this key records what the forecast
/// said while the trip was still being planned.
/// </para>
/// <para>
/// Two things a party plans around are deliberately absent, and the reason belongs beside the
/// keys rather than in a document nobody opens.
/// </para>
/// <para>
/// Whether a cave floods, or is shut for part of the year, is answered nowhere in this system.
/// A cave carries no such field, and the data-driven property bag that lets other kinds of
/// feature grow attributes without a migration cannot reach one: a cave is a subtyped kind, and
/// the database refuses a subtyped row a data-level type at all. So the flag would be two
/// ordinary columns on the cave, beside the protection class that already lives there, plus
/// their redaction in the change history, their fields on the cave form and their wording. That
/// is cave work wearing a trip-planning label, and it carries a disclosure question of its own:
/// "this entrance floods after rain" is a fact about a guarded cave, and would have to be
/// withheld from exactly the people a plan invites. What ships instead is the note the party
/// writes for itself, above.
/// </para>
/// <para>
/// A discussion thread on the plan is likewise not built, and the group-chat link above is the
/// whole of the answer, because a club's talking already happens somewhere else. If a thread is
/// ever wanted it is not a new mechanism: the remarks on a document are a shipped one-level
/// thread whose readership is decided entirely by its parent's — no per-remark grant and no
/// second rule to keep in step — so it becomes a matter of letting that parent be either kind
/// and addressing every route through the parent, so there is only ever one door onto the rows.
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
          "meeting_time":{"type":"string","title":"Meeting time"},
          "meeting_description":{"type":"string","title":"Meeting point, described"},
          "transport_drivers":{"type":"string","title":"Drivers"},
          "transport_seats":{"type":"integer","title":"Seats available","minimum":0},
          "transport_departure":{"type":"string","title":"Departure points"},
          "equipment_note":{"type":"string","title":"Equipment and rigging"},
          "permit_required":{"type":"boolean","title":"Permit required"},
          "permit_obtained":{"type":"boolean","title":"Permit obtained"},
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
          "weather_note":{"type":"string","title":"Weather note"},
          "whatsapp_group_url":{"type":"string","title":"Group chat link"},
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
