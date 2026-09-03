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
[Collection(PostgresCollection.Name)]
public sealed class TripImportPreviewResolutionTests : IAsyncLifetime, IDisposable
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
    /// One row naming all three caves and both people. One row rather than three, because what is
    /// being read is a resolution per name and putting them together proves the three answers come
    /// out of the same reading rather than out of three differently-configured ones.
    /// </summary>
    private string Sheet =>
        "Nr crt.,Data inceput,Titlu,Tara,Masiv/zona,Pesteri,Participanti,Tip\r\n"
        + $"1,17/04/2024,O tura,Romania,Masivul Necunoscut {tag},"
        + $"\"{Readable}; {Guarded}; {Hidden}\",\"{Shared}; {Alone}\",explorare {tag}\r\n";

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
            withheld.GetProperty("candidates").GetInt32().ShouldBe(0);
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
        people[0].GetProperty("candidates").GetInt32().ShouldBe(2);
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

    // ---------- helpers ----------

    /// <summary>
    /// Three caves — readable and placeable, readable but guarded, neither — and three roster
    /// entries, two of which answer to one name once folded.
    /// </summary>
    private async Task<(Guid Readable, Guid Guarded, Guid Hidden)> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var readable = Cave(Readable, editorId, guarded: false);
        var guarded = Cave(Guarded, strangerId, guarded: true);

        // Held back by an explicit refusal rather than by visibility: the seeded editors read past
        // visibility at the widest scope, so a matrix built on a private row would be proving that
        // the query ran and nothing about what it withheld.
        var hidden = Cave(Hidden, strangerId, guarded: false);
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

    private static Feature Cave(string name, Guid ownerId, bool guarded)
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
        };
    }

    private static object Options(
        bool createEverything = false,
        IReadOnlyDictionary<string, Guid>? caverChoices = null,
        IReadOnlyDictionary<string, Guid>? featureChoices = null) => new
        {
            delimiter = ",",
            multiValueSeparators = ";",
            slashSeparatedFields = Array.Empty<string>(),
            dateOrder = "dayFirst",
            columns = new Dictionary<string, string>(),
            visibility = "private",
            cavingGroupId = (Guid?)null,
            createMissingCaves = createEverything,
            createMissingAreas = createEverything,
            createMissingCavers = createEverything,
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

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
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
