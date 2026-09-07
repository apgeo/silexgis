// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A trip records three sections beyond its columns — what it found underground, what it needed
/// to happen, and what went wrong — and what each may say is described by a JSON schema the trip
/// purpose carries.
/// <para>
/// The load-bearing property, and the reason the version stamp exists at all: a club that
/// tightens a schema must not thereby invalidate every report already written under the looser
/// one. A stored report is measured against the version it was written against, and only a write
/// that actually rewrites the section is measured against the schema as it now stands.
/// </para>
/// </summary>
public sealed class TripSectionSchemaTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;  // Editor — writes trips
    private HttpClient admin = null!;   // Full administrator — keeps the vocabulary and its schemas

    // A Viewer, and deliberately not an Editor: the shipped Editors group reads every trip
    // regardless of visibility, so an "an editor cannot see it" test would prove nothing about
    // the rule. This one is given read of a single trip and nothing else.
    private HttpClient reader = null!;
    private Guid readerId;

    public TripSectionSchemaTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tsec-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tsec-adm-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsec-rd-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"tsec-ed-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tsec-adm-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tsec-rd-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        admin?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }

    /// <summary>
    /// A fresh installation has something in each section rather than three empty boxes, and each
    /// shipped schema is in the history under a version a report can be stamped with — a current
    /// schema missing from the history would leave every report measured against a text nobody
    /// can produce.
    /// </summary>
    [Fact]
    public async Task Shipped_purposes_start_with_all_three_sections_published_and_a_version_to_stamp()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        var survey = listed.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "survey");

        survey.GetProperty("fieldDataSchema").GetString().ShouldNotBeNull().ShouldContain("survey_grade");
        survey.GetProperty("logisticsSchema").GetString().ShouldNotBeNull()
            .ShouldContain("permit_holder_caver_id");
        survey.GetProperty("safetySchema").GetString().ShouldNotBeNull().ShouldContain("incident_severity");
        survey.GetProperty("fieldDataSchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);
        survey.GetProperty("logisticsSchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);
        survey.GetProperty("safetySchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);

        var id = survey.GetProperty("id").GetInt64();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var published = await db.TripTypeSchemas.AsNoTracking()
            .Where(s => s.TripTypeId == id)
            .Select(s => s.Section)
            .ToListAsync();
        published.Order().ShouldBe([TripSection.FieldData, TripSection.Logistics, TripSection.Safety]);
    }

    /// <summary>
    /// The whole reason the stamp exists. One report is written while the schema is loose, the
    /// schema is then tightened, and the report is saved again without its section being touched:
    /// it survives, measured against the version it was written under. The same value offered as
    /// a fresh answer is refused — so this is not "validation stopped running", which is the
    /// failure a test of the surviving half alone would pass.
    /// </summary>
    [Fact]
    public async Task A_report_written_under_a_looser_schema_survives_the_schema_being_tightened()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTypeAsync(suffix, """
            {"type":"object","properties":{"lamp":{"type":"string","title":"Lamp"}}}
            """);

        var created = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, new
        {
            fieldData = new { lamp = "carbide" },
        }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(created);
        var tripId = body.GetProperty("id").GetGuid();
        body.GetProperty("fieldDataSchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);

        // The club decides a lamp must be one of three, which the stored answer is not.
        var tightened = await admin.PutAsJsonAsync($"/api/v1/trip-types/{typeId}", TypeBody(suffix, """
            {"type":"object","properties":{"lamp":{"type":"string","title":"Lamp","enum":["led","acetylene","none"]}}}
            """));
        tightened.StatusCode.ShouldBe(HttpStatusCode.OK, await tightened.Content.ReadAsStringAsync());
        (await ReadJsonAsync(tightened)).GetProperty("fieldDataSchemaVersion").GetInt32()
            .ShouldBe(TripType.FirstSchemaVersion + 1);

        // Correcting the title of an old report does not make its author fix a section they never
        // opened: the section is absent from the request, so it is measured against its stamp.
        var renamed = await editor.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody($"{suffix} corrected", typeId, null));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var after = await ReadJsonAsync(renamed);
        after.GetProperty("fieldData").GetProperty("lamp").GetString().ShouldBe("carbide");
        after.GetProperty("fieldDataSchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);

        // Offering the same value as a new answer is measured against the schema as it now
        // stands, and refused. Without this half the test above would also pass if the write path
        // had simply stopped validating.
        var refused = await editor.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", TripBody(
            $"{suffix} corrected", typeId, new { fieldData = new { lamp = "carbide" } }));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await ReadCodeAsync(refused)).ShouldBe("trip_log.field_data_invalid");

        // And the refusal left the stored report exactly as it was.
        var reread = await editor.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        reread.GetProperty("fieldData").GetProperty("lamp").GetString().ShouldBe("carbide");
        reread.GetProperty("fieldDataSchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);
    }

    /// <summary>
    /// Each section is refused under a code of its own. A caller filling in three forms has to be
    /// told which of them the answer is about, and one shared code would make that a matter of
    /// reading prose.
    /// </summary>
    [Fact]
    public async Task A_value_that_does_not_fit_its_section_is_refused_under_that_sections_own_code()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTypeAsync(
            suffix,
            """{"type":"object","properties":{"depth":{"type":"number","title":"Depth"}}}""",
            """{"type":"object","properties":{"permit_reference":{"type":"string"}}}""",
            """{"type":"object","properties":{"equipment_failure":{"type":"boolean"}}}""");

        (string Section, object Bag, string Code)[] cases =
        [
            ("fieldData", new { fieldData = new { depth = "quite deep" } }, "trip_log.field_data_invalid"),
            ("logistics", new { logistics = new { permit_reference = 7 } }, "trip_log.logistics_invalid"),
            ("safety", new { safety = new { equipment_failure = "maybe" } }, "trip_log.safety_invalid"),
        ];

        foreach (var (section, bag, code) in cases)
        {
            var refused = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, bag));
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, section);
            (await ReadCodeAsync(refused)).ShouldBe(code, section);
        }

        // The same three sections, answered properly, are accepted — so the codes above are a
        // schema being enforced rather than the sections being refused outright.
        var accepted = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, new
        {
            fieldData = new { depth = 121.5 },
            logistics = new { permit_reference = "RO-2026-14" },
            safety = new { equipment_failure = false },
        }));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        var stored = await ReadJsonAsync(accepted);
        stored.GetProperty("logistics").GetProperty("permit_reference").GetString().ShouldBe("RO-2026-14");
        stored.GetProperty("safety").GetProperty("equipment_failure").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A permit holder, a key holder or a landowner is a person the roster knows, not a phone
    /// number typed into a bag. The shipped logistics schema says so in the only way that holds:
    /// the identity field takes an identifier's shape and refuses a bare name, while a free-text
    /// field beside it carries whoever the roster does not know — so recording a stranger never
    /// requires putting their details where no disclosure rule will ever look at them.
    /// </summary>
    [Fact]
    public async Task A_permit_or_key_holder_is_a_roster_reference_with_free_text_only_beside_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        var typeId = listed.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "exploration")
            .GetProperty("id").GetInt64();

        var caverId = await RosterHelper.CaverIdForAsync(
            factory, await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsec-key-{suffix}@t.local"));

        // A name where an identity belongs is refused: it would be personal data about someone
        // who is not a user, sitting where nothing inspects it.
        var refused = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, new
        {
            logistics = new { key_holder_caver_id = "Ion, the shepherd, 07xx xxx xxx" },
        }));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await ReadCodeAsync(refused)).ShouldBe("trip_log.logistics_invalid");

        // The roster identity is accepted, and so is the note beside it for somebody the roster
        // does not have — both halves, because a schema that refused the note as well would push
        // the same detail into whatever free-text field was left.
        var accepted = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, new
        {
            logistics = new
            {
                key_holder_caver_id = caverId.ToString(),
                landowner_note = "Ask at the last house before the bridge",
            },
        }));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        var stored = (await ReadJsonAsync(accepted)).GetProperty("logistics");
        stored.GetProperty("key_holder_caver_id").GetString().ShouldBe(caverId.ToString());
        stored.GetProperty("landowner_note").GetString().ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// Two audiences over one row. That something went wrong is told to everyone who may read
    /// the trip — a club counts incidents, finds them and notices a run of them, and a fact
    /// nobody can count is a fact nobody reviews. What went wrong names an identifiable member's
    /// mistake, so it is told only to whoever may change the trip.
    /// <para>
    /// Both halves over one fixture and one trip, because the failure to guard against is not
    /// "the rule stopped working" but "the rule stopped applying" — a test of the withheld half
    /// alone would also pass if the section had simply stopped being returned to anybody, and a
    /// test of the disclosed half alone would pass with no rule at all. The withheld side is a
    /// Viewer holding an explicit read of this one trip: genuinely able to read it, genuinely
    /// unable to write it, which is the only state that proves anything here.
    /// </para>
    /// <para>
    /// The timeline is asked the same question in the same test, because a narrower audience is
    /// only as narrow as its widest emitter and a diff of the section would hand over exactly
    /// the text the record withholds.
    /// </para>
    /// </summary>
    [Fact]
    public async Task What_went_wrong_is_for_whoever_may_change_the_trip_while_that_it_did_is_for_every_reader()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTypeAsync(
            suffix, null, null,
            """{"type":"object","properties":{"incident_account":{"type":"string","title":"What happened"}}}""");

        const string Account = "Ana ran out of light below the third pitch; the spare had been left at camp.";
        var created = await editor.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(suffix, typeId, new
        {
            hadIncident = true,
            safety = new { incident_account = Account },
        }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var tripId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        // Read of this one trip and nothing more — the state the withheld half is about.
        await GrantReadAsync(tripId, readerId);

        // Whoever may change the trip is told what happened, and under which version of the
        // purpose's schema it was recorded.
        var asWriter = await editor.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        asWriter.GetProperty("hadIncident").GetBoolean().ShouldBeTrue();
        asWriter.GetProperty("safety").GetProperty("incident_account").GetString().ShouldBe(Account);
        asWriter.GetProperty("safetySchemaVersion").GetInt32().ShouldBe(TripType.FirstSchemaVersion);

        // The reader is told the trip, and that something went wrong, and is told nothing at all
        // where the account would be — not an empty object, which would read as "nothing
        // happened" on exactly the record where that reading is worst.
        var asReader = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        asReader.GetProperty("title").GetString().ShouldNotBeNullOrEmpty();
        asReader.GetProperty("hadIncident").GetBoolean().ShouldBeTrue();
        asReader.GetProperty("safety").ValueKind.ShouldBe(JsonValueKind.Null);
        asReader.GetProperty("safetySchemaVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        asReader.GetRawText().ShouldNotContain("third pitch");

        // The listing answers the same way as the record: one rule, not one per surface.
        var listed = await reader.GetFromJsonAsync<JsonElement>("/api/v1/trip-logs/");
        var row = listed.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == tripId);
        row.GetProperty("hadIncident").GetBoolean().ShouldBeTrue();
        row.GetProperty("safety").ValueKind.ShouldBe(JsonValueKind.Null);

        // And so does the change history, which would otherwise be the way around all of it.
        var writerTimeline = await editor.GetStringAsync(
            $"/api/v1/history?entityType=TripLog&entityId={tripId}");
        writerTimeline.ShouldContain("third pitch");

        var readerTimeline = await reader.GetStringAsync(
            $"/api/v1/history?entityType=TripLog&entityId={tripId}");
        readerTimeline.ShouldNotContain("third pitch");
        // The event itself is not hidden — the trip was created, and saying so is not the same
        // as saying what it said.
        readerTimeline.ShouldContain(tripId.ToString());
    }

    /// <summary>
    /// What a party has to settle before it sets off — where and when it gathers, who is driving
    /// and from where, what gear is taken, whether the permit is in hand, what the forecast says,
    /// and where the talking happens — is recorded on the shipped logistics schema rather than in
    /// columns of its own, because nothing queries any of it.
    /// <para>
    /// Stated end to end over the shipped purpose rather than over a schema this test installs:
    /// what makes these facts usable is that a fresh installation already asks for them, and a
    /// test that supplied its own schema would pass with the shipped one empty. Each answer is
    /// written through the ordinary trip write, read back off the record, and then found in the
    /// generated write-up — which is the whole point of putting them in a section, since a
    /// section's answers reach the document without anything being told about them one by one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task What_a_party_settles_before_it_sets_off_is_asked_by_the_shipped_purpose_and_reaches_the_write_up()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        var exploration = listed.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "exploration");
        var typeId = exploration.GetProperty("id").GetInt64();
        var schema = exploration.GetProperty("logisticsSchema").GetString().ShouldNotBeNull();

        // Distinctive enough to find in a document, and carrying this run's own suffix so that a
        // trip some other test wrote cannot be read here as this one's answers.
        var answers = new Dictionary<string, object>
        {
            ["meeting_time"] = $"07:30 sharp, {suffix}",
            ["meeting_description"] = $"Layby past the last bridge, {suffix}",
            ["transport_drivers"] = $"Ana and Radu, {suffix}",
            ["transport_seats"] = 7,
            ["transport_departure"] = $"Cluj and Turda, {suffix}",
            ["equipment_note"] = $"Two 60 m ropes and a spare hanger set, {suffix}",
            ["permit_required"] = true,
            ["permit_obtained"] = false,
            ["weather_note"] = $"Rain forecast from midday, {suffix}",
            ["whatsapp_group_url"] = $"https://chat.example.invalid/{suffix}",
        };

        // The shipped purpose asks for every one of them: a key the schema does not carry would
        // be stored by the server and shown by nothing, which is a fact nobody can record.
        foreach (var key in answers.Keys)
        {
            schema.Contains($"\"{key}\"", StringComparison.Ordinal)
                .ShouldBeTrue($"the shipped logistics schema does not ask for {key}");
        }

        var created = await editor.PostAsJsonAsync(
            "/api/v1/trip-logs/", TripBody(suffix, typeId, new { logistics = answers }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var tripId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        // Back off the record, each answer as it was given — the round trip a form makes.
        var stored = (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"))
            .GetProperty("logistics");
        stored.GetProperty("meeting_time").GetString().ShouldBe($"07:30 sharp, {suffix}");
        stored.GetProperty("meeting_description").GetString().ShouldBe($"Layby past the last bridge, {suffix}");
        stored.GetProperty("transport_drivers").GetString().ShouldBe($"Ana and Radu, {suffix}");
        stored.GetProperty("transport_seats").GetInt32().ShouldBe(7);
        stored.GetProperty("transport_departure").GetString().ShouldBe($"Cluj and Turda, {suffix}");
        stored.GetProperty("equipment_note").GetString()
            .ShouldBe($"Two 60 m ropes and a spare hanger set, {suffix}");
        stored.GetProperty("permit_required").GetBoolean().ShouldBeTrue();
        stored.GetProperty("permit_obtained").GetBoolean().ShouldBeFalse();
        stored.GetProperty("weather_note").GetString().ShouldBe($"Rain forecast from midday, {suffix}");
        stored.GetProperty("whatsapp_group_url").GetString()
            .ShouldBe($"https://chat.example.invalid/{suffix}");

        // And in the write-up, each under the wording the purpose's schema gave it. The two
        // states that are not free text are checked with their labels attached: "Yes" and "7"
        // alone would be satisfied by any other line of the document.
        var document = await ReportTextAsync(editor, tripId);
        document.ShouldContain($"07:30 sharp, {suffix}");
        document.ShouldContain($"Layby past the last bridge, {suffix}");
        document.ShouldContain($"Ana and Radu, {suffix}");
        document.ShouldContain("Seats available: 7");
        document.ShouldContain($"Cluj and Turda, {suffix}");
        document.ShouldContain($"Two 60 m ropes and a spare hanger set, {suffix}");
        document.ShouldContain("Permit required: Yes");
        document.ShouldContain("Permit obtained: No");
        document.ShouldContain($"Rain forecast from midday, {suffix}");
        document.ShouldContain($"https://chat.example.invalid/{suffix}");
    }

    /// <summary>The words of the write-up this trip generates, as a word processor would read them.</summary>
    private static async Task<string> ReportTextAsync(HttpClient client, Guid tripId)
    {
        using var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return document.MainDocumentPart!.Document!.InnerText;
    }

    /// <summary>Grants one user read of one trip, and nothing else.</summary>
    private async Task GrantReadAsync(Guid tripId, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is the
            // feature-domain foreign key and stays empty here.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a club purpose carrying the supplied section schemas.</summary>
    private async Task<long> CreateTypeAsync(
        string suffix, string? fieldData, string? logistics = null, string? safety = null)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/v1/trip-types", TypeBody(suffix, fieldData, logistics, safety));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(created)).GetProperty("id").GetInt64();
    }

    private static object TypeBody(
        string suffix, string? fieldData, string? logistics = null, string? safety = null) => new
        {
            code = $"probe_{suffix}",
            name = "Probe",
            description = (string?)null,
            sortOrder = 900,
            fieldDataSchema = fieldData,
            logisticsSchema = logistics,
            safetySchema = safety,
        };

    /// <summary>
    /// A trip write. <paramref name="sections"/> is spread into the body, so a test that passes
    /// null sends a request that mentions no section at all — which is what "not editing them"
    /// looks like on the wire.
    /// </summary>
    private static Dictionary<string, object?> TripBody(string title, long typeId, object? sections)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = $"Section trip {title}",
            ["tripTypeId"] = typeId,
            ["tripDate"] = "2026-06-01",
            ["participants"] = Array.Empty<object>(),
            ["visibility"] = "private",
        };
        if (sections is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(sections).EnumerateObject())
            {
                body[property.Name] = property.Value;
            }
        }

        return body;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();
}
