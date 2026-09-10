// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text;
using System.Text.Json;
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
/// What the review screen is told before anybody confirms anything.
///
/// <para>
/// The confirmation is where a sheet becomes trips, but the decision is made here — and a decision
/// made in front of the sheet's own words is not a decision at all. So three things are on trial.
/// The preview says what every name was taken for, including the names it took for nothing. The
/// protection matrix holds on this read path exactly as it holds on the write path, because
/// proposing a cave is itself a read of that cave. And a value the reviewer settles by hand is
/// honoured only among the candidates they were offered — a chosen identifier is a person picking
/// from a list, never a second way to reach a feature the list withheld.
/// </para>
/// <para>
/// Every name in these sheets is invented, and every one of them carries this run's own suffix:
/// the database outlives one test here, so a plain name seeded by one would be a second claimant
/// in the next and the ambiguity rule would fire for the wrong reason.
/// </para>
/// </summary>
public sealed class TripImportPreviewResolutionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private Guid editorId;
    private Guid strangerId;
    private string tag = null!;

    private string Readable => $"Pestera Vizibila {tag}";

    private string Guarded => $"Pestera Pazita {tag}";

    private string Hidden => $"Pestera Interzisa {tag}";

    private string Shared => $"Ioana Campioana {tag}";

    private string Alone => $"Bogdan Ionescu {tag}";

    /// <summary>
    /// A name written the way a club sheet writes one when nobody wrote the surname down. It
    /// carries this run's suffix inside its first word rather than as a second one, because a
    /// suffix written as a second word would make it a name a person could be created from and
    /// the fixture would be proving the opposite of what it is here for.
    /// </summary>
    private string Initialled => $"Ionel{tag} A.";

    /// <summary>One word and nothing else, for the same reason and with the suffix inside it.</summary>
    private string Mononym => $"Gheorghita{tag}";

    /// <summary>Two full words nothing here answers to: the control that a switch can act on.</summary>
    private string Newcomer => $"Vasile Nou {tag}";

    /// <summary>
    /// One row naming all three caves and both people. One row rather than three, because what is
    /// being read is a resolution per name and putting them together proves the three answers come
    /// out of the same reading rather than out of three differently-configured ones.
    /// </summary>
    private string Sheet =>
        "Nr crt.,Data inceput,Titlu,Tara,Masiv/zona,Pesteri,Participanti,Tip\r\n"
        + $"1,17/04/2024,O tura,Romania,Masivul Necunoscut {tag},"
        + $"\"{Readable}; {Guarded}; {Hidden}\",\"{Shared}; {Alone}\",explorare {tag}\r\n";

    /// <summary>
    /// One row naming five people: the one two roster entries answer to, the one exactly one
    /// answers to, two nobody can be made from, and one nothing answers to that a switch could
    /// make. Together they are every state a name can be in, read out of one sheet so the figures
    /// are known to come from one reading.
    /// </summary>
    private string PeopleSheet =>
        "Nr crt.,Data inceput,Titlu,Participanti\r\n"
        + $"1,17/04/2024,O tura,\"{Shared}; {Alone}; {Initialled}; {Mononym}; {Newcomer}\"\r\n";

    public TripImportPreviewResolutionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tip-editor-{tag}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tip-owner-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"tip-editor-{tag}@t.local");
    }

    // ---------- the preview resolves, and the matrix holds while it does ----------

    /// <summary>
    /// The reviewer is shown what each name was taken for, and the two caves they may not place
    /// are shown to them as names nothing answered to — which is the same answer they would get
    /// for a cave that does not exist here at all.
    /// </summary>
    [Fact]
    public async Task The_preview_says_what_every_name_was_taken_for()
    {
        var (readableId, _, _) = await SeedAsync();

        var fileId = await UploadAsync("resolved.csv", Sheet);
        var preview = await PreviewAsync(fileId, Options(createEverything: true));

        var row = Items(preview)[0].GetProperty("resolution");
        row.ValueKind.ShouldNotBe(JsonValueKind.Null, "the preview must resolve, not echo the sheet");

        var caves = row.GetProperty("caves").EnumerateArray().ToList();
        caves.Count.ShouldBe(3);

        // The one this account may read and may place: named, with the identifier a link would
        // use, and not offered for creation — a switch decides only what happens to what missed.
        caves[0].GetProperty("state").GetString().ShouldBe("matched");
        caves[0].GetProperty("featureId").GetGuid().ShouldBe(readableId);
        caves[0].GetProperty("name").GetString().ShouldBe(Readable);
        caves[0].GetProperty("willCreate").GetBoolean().ShouldBeFalse();

        // The guarded one and the withheld one answer identically, and their answer is the one a
        // caller gets for a name nothing here holds. Nothing in the state, the count of candidates
        // or the name says that something was kept back — an answer that differed from "nothing
        // matched" would be a disclosure anybody could ask for by uploading a spreadsheet.
        foreach (var withheld in new[] { caves[1], caves[2] })
        {
            withheld.GetProperty("state").GetString().ShouldBe("unmatched");
            withheld.GetProperty("featureId").ValueKind.ShouldBe(JsonValueKind.Null);
            withheld.GetProperty("name").ValueKind.ShouldBe(JsonValueKind.Null);
            withheld.GetProperty("candidates").GetArrayLength().ShouldBe(0);
        }

        // The same reading with the switches off, which is where the words matter: a name that
        // becomes nothing at all still has to reach the trip's own text, or the sheet is quietly
        // the poorer for the refusal. The name that matched does not, because a link says it.
        var quiet = await PreviewAsync(fileId, Options());
        var note = Items(quiet)[0].GetProperty("resolution").GetProperty("locationNote").GetString();
        note.ShouldNotBeNull();
        note!.ShouldContain(Guarded);
        note.ShouldContain(Hidden);
        note.ShouldNotContain(Readable);

        // The person two roster entries answer to is shown as unsettled rather than as a plain
        // name, and the switch does not turn the ambiguity into a third person.
        var people = row.GetProperty("participants").EnumerateArray().ToList();
        people[0].GetProperty("state").GetString().ShouldBe("ambiguous");
        // Named, not counted: settling the name means picking one of these two, so they travel to
        // the review rather than a figure saying how many there were.
        var offered = people[0].GetProperty("candidates").EnumerateArray().ToList();
        offered.Count.ShouldBe(2);
        offered.ShouldAllBe(c => c.GetProperty("name").GetString()!.Length > 0);
        people[0].GetProperty("willCreate").GetBoolean().ShouldBeFalse();
        people[1].GetProperty("state").GetString().ShouldBe("matched");
    }

    /// <summary>
    /// The whole-file tables: one line per distinct value, and a count of every kind of thing a
    /// confirmation would add — stated even where it is nothing.
    /// </summary>
    [Fact]
    public async Task The_preview_counts_what_a_confirmation_would_add()
    {
        await SeedAsync();

        var fileId = await UploadAsync("proposals.csv", Sheet);

        var off = (await PreviewAsync(fileId, Options())).GetProperty("proposals");

        // Every count present at zero. A row that vanishes when it is nothing reads exactly like
        // a row that has not been worked out.
        off.GetProperty("newCaveCount").GetInt32().ShouldBe(0);
        off.GetProperty("newAreaCount").GetInt32().ShouldBe(0);
        off.GetProperty("newCaverCount").GetInt32().ShouldBe(0);
        off.GetProperty("newTripTypeCount").GetInt32().ShouldBe(0);

        // The candidate tables answer whatever the switches say, because matching does not depend
        // on them: three distinct cave names were read, one massif, two people, one type.
        off.GetProperty("caves").GetArrayLength().ShouldBe(3);
        off.GetProperty("areas").GetArrayLength().ShouldBe(1);
        off.GetProperty("people").GetArrayLength().ShouldBe(2);
        off.GetProperty("tripTypes").GetArrayLength().ShouldBe(1);
        off.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);

        var on = (await PreviewAsync(fileId, Options(createEverything: true))).GetProperty("proposals");

        // With the switches on: the two caves nothing answered to, the massif, the one type — and
        // still nobody, because the only unresolved name is the ambiguous one and a guess is not
        // what an on switch turns into either.
        on.GetProperty("newCaveCount").GetInt32().ShouldBe(2);
        on.GetProperty("newAreaCount").GetInt32().ShouldBe(1);
        on.GetProperty("newTripTypeCount").GetInt32().ShouldBe(1);
        on.GetProperty("newCaverCount").GetInt32().ShouldBe(0);
        on.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);

        // Both names on this sheet are two full words, so nothing here is a name a person could
        // not be made from — and the figure that says so is present saying nothing, which is the
        // only way a reviewer can tell it from a figure nobody worked out.
        off.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(0);
        on.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The figure a reviewer reads before confirming, on a sheet whose people cannot all be
    /// recorded: an initial where the surname should be and a name of one word.
    ///
    /// <para>
    /// Counting only the ambiguous names said nobody needed a decision while these two were being
    /// dropped from the trip they went on — a sheet that could not be imported honestly reading
    /// exactly like one that could. So the assertion is on the pair of headline figures together,
    /// on both settings of the switch, and on the list still naming everybody: the count is what
    /// a reviewer notices and the list is what they act on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_preview_counts_the_people_no_switch_can_create()
    {
        await SeedAsync();

        var fileId = await UploadAsync("people.csv", PeopleSheet);
        var on = (await PreviewAsync(fileId, Options(createEverything: true))).GetProperty("proposals");

        // One name two roster entries answer to, and two nobody can be made from at all.
        on.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);
        on.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(2);

        // The control, and the proof that the new figure is not simply counting what missed: the
        // fifth name missed too, and it is a name a person can be made from, so the switch makes
        // one. Two of the three that missed are a decision; this one is not.
        on.GetProperty("newCaverCount").GetInt32().ShouldBe(1);

        // Every name is still listed. The count is the headline; the list is the only place the
        // reviewer can see which two names it is talking about.
        var people = on.GetProperty("people").EnumerateArray().ToList();
        people.Count.ShouldBe(5);
        foreach (var name in new[] { Initialled, Mononym })
        {
            var entry = people.Single(p => p.GetProperty("source").GetString() == name);
            entry.GetProperty("state").GetString().ShouldBe("unmatched");
            entry.GetProperty("candidates").GetArrayLength().ShouldBe(0);
            entry.GetProperty("willCreate").GetBoolean().ShouldBeFalse();
            entry.GetProperty("mayCreate").GetBoolean().ShouldBeFalse();
        }

        // With the switch off the figure does not move, because the switch is not what is
        // refusing these two. A number that fell to zero when the switch went off would be
        // telling the reviewer they had already dealt with it.
        var off = (await PreviewAsync(fileId, Options())).GetProperty("proposals");
        off.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(2);
        off.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);
        off.GetProperty("newCaverCount").GetInt32().ShouldBe(0);

        // The two facts pulled apart, on the setting where they look alike. With the switch off
        // nothing is created, so "will not be created" is true of every name that missed — the
        // ordinary one included. Whether a person *could* be made from the name is a different
        // answer, and it is stated separately so that a screen reading it cannot list somebody
        // among the people nothing can be done about when a single switch would make them.
        var quiet = off.GetProperty("people").EnumerateArray().ToList();
        var ordinary = quiet.Single(p => p.GetProperty("source").GetString() == Newcomer);
        ordinary.GetProperty("state").GetString().ShouldBe("unmatched");
        ordinary.GetProperty("willCreate").GetBoolean().ShouldBeFalse();
        ordinary.GetProperty("mayCreate").GetBoolean().ShouldBeTrue();

        // And the ones the count is about answer the other way on the same setting, so the two
        // fields are not simply agreeing with each other everywhere.
        foreach (var name in new[] { Initialled, Mononym })
        {
            quiet.Single(p => p.GetProperty("source").GetString() == name)
                .GetProperty("mayCreate").GetBoolean().ShouldBeFalse();
        }
    }

    /// <summary>
    /// The same sheet read by somebody who has said their club writes people that way: the two
    /// names stop being a decision and become people, and every figure the review renders moves
    /// with them.
    ///
    /// <para>
    /// Read on the wire rather than only in the rule, because the figures are what the reviewer
    /// acts on and they are worked out one layer above it. A screen that went on reporting people
    /// as impossible while the import was about to create them would be the same failure as the
    /// one this pair of counts was added for, pointing the other way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Allowing_abbreviated_names_moves_them_out_of_the_decisions_and_into_the_creations()
    {
        await SeedAsync();

        var fileId = await UploadAsync("people.csv", PeopleSheet);
        var on = (await PreviewAsync(
                fileId, Options(createEverything: true, createAbbreviatedCavers: true)))
            .GetProperty("proposals");

        // Nobody is left that no person can be made from, and the three names nothing answered to
        // are now three people the confirmation would add.
        on.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(0);
        on.GetProperty("newCaverCount").GetInt32().ShouldBe(3);

        // And the part the choice deliberately does not touch. Two roster entries answer to this
        // name; which of them was underground is a question only a person can settle, and a name
        // being short has nothing to do with it.
        on.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);

        var people = on.GetProperty("people").EnumerateArray().ToList();
        people.Count.ShouldBe(5);
        foreach (var name in new[] { Initialled, Mononym })
        {
            var entry = people.Single(p => p.GetProperty("source").GetString() == name);
            entry.GetProperty("state").GetString().ShouldBe("unmatched");
            entry.GetProperty("mayCreate").GetBoolean().ShouldBeTrue();
            entry.GetProperty("willCreate").GetBoolean().ShouldBeTrue();
        }

        var shared = people.Single(p => p.GetProperty("source").GetString() == Shared);
        shared.GetProperty("state").GetString().ShouldBe("ambiguous");
        shared.GetProperty("willCreate").GetBoolean().ShouldBeFalse();
        shared.GetProperty("candidates").GetArrayLength().ShouldBe(2);

        // The choice widens what the roster switch creates; it creates nothing by itself. With
        // the roster switch off nobody is added, and the two names are still not reported as
        // people nobody could be made from — because now they could.
        var withoutTheSwitch = (await PreviewAsync(fileId, Options(createAbbreviatedCavers: true)))
            .GetProperty("proposals");
        withoutTheSwitch.GetProperty("newCaverCount").GetInt32().ShouldBe(0);
        withoutTheSwitch.GetProperty("uncreatablePersonCount").GetInt32().ShouldBe(0);
        withoutTheSwitch.GetProperty("ambiguousPersonCount").GetInt32().ShouldBe(1);
    }

    // ---------- a choice picks from the list, and only from the list ----------

    /// <summary>
    /// The reviewer settles the ambiguous name onto one of the two people, and the same control
    /// pointed at a cave they were never offered changes nothing.
    /// </summary>
    [Fact]
    public async Task A_choice_settles_an_ambiguity_and_reaches_nothing_that_was_withheld()
    {
        var (_, guardedId, _) = await SeedAsync();

        Guid chosenCaver;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            chosenCaver = await db.Cavers.AsNoTracking()
                .Where(c => c.FullName == Shared).Select(c => c.Id).FirstAsync();
        }

        var fileId = await UploadAsync("settled.csv", Sheet);
        var preview = await PreviewAsync(fileId, Options(
            createEverything: true,
            caverChoices: new Dictionary<string, Guid> { [Shared] = chosenCaver },

            // The guarded cave, named by its identifier. This account may read it and may not
            // place it, so the resolution never offered it — and a choice is a person picking one
            // of the candidates rather than a way to reach past the gate that removed them.
            featureChoices: new Dictionary<string, Guid> { [Guarded] = guardedId }));

        var row = Items(preview)[0].GetProperty("resolution");

        var settled = row.GetProperty("participants").EnumerateArray().First();
        settled.GetProperty("state").GetString().ShouldBe("matched");
        settled.GetProperty("caverId").GetGuid().ShouldBe(chosenCaver);

        var guarded = row.GetProperty("caves").EnumerateArray().ToList()[1];
        guarded.GetProperty("state").GetString().ShouldBe("unmatched");
        guarded.GetProperty("featureId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A sheet whose bytes are a Central European code page rather than UTF-8, read end to end:
    /// the upload, the store, the reader and the preview. This is where that path is proved,
    /// because the reader is the one piece of it that only exists behind a stored file.
    /// </summary>
    [Fact]
    public async Task A_sheet_that_is_not_utf8_is_read_and_the_preview_says_what_it_was_read_as()
    {
        await SeedAsync();

        var fileId = await UploadBytesAsync("codepage.csv", CodePageSheet());

        // Nothing states the encoding, so it is worked out — and the answer is reported as worked
        // out, which is the whole point: a wrong guess is invisible in the text, reading as the
        // wrong accents rather than as an error, so a guess nobody is told about cannot be fixed.
        var guessed = await PreviewAsync(fileId, Options());
        guessed.GetProperty("encoding").GetString().ShouldBe("windows1250");
        guessed.GetProperty("encodingSource").GetString().ShouldBe("guessed");
        Items(guessed)[0].GetProperty("title").GetString().ShouldBe("Peştera Urşilor");

        // And the reviewer overrules it. Western European reads the same bytes as different
        // letters, so an override that were quietly dropped would be visible here as the title
        // coming back unchanged.
        var stated = await PreviewAsync(fileId, Options(encoding: "windows1252"));
        stated.GetProperty("encoding").GetString().ShouldBe("windows1252");
        stated.GetProperty("encodingSource").GetString().ShouldBe("stated");
        Items(stated)[0].GetProperty("title").GetString().ShouldBe("Peºtera Urºilor");
    }

    /// <summary>
    /// A one-row invented sheet in Windows-1250. The five substitutions are the whole of what
    /// separates it from ASCII, written out by hand so the fixture states its own bytes rather
    /// than asking the decoder under test to produce them.
    /// </summary>
    private static byte[] CodePageSheet()
    {
        const string text =
            "Nr crt.,Data inceput,Titlu,Tara,Masiv/zona,Participanti,Tip\r\n"
            + "1,5/1/2024,Peştera Urşilor,România,M. Căpăţânii,\"Ion Anghel\",pestera\r\n";

        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = text[i] switch
            {
                'ş' => 0xBA, // s with cedilla
                'ţ' => 0xFE, // t with cedilla
                'ă' => 0xE3, // a with breve
                'â' => 0xE2, // a with circumflex
                'î' => 0xEE, // i with circumflex
                var c => (byte)c,
            };
        }

        return bytes;
    }

    // ---------- helpers ----------

    /// <summary>
    /// Three caves — readable and placeable, readable but guarded, neither — and three roster
    /// entries, two of which answer to one name once folded.
    /// </summary>
    private async Task<(Guid Readable, Guid Guarded, Guid Hidden)> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        var readable = Cave(Readable, editorId, guarded: false, caveTypeId);
        var guarded = Cave(Guarded, strangerId, guarded: true, caveTypeId);

        // Held back by an explicit refusal rather than by visibility: the seeded editors read past
        // visibility at the widest scope, so a matrix built on a private row would be proving that
        // the query ran and nothing about what it withheld.
        var hidden = Cave(Hidden, strangerId, guarded: false, caveTypeId);
        foreach (var cave in new[] { readable, guarded, hidden })
        {
            db.Features.Add(cave);

            // The closure row the write service would have written beside the ancestor array.
            // Writing only the array leaves the installation in a state the integrity verifier is
            // right to call broken, and it reads the whole database, so the failure would surface
            // in whichever unrelated test happens to run it.
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = cave.Id, AncestorId = cave.Id });
        }

        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read | AccessAction.ViewExactLocation,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = hidden.Id,
        });

        // Growing the trip type list is the taxonomy's right and the shipped editor group does not
        // hold it, so this account is given it explicitly. Without that the previews below would
        // be refused rather than answered, and what is under test here is what a preview says.
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Taxonomies,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });

        // And the roster's, for the same reason: enrolling people is the roster's right and the
        // editor group does not hold it either, so the switch that creates missing people would
        // otherwise be refused before a preview could say anything.
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Cavers,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });

        db.Cavers.Add(new Caver { FullName = Shared });

        // Written with the diacritics the other one lacks: folding is what makes these one name,
        // and a resolver comparing them as written would call this unambiguous.
        db.Cavers.Add(new Caver { FullName = Shared.Replace("Campioana", "Câmpioana") });
        db.Cavers.Add(new Caver { FullName = Alone });

        await db.SaveChangesAsync();
        return (readable.Id, guarded.Id, hidden.Id);
    }

    private static Feature Cave(string name, Guid ownerId, bool guarded, long caveTypeId)
    {
        var id = Guid.NewGuid();
        return new Feature
        {
            Id = id,
            Name = name,
            Kind = FeatureKind.Cave,
            OwnerUserId = ownerId,
            Visibility = Visibility.Public,
            LocationProtected = guarded,
            // Stamped here because the write service, which normally derives it, is not what put
            // this row in. A guard nothing reads is a guard that does not hold.
            IsProtectedEffective = guarded,
            AncestorIds = [id],
            // A cave is two rows: the feature and the subtype row that carries its
            // cave-specific attributes. The database only guards the direction that cannot
            // happen anyway — a subtype row without its feature — so a fixture that writes the
            // feature alone leaves behind a state no write path can produce and every read path
            // that projects a cave falls over, for every reader of the listing rather than only
            // for whoever owns the row.
            Cave = new Cave { Id = id, CaveTypeId = caveTypeId },
        };
    }

    private static object Options(
        bool createEverything = false,
        bool createAbbreviatedCavers = false,
        string? encoding = null,
        IReadOnlyDictionary<string, Guid>? caverChoices = null,
        IReadOnlyDictionary<string, Guid>? featureChoices = null) => new
        {
            delimiter = ",",
            multiValueSeparators = ";",
            slashSeparatedFields = Array.Empty<string>(),
            dateOrder = "dayFirst",
            encoding,
            columns = new Dictionary<string, string>(),
            visibility = "private",
            cavingGroupId = (Guid?)null,
            createMissingCaves = createEverything,
            createMissingAreas = createEverything,
            createMissingCavers = createEverything,
            createAbbreviatedCavers,
            createMissingTripTypes = createEverything,
            caverChoices = caverChoices ?? new Dictionary<string, Guid>(),
            featureChoices = featureChoices ?? new Dictionary<string, Guid>(),
        };

    private async Task<JsonElement> PreviewAsync(Guid fileId, object options)
    {
        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/preview", Body(new { options }));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<Guid> UploadAsync(string fileName, string text) =>
        await UploadBytesAsync(fileName, Encoding.UTF8.GetBytes(text));

    private async Task<Guid> UploadBytesAsync(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/csv");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await editor.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static List<JsonElement> Items(JsonElement preview) =>
        [.. preview.GetProperty("items").EnumerateArray()];

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
