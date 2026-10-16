// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Cabinets;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Features.GeoreferencedMaps;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A filter world against the list it stands beside, asked by the same people over real HTTP.
/// </summary>
/// <remarks>
/// The conformance suite proves each world against fixtures written for it. This proves the claim
/// those fixtures stand for: that a world sees exactly what its entity's own list shows each reader
/// — the owner, somebody in the club the rows are shared with, and somebody with no relation to
/// them — so the filter is neither a more generous way to ask the list's question nor a stingier
/// one that makes a saved filter quietly miss rows its reader can open.
/// </remarks>
public sealed class FilterWorldListParityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly System.Text.Json.JsonSerializerOptions Wire = BuildWire();

    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public FilterWorldListParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(GeoreferencedMapFilterWorld.Key)]
    [InlineData(CabinetFilterWorld.Key)]
    [InlineData(SurveyModelFilterWorld.Key)]
    [InlineData(PhotographFilterWorld.Key)]
    [InlineData(GeofileFilterWorld.Key)]
    public async Task Anonymous_callers_cannot_ask_the_world(string world)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/filters/query", new FilterQueryRequest(About(world, Named(tag))));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_georeferenced_map_world_shows_each_reader_what_the_map_list_shows_them()
    {
        var people = await PeopleAsync("geo");
        Guid privateMap, clubMap, publicMap, openCaveMap, protectedCaveMap;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var openCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: false);
            var protectedCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: true);

            privateMap = Map(db, people.OwnerId, "private", Visibility.Private).Id;
            var club = Map(db, people.OwnerId, "club", Visibility.CavingGroup);
            club.CavingGroupId = people.ClubId;
            clubMap = club.Id;
            publicMap = Map(db, people.OwnerId, "public", Visibility.Public).Id;
            var open = Map(db, people.OwnerId, "open cave", Visibility.Public);
            open.CaveFeatureId = openCave.Id;
            open.MapKind = MapKind.CaveMap;
            openCaveMap = open.Id;
            // Public, so the walk admits it to everybody; only the cave's protection keeps it back.
            var guarded = Map(db, people.OwnerId, "protected cave", Visibility.Public);
            guarded.CaveFeatureId = protectedCave.Id;
            guarded.MapKind = MapKind.CaveMap;
            protectedCaveMap = guarded.Id;
            await db.SaveChangesAsync();
        }

        var expected = new Dictionary<HttpClient, Guid[]>
        {
            // The owner places their own cave exactly, so the cave map reaches them.
            [people.Owner] = [privateMap, clubMap, publicMap, openCaveMap, protectedCaveMap],
            [people.Clubmate] = [clubMap, publicMap, openCaveMap],
            [people.Outsider] = [publicMap, openCaveMap],
        };

        foreach (var (client, ids) in expected)
        {
            var listed = await ListedMapsAsync(client);
            var found = await WorldIdsAsync(client, GeoreferencedMapFilterWorld.Key, Named(tag));

            listed.ShouldBe(ids, ignoreOrder: true);
            found.Ids.ShouldBe(listed, ignoreOrder: true);
            // A total taken before the cave rule would count the map it did not return.
            found.Total.ShouldBe(listed.Count);
        }

        // The map's own field, as the owner asks it: the kind narrows within what they see.
        var caveMaps = await WorldIdsAsync(
            people.Owner,
            GeoreferencedMapFilterWorld.Key,
            new AllOfNode(
            [
                Named(tag),
                new ConditionNode(GeoreferencedMapFilterFields.MapKind, FilterOp.In, [new IdValue("caveMap")]),
            ]));
        caveMaps.Ids.ShouldBe([openCaveMap, protectedCaveMap], ignoreOrder: true);

        people.Dispose();
    }

    [Fact]
    public async Task The_cabinet_world_shows_every_reader_the_whole_tree_as_the_cabinet_list_does()
    {
        var people = await PeopleAsync("cab");
        Guid root, child;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = new CabinetWriteService(db);
            var parent = await writer.CreateAsync($"Archive {tag}", null, null);
            await db.SaveChangesAsync();
            var shelf = await writer.CreateAsync($"Surveys {tag}", null, parent.Id);
            await db.SaveChangesAsync();
            root = parent.Id;
            child = shelf.Id;
        }

        foreach (var client in new[] { people.Owner, people.Clubmate, people.Outsider })
        {
            var listed = await ListedCabinetsAsync(client);
            var found = await WorldIdsAsync(client, CabinetFilterWorld.Key, Named(tag));

            listed.ShouldBe([root, child], ignoreOrder: true);
            found.Ids.ShouldBe(listed, ignoreOrder: true);
            found.Total.ShouldBe(2);
        }

        // The shelf is told apart by where it sits, which every reader of the tree already sees.
        var hit = (await QueryAsync(people.Outsider, CabinetFilterWorld.Key, Named($"Surveys {tag}"))).Hits.Single();
        hit.Subtitle.ShouldBe($"Archive {tag}");
        hit.Placeable.ShouldBeFalse();

        people.Dispose();
    }

    [Fact]
    public async Task The_survey_model_world_shows_each_reader_what_the_caves_model_lists_show_them()
    {
        var people = await PeopleAsync("sm");
        Feature openCave, protectedCave, privateCave, clubCave;
        Guid openModel, protectedModel, privateModel, clubModel;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            openCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: false);
            // Public, so every reader may read the cave; only its protection closes its models.
            protectedCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: true);
            privateCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Private, locationProtected: false);
            clubCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.CavingGroup, locationProtected: false);
            clubCave.CavingGroupId = people.ClubId;

            openModel = SurveyModelWorldFixture.Add(db, openCave, people.OwnerId, $"Model open {tag}").Id;
            protectedModel = SurveyModelWorldFixture.Add(db, protectedCave, people.OwnerId, $"Model protected {tag}").Id;
            privateModel = SurveyModelWorldFixture.Add(db, privateCave, people.OwnerId, $"Model private {tag}").Id;
            clubModel = SurveyModelWorldFixture.Add(db, clubCave, people.OwnerId, $"Model club {tag}").Id;
            await db.SaveChangesAsync();
        }

        Feature[] caves = [openCave, protectedCave, privateCave, clubCave];
        var expected = new Dictionary<HttpClient, Guid[]>
        {
            // The owner places their own protected cave exactly, so its model reaches them.
            [people.Owner] = [openModel, protectedModel, privateModel, clubModel],
            // Neither may place the protected cave, though both may read it.
            [people.Clubmate] = [openModel, clubModel],
            [people.Outsider] = [openModel],
        };

        foreach (var (client, ids) in expected)
        {
            var listed = await ListedModelsAsync(client, caves);
            var found = await WorldIdsAsync(client, SurveyModelFilterWorld.Key, Named(tag));

            listed.ShouldBe(ids, ignoreOrder: true);
            found.Ids.ShouldBe(listed, ignoreOrder: true);
            // A total taken before the exact-view rule would count the model it did not return.
            found.Total.ShouldBe(listed.Count);
        }

        // The protected cave itself stays readable to the outsider: what keeps its model back is
        // the cave's protection, not its visibility, which is the case the comparison is about.
        (await people.Outsider.GetAsync($"/api/v1/caves/{protectedCave.Id}/survey-models"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The model's own field narrows within what the reader sees.
        var lineplots = await WorldIdsAsync(
            people.Owner,
            SurveyModelFilterWorld.Key,
            new AllOfNode(
            [
                Named(tag),
                new ConditionNode(SurveyModelFilterFields.Format, FilterOp.In, [new IdValue("lox")]),
                new ConditionNode(SurveyModelFilterFields.SurveyedAt, FilterOp.Between,
                [
                    new InstantValue(new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero)),
                    new InstantValue(new DateTimeOffset(2024, 8, 31, 0, 0, 0, TimeSpan.Zero)),
                ]),
            ]));
        lineplots.Ids.ShouldBe(expected[people.Owner], ignoreOrder: true);

        people.Dispose();
    }

    [Fact]
    public async Task The_photograph_world_shows_each_reader_what_the_gallery_shows_them_and_never_places_one()
    {
        var people = await PeopleAsync("ph");
        var token = PhotographWorldFixture.Token(tag);
        Guid privatePhoto, clubPhoto, publicPhoto, protectedCavePhoto;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            privatePhoto = ConformanceSeeds.Photograph(db, $"Photo private {token}", people.OwnerId, Visibility.Private).Id;
            var club = ConformanceSeeds.Photograph(db, $"Photo club {token}", people.OwnerId, Visibility.CavingGroup);
            club.CavingGroupId = people.ClubId;
            clubPhoto = club.Id;
            publicPhoto = ConformanceSeeds.Photograph(db, $"Photo public {token}", people.OwnerId, Visibility.Public).Id;
            protectedCavePhoto = ConformanceSeeds.Photograph(db, $"Photo in protected cave {token}", people.OwnerId, Visibility.Public).Id;
            var protectedCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: true);
            await db.SaveChangesAsync();

            // A fix inside the protected cave, on a picture filed against it: the gallery lists the
            // picture to everybody and its position only to whoever may place the cave.
            var file = await db.StoredFiles.SingleAsync(f =>
                db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.DocumentId == protectedCavePhoto));
            file.Geom = new NetTopologySuite.Geometries.Point(25.0, 45.5) { SRID = 4326 };
            file.PositionSource = SilexGis.Domain.Import.PhotoPositionSource.Exif;
            db.Attachments.Add(new Attachment { FileId = file.Id, FeatureId = protectedCave.Id });
            await db.SaveChangesAsync();
        }

        var expected = new Dictionary<HttpClient, Guid[]>
        {
            [people.Owner] = [privatePhoto, clubPhoto, publicPhoto, protectedCavePhoto],
            [people.Clubmate] = [clubPhoto, publicPhoto, protectedCavePhoto],
            [people.Outsider] = [publicPhoto, protectedCavePhoto],
        };

        foreach (var (client, ids) in expected)
        {
            var gallery = await ListedPhotosAsync(client, token);
            var found = await QueryAsync(client, PhotographFilterWorld.Key, Titled(token));

            gallery.Select(p => p.DocumentId).ShouldBe(ids, ignoreOrder: true);
            found.Hits.Select(h => h.Id).ShouldBe(gallery.Select(p => p.DocumentId), ignoreOrder: true);
            found.Total.ShouldBe(gallery.Count);
            // Whatever the gallery would disclose, a hit never places a picture.
            found.Hits.ShouldAllBe(h => !h.Placeable);
        }

        // The rule the world stays out of, seen working on the gallery: the owner is told where the
        // picture was taken, the outsider is shown the picture without it.
        (await ListedPhotosAsync(people.Owner, token)).Single(p => p.DocumentId == protectedCavePhoto)
            .Position.ShouldNotBeNull();
        (await ListedPhotosAsync(people.Outsider, token)).Single(p => p.DocumentId == protectedCavePhoto)
            .Position.ShouldBeNull();

        // A document that is not a picture is not a photograph, whoever may read it.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = ConformanceSeeds.File(db, people.OwnerId, FileKind.Other, "pdf");
            await db.SaveChangesAsync();
            var paper = await db.Documents.SingleAsync(d =>
                db.DocumentVersions.Any(v => v.DocumentId == d.Id && v.Id == file.DocumentVersionId));
            paper.Title = $"Paper {token}";
            paper.Visibility = Visibility.Public;
            await db.SaveChangesAsync();
        }

        (await QueryAsync(people.Outsider, PhotographFilterWorld.Key, Titled(token))).Hits
            .Select(h => h.Id).ShouldBe(expected[people.Outsider], ignoreOrder: true);

        people.Dispose();
    }

    [Fact]
    public async Task The_geofile_world_shows_each_reader_what_the_geofile_list_shows_them()
    {
        var people = await PeopleAsync("gf");
        Guid privateFile, clubFile, publicFile;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            privateFile = GeofileWorldFixture.Add(db, $"Track private {tag}", people.OwnerId, Visibility.Private).Id;
            var club = GeofileWorldFixture.Add(db, $"Track club {tag}", people.OwnerId, Visibility.CavingGroup);
            club.CavingGroupId = people.ClubId;
            clubFile = club.Id;
            publicFile = GeofileWorldFixture.Add(db, $"Track public {tag}", people.OwnerId, Visibility.Public).Id;
            await db.SaveChangesAsync();
        }

        var expected = new Dictionary<HttpClient, Guid[]>
        {
            [people.Owner] = [privateFile, clubFile, publicFile],
            [people.Clubmate] = [clubFile, publicFile],
            [people.Outsider] = [publicFile],
        };

        foreach (var (client, ids) in expected)
        {
            var listed = await ListedGeofilesAsync(client);
            var found = await WorldIdsAsync(client, GeofileFilterWorld.Key, Named(tag));

            listed.ShouldBe(ids, ignoreOrder: true);
            found.Ids.ShouldBe(listed, ignoreOrder: true);
            found.Total.ShouldBe(listed.Count);
        }

        people.Dispose();
    }

    // ---------- the gallery reads the photograph world's filter ----------

    [Fact]
    public async Task The_gallery_narrowed_by_a_filter_shows_each_reader_only_what_they_could_see_and_still_withholds_a_position()
    {
        var people = await PeopleAsync("gfl");
        var token = PhotographWorldFixture.Token(tag);
        Guid privateKept, clubKept, publicKept, protectedKept, publicDropped;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            privateKept = ConformanceSeeds.Photograph(db, $"Kept private {token}", people.OwnerId, Visibility.Private).Id;
            var club = ConformanceSeeds.Photograph(db, $"Kept club {token}", people.OwnerId, Visibility.CavingGroup);
            club.CavingGroupId = people.ClubId;
            clubKept = club.Id;
            publicKept = ConformanceSeeds.Photograph(db, $"Kept public {token}", people.OwnerId, Visibility.Public).Id;
            protectedKept = ConformanceSeeds.Photograph(db, $"Kept in protected cave {token}", people.OwnerId, Visibility.Public).Id;
            publicDropped = ConformanceSeeds.Photograph(db, $"Dropped public {token}", people.OwnerId, Visibility.Public).Id;
            var protectedCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: true);
            await db.SaveChangesAsync();

            var file = await db.StoredFiles.SingleAsync(f =>
                db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.DocumentId == protectedKept));
            file.Geom = new NetTopologySuite.Geometries.Point(25.0, 45.5) { SRID = 4326 };
            file.PositionSource = SilexGis.Domain.Import.PhotoPositionSource.Exif;
            db.Attachments.Add(new Attachment { FileId = file.Id, FeatureId = protectedCave.Id });
            await db.SaveChangesAsync();
        }

        // The gallery's own search keeps the answer to this test's rows; the filter narrows them.
        var filter = FilterToken.Encode(About(PhotographFilterWorld.Key, Titled("Kept")));
        var expected = new Dictionary<HttpClient, Guid[]>
        {
            [people.Owner] = [privateKept, clubKept, publicKept, protectedKept],
            [people.Clubmate] = [clubKept, publicKept, protectedKept],
            // A Viewer in no club, holding no grant: the owner's private and club pictures match the
            // filter and are still not hers to see.
            [people.Outsider] = [publicKept, protectedKept],
        };

        foreach (var (client, ids) in expected)
        {
            var unfiltered = await ListedPhotosAsync(client, token);
            unfiltered.Select(p => p.DocumentId).ShouldContain(publicDropped);

            var page = await GalleryPageAsync(client, $"search={token}&filter={filter}");
            page.Items.Select(p => p.DocumentId).ShouldBe(ids, ignoreOrder: true);
            page.TotalItems.ShouldBe(ids.Length);
        }

        // The filter says nothing about positions, so the per-picture disclosure is unchanged by it:
        // the owner is told where the protected cave's picture was taken, the outsider is not.
        (await GalleryPageAsync(people.Owner, $"search={token}&filter={filter}")).Items
            .Single(p => p.DocumentId == protectedKept).Position.ShouldNotBeNull();
        (await GalleryPageAsync(people.Outsider, $"search={token}&filter={filter}")).Items
            .Single(p => p.DocumentId == protectedKept).Position.ShouldBeNull();

        people.Dispose();
    }

    [Fact]
    public async Task The_gallery_runs_a_saved_photograph_filter_by_its_id_only_for_readers_of_the_filter()
    {
        var people = await PeopleAsync("gsf");
        var token = PhotographWorldFixture.Token(tag);
        Guid kept;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            kept = ConformanceSeeds.Photograph(db, $"Kept public {token}", people.OwnerId, Visibility.Public).Id;
            ConformanceSeeds.Photograph(db, $"Dropped public {token}", people.OwnerId, Visibility.Public);
            await db.SaveChangesAsync();
        }

        var saved = await people.Owner.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Kept pictures {tag}",
            document = About(PhotographFilterWorld.Key, Titled("Kept")),
            visibility = "private",
        }, Wire);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var id = (await saved.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();

        var owners = await GalleryPageAsync(people.Owner, $"search={token}&savedFilterId={id}");
        owners.Items.Select(p => p.DocumentId).ShouldBe([kept]);
        owners.TotalItems.ShouldBe(1);

        // The filter is private to its author: to the outsider, who may see both pictures, it is
        // not there — the same refusal an invented id gets — and the gallery is never answered
        // unfiltered in its place.
        (await ListedPhotosAsync(people.Outsider, token)).Count.ShouldBe(2);
        var others = await people.Outsider.GetAsync($"/api/v1/photos?search={token}&savedFilterId={id}");
        var invented = await people.Outsider.GetAsync($"/api/v1/photos?savedFilterId={Guid.NewGuid()}");
        others.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await others.Content.ReadAsStringAsync()).ShouldContain(SurfaceFilters.SavedNotFoundCode);

        people.Dispose();
    }

    [Fact]
    public async Task The_gallery_refuses_a_filter_it_cannot_read_and_never_answers_unfiltered_instead()
    {
        var people = await PeopleAsync("grf");
        var aboutPhotographs = FilterToken.Encode(About(PhotographFilterWorld.Key, Titled("x")));
        var aboutDocuments = FilterToken.Encode(About(DocumentFilterWorld.Key, Titled("x")));
        // The camera is a gallery control the photograph world deliberately does not declare.
        var unknownField = FilterToken.Encode(About(
            PhotographFilterWorld.Key,
            new ConditionNode("camera", FilterOp.Contains, [new TextValue("x")])));

        var cases = new (string Query, string Code)[]
        {
            ("filter=eyJ2ZXJzaW9uIjox", SurfaceFilters.MalformedCode),
            // A document filter is not a gallery filter, although every photograph is a document.
            ($"filter={aboutDocuments}", SurfaceFilters.WrongWorldCode),
            ($"filter={unknownField}", SurfaceFilters.InvalidCode),
            ($"filter={aboutPhotographs}&savedFilterId={Guid.NewGuid()}", SurfaceFilters.AmbiguousCode),
        };

        foreach (var (query, code) in cases)
        {
            var response = await people.Owner.GetAsync($"/api/v1/photos?{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            (await response.Content.ReadAsStringAsync()).ShouldContain(code, customMessage: query);
        }

        // The positive twin: the same reader, a readable filter, an answer.
        (await people.Owner.GetAsync($"/api/v1/photos?filter={aboutPhotographs}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/photos?filter={aboutPhotographs}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        people.Dispose();
    }

    [Fact]
    public async Task The_gallery_controls_written_as_conditions_match_what_the_controls_matched()
    {
        // The gallery writes its search box, its tag and its date range into the filter document
        // rather than sending them beside it, so a saved gallery view keeps them. That is only
        // honest if each condition answers what its control answered: asked both ways, the same
        // pictures come back.
        var people = await PeopleAsync("gctl");
        var token = PhotographWorldFixture.Token(tag);
        var phrase = $"cascade {tag}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid captioned, placed, older;
        long tagId;
        var slug = $"gallery-{tag}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var label = new Tag { Name = $"Gallery {tag}", Slug = slug };
            db.Tags.Add(label);

            // Taken on its camera's date, captioned, and tagged.
            var a = ConformanceSeeds.Photograph(db, $"A {token}", people.OwnerId, Visibility.Public);
            db.PhotoDetails.Add(new PhotoDetails { DocumentId = a.Id, Caption = $"Under the {phrase}" });
            // No camera date, so it is dated by its upload — today — and named by place.
            var b = ConformanceSeeds.Photograph(db, $"B {token}", people.OwnerId, Visibility.Public);
            db.PhotoDetails.Add(new PhotoDetails { DocumentId = b.Id, PlaceName = $"Valley of the {phrase}" });
            var c = ConformanceSeeds.Photograph(db, $"C {token}", people.OwnerId, Visibility.Public);
            await db.SaveChangesAsync();

            Guid[] documents = [a.Id, b.Id, c.Id];
            var fileOf = await (from f in db.StoredFiles
                                join v in db.DocumentVersions on f.DocumentVersionId equals v.Id
                                where documents.Contains(v.DocumentId)
                                select new { v.DocumentId, FileId = f.Id })
                .ToDictionaryAsync(x => x.DocumentId, x => x.FileId);
            await db.StoredFiles.Where(f => f.Id == fileOf[b.Id])
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.ContentCreatedAt, (DateTimeOffset?)null));
            await db.StoredFiles.Where(f => f.Id == fileOf[c.Id])
                .ExecuteUpdateAsync(u => u.SetProperty(
                    f => f.ContentCreatedAt, (DateTimeOffset?)new DateTimeOffset(2023, 1, 1, 9, 0, 0, TimeSpan.Zero)));
            db.Taggings.Add(new Tagging
            {
                TagId = label.Id,
                EntityType = AttachedEntityType.StoredFile,
                EntityId = fileOf[a.Id],
            });
            await db.SaveChangesAsync();
            (captioned, placed, older, tagId) = (a.Id, b.Id, c.Id, label.Id);
        }

        string Doc(FilterNode where) => FilterToken.Encode(About(PhotographFilterWorld.Key, where));
        static InstantValue Midnight(DateOnly day) =>
            new(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        async Task<Guid[]> Ids(string query) =>
            [.. (await GalleryPageAsync(people.Owner, query)).Items.Select(p => p.DocumentId)];

        var term = Uri.EscapeDataString(phrase);
        var search = Doc(new AnyOfNode(
        [
            Titled(phrase),
            new ConditionNode(PhotographFilterFields.Caption, FilterOp.Contains, [new TextValue(phrase)]),
            new ConditionNode(PhotographFilterFields.PlaceName, FilterOp.Contains, [new TextValue(phrase)]),
        ]));
        var bySearch = await Ids($"search={term}");
        bySearch.ShouldBe([captioned, placed], ignoreOrder: true);
        (await Ids($"filter={search}")).ShouldBe(bySearch, ignoreOrder: true);

        var tagged = Doc(new AllOfNode(
        [
            Titled(token),
            new ConditionNode(PhotographFilterFields.Tag, FilterOp.Equals, [new IdValue(slug)]),
        ]));
        var byTag = await Ids($"search={token}&tagId={tagId}");
        byTag.ShouldBe([captioned]);
        (await Ids($"filter={tagged}")).ShouldBe(byTag, ignoreOrder: true);

        // The range control's two ends: from the first day's start, up to the last day's end.
        foreach (var (from, to, expected) in new[]
        {
            (new DateOnly(2025, 7, 19), new DateOnly(2025, 7, 19), new[] { captioned }),
            (today, today, new[] { placed }),
            (new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31), new[] { older }),
        })
        {
            var dated = Doc(new AllOfNode(
            [
                Titled(token),
                new ConditionNode(PhotographFilterFields.DatedAt, FilterOp.GreaterOrEqual, [Midnight(from)]),
                new ConditionNode(PhotographFilterFields.DatedAt, FilterOp.LessThan, [Midnight(to.AddDays(1))]),
            ]));
            var byRange = await Ids($"search={token}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
            byRange.ShouldBe(expected, ignoreOrder: true);
            (await Ids($"filter={dated}")).ShouldBe(byRange, ignoreOrder: true);
        }

        people.Dispose();
    }

    [Fact]
    public async Task The_geofile_list_reads_a_filter_and_refuses_one_it_cannot_read()
    {
        var people = await PeopleAsync("gfl");
        Guid kept;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            kept = GeofileWorldFixture.Add(db, $"Keep {tag}", people.OwnerId, Visibility.Public).Id;
            GeofileWorldFixture.Add(db, $"Drop {tag}", people.OwnerId, Visibility.Public);
            // Matches the filter, and is still not the outsider's to see.
            GeofileWorldFixture.Add(db, $"Keep private {tag}", people.OwnerId, Visibility.Private);
            await db.SaveChangesAsync();
        }

        var keep = FilterToken.Encode(About(GeofileFilterWorld.Key, Named($"Keep")));
        var page = await people.Outsider.GetFromJsonAsync<PagedResult<ListedGeofile>>(
            $"/api/v1/geofiles?search={tag}&filter={keep}", Wire);
        page.ShouldNotBeNull();
        page.Items.Select(g => g.Id).ShouldBe([kept]);
        page.TotalItems.ShouldBe(1);

        await RefusalsAsync(people, "/api/v1/geofiles", GeofileFilterWorld.Key);
        people.Dispose();
    }

    [Fact]
    public async Task The_georeferenced_map_list_reads_a_filter_and_never_counts_a_map_it_withholds()
    {
        var people = await PeopleAsync("gml");
        Guid kept, protectedCaveId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var protectedCave = await ConformanceSeeds.CaveAsync(db, people.OwnerId, Visibility.Public, locationProtected: true);
            protectedCaveId = protectedCave.Id;
            kept = Map(db, people.OwnerId, "keep", Visibility.Public).Id;
            Map(db, people.OwnerId, "drop", Visibility.Public);
            var guarded = Map(db, people.OwnerId, "keep guarded", Visibility.Public);
            guarded.CaveFeatureId = protectedCave.Id;
            guarded.MapKind = MapKind.CaveMap;
            await db.SaveChangesAsync();
        }

        var keep = FilterToken.Encode(About(
            GeoreferencedMapFilterWorld.Key,
            new AllOfNode([Named(tag), Named("keep")])));

        var owners = await people.Owner.GetFromJsonAsync<PagedResult<GeoreferencedMapDto>>(
            $"/api/v1/georeferenced-maps?filter={keep}", Wire);
        owners.ShouldNotBeNull();
        owners.Items.Count.ShouldBe(2);
        owners.TotalItems.ShouldBe(2);

        var outsiders = await people.Outsider.GetFromJsonAsync<PagedResult<GeoreferencedMapDto>>(
            $"/api/v1/georeferenced-maps?filter={keep}", Wire);
        outsiders.ShouldNotBeNull();
        outsiders.Items.Select(m => m.Id).ShouldBe([kept]);
        // The count is taken after the cave rule, so it does not announce the map left out.
        outsiders.TotalItems.ShouldBe(1);

        // The same with no filter at all: asking for the protected cave's maps answers an empty page
        // and a count of none, as a cave with no maps would.
        var byCave = await people.Outsider.GetFromJsonAsync<PagedResult<GeoreferencedMapDto>>(
            $"/api/v1/georeferenced-maps?caveFeatureId={protectedCaveId}", Wire);
        byCave.ShouldNotBeNull();
        byCave.Items.ShouldBeEmpty();
        byCave.TotalItems.ShouldBe(0);
        var ownersByCave = await people.Owner.GetFromJsonAsync<PagedResult<GeoreferencedMapDto>>(
            $"/api/v1/georeferenced-maps?caveFeatureId={protectedCaveId}", Wire);
        ownersByCave.ShouldNotBeNull();
        ownersByCave.TotalItems.ShouldBe(1);

        await RefusalsAsync(people, "/api/v1/georeferenced-maps", GeoreferencedMapFilterWorld.Key);
        people.Dispose();
    }

    /// <summary>
    /// What every list that reads a filter refuses, and that it is never answered unfiltered instead.
    /// </summary>
    private async Task RefusalsAsync(People people, string path, string world)
    {
        var readable = FilterToken.Encode(About(world, Named("x")));
        var aboutPhotographs = FilterToken.Encode(About(PhotographFilterWorld.Key, Titled("x")));

        var cases = new (string Query, HttpStatusCode Status, string Code)[]
        {
            ("filter=eyJ2ZXJzaW9uIjox", HttpStatusCode.BadRequest, SurfaceFilters.MalformedCode),
            ($"filter={aboutPhotographs}", HttpStatusCode.BadRequest, SurfaceFilters.WrongWorldCode),
            ($"filter={readable}&savedFilterId={Guid.NewGuid()}", HttpStatusCode.BadRequest, SurfaceFilters.AmbiguousCode),
            ($"savedFilterId={Guid.NewGuid()}", HttpStatusCode.NotFound, SurfaceFilters.SavedNotFoundCode),
        };

        foreach (var (query, status, code) in cases)
        {
            var response = await people.Owner.GetAsync($"{path}?{query}");
            response.StatusCode.ShouldBe(status, query);
            (await response.Content.ReadAsStringAsync()).ShouldContain(code, customMessage: query);
        }

        // A saved filter the reader may not read is the same refusal as one that is not there.
        var saved = await people.Owner.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Private {world} {tag}",
            document = About(world, Named(tag)),
            visibility = "private",
        }, Wire);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var id = (await saved.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        (await people.Owner.GetAsync($"{path}?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await people.Outsider.GetAsync($"{path}?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The positive twin, and the anonymous caller.
        (await people.Owner.GetAsync($"{path}?filter={readable}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{path}?filter={readable}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- the lists ----------

    /// <summary>
    /// Every model the reader is shown across the given caves' model lists. A cave the reader may
    /// not read answers "not found", which for this comparison is a cave with no models to show.
    /// </summary>
    private async Task<List<Guid>> ListedModelsAsync(HttpClient client, IEnumerable<Feature> caves)
    {
        var ids = new List<Guid>();
        foreach (var cave in caves)
        {
            var response = await client.GetAsync($"/api/v1/caves/{cave.Id}/survey-models");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var models = await response.Content.ReadFromJsonAsync<List<ListedModel>>(Wire);
            models.ShouldNotBeNull();
            ids.AddRange(models.Where(m => m.Name.Contains(tag, StringComparison.Ordinal)).Select(m => m.Id));
        }

        return ids;
    }

    private static async Task<List<ListedPhoto>> ListedPhotosAsync(HttpClient client, string token)
    {
        var page = await client.GetFromJsonAsync<PagedResult<ListedPhoto>>(
            $"/api/v1/photos?search={token}&pageSize={Paging.MaxPageSize}", Wire);
        page.ShouldNotBeNull();
        return [.. page.Items];
    }

    private async Task<List<Guid>> ListedGeofilesAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<PagedResult<ListedGeofile>>(
            $"/api/v1/geofiles?search={tag}&pageSize={Paging.MaxPageSize}", Wire);
        page.ShouldNotBeNull();
        return [.. page.Items.Select(g => g.Id)];
    }

    // Only the members these comparisons read; the rest of each answer is not their business.
    private sealed record ListedModel(Guid Id, string Name);

    private static async Task<PagedResult<ListedPhoto>> GalleryPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/photos?{query}&pageSize={Paging.MaxPageSize}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await response.Content.ReadFromJsonAsync<PagedResult<ListedPhoto>>(Wire);
        page.ShouldNotBeNull();
        return page;
    }

    private sealed record ListedPhoto(Guid DocumentId, System.Text.Json.JsonElement? Position);

    private sealed record ListedGeofile(Guid Id, string Name);

    private async Task<List<Guid>> ListedMapsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<PagedResult<GeoreferencedMapDto>>(
            $"/api/v1/georeferenced-maps?pageSize={Paging.MaxPageSize}", Wire);
        page.ShouldNotBeNull();
        return [.. page.Items.Where(m => m.Name.Contains(tag, StringComparison.Ordinal)).Select(m => m.Id)];
    }

    private async Task<List<Guid>> ListedCabinetsAsync(HttpClient client)
    {
        var tree = await client.GetFromJsonAsync<List<CabinetDto>>("/api/v1/cabinets", Wire);
        tree.ShouldNotBeNull();
        return [.. tree.Where(c => c.Name.Contains(tag, StringComparison.Ordinal)).Select(c => c.Id)];
    }

    // ---------- the world ----------

    private static async Task<(List<Guid> Ids, int? Total)> WorldIdsAsync(HttpClient client, string world, FilterNode where)
    {
        var result = await QueryAsync(client, world, where);
        return ([.. result.Hits.Select(h => h.Id)], result.Total);
    }

    private static async Task<FilterWorldResultDto> QueryAsync(HttpClient client, string world, FilterNode where)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(About(world, where), PageSize: 100));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<FilterQueryResponse>(Wire);
        body.ShouldNotBeNull();
        var result = body.Worlds.Single();
        result.World.ShouldBe(world);
        result.Hits.ShouldAllBe(h => h.World == world);
        return result;
    }

    private static FilterDocument About(string world, FilterNode? where) =>
        new() { Scope = [new WorldScope(world, where)] };

    private static ConditionNode Named(string text) =>
        new(CommonFilterFields.Name, FilterOp.Contains, [new TextValue(text)]);

    private static ConditionNode Titled(string text) =>
        new(PhotographFilterFields.Title, FilterOp.Contains, [new TextValue(text)]);

    // ---------- the people ----------

    private GeoreferencedMap Map(SilexGisDbContext db, Guid ownerId, string what, Visibility visibility)
    {
        var file = ConformanceSeeds.File(db, ownerId, FileKind.Raster, "tif");
        var map = new GeoreferencedMap
        {
            Name = $"Map {what} {tag}",
            FileId = file.Id,
            OwnerUserId = ownerId,
            Visibility = visibility,
        };
        db.GeoreferencedMaps.Add(map);
        return map;
    }

    /// <summary>
    /// Three Viewers: an owner and a clubmate in one club, and an outsider in none.
    /// </summary>
    /// <remarks>
    /// Viewers on purpose. The seeded Editors group reads past visibility everywhere, so an editor
    /// would see every row whatever the world did, and the comparison would prove nothing.
    /// </remarks>
    private async Task<People> PeopleAsync(string prefix)
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"{prefix}-own-{tag}@t.local");
        var mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"{prefix}-mate-{tag}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"{prefix}-out-{tag}@t.local");

        Guid clubId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Parity Club {prefix} {tag}", Slug = $"parity-{prefix}-{tag}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            await RosterHelper.AddMemberAsync(db, club.Id, ownerId);
            await RosterHelper.AddMemberAsync(db, club.Id, mateId);
            clubId = club.Id;
        }

        return new People(
            ownerId,
            clubId,
            await AuthHelper.BearerClientAsync(factory, $"{prefix}-own-{tag}@t.local"),
            await AuthHelper.BearerClientAsync(factory, $"{prefix}-mate-{tag}@t.local"),
            await AuthHelper.BearerClientAsync(factory, $"{prefix}-out-{tag}@t.local"));
    }

    private sealed record People(Guid OwnerId, Guid ClubId, HttpClient Owner, HttpClient Clubmate, HttpClient Outsider)
        : IDisposable
    {
        public void Dispose()
        {
            Owner.Dispose();
            Clubmate.Dispose();
            Outsider.Dispose();
        }
    }

    private static System.Text.Json.JsonSerializerOptions BuildWire()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.CamelCase));
        return options;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
