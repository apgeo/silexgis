// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// How much of a test's surroundings is its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precise</b>, the default: every test builds the application for itself. Nothing held in
/// memory by one test — a cache, a limiter's window, a message it sent — can be seen by the next,
/// because the next has a different application.
/// </para>
/// <para>
/// <b>Fast</b>, asked for with <c>SILEXGIS_TEST_MODE=fast</c>: the tests of a class take turns on
/// one running application where they ask for the same one, the way they already take turns on one
/// database. Building the application is most of what a test costs, so this is several times
/// quicker, and it is less sure: the application is not newly started for each test, and what it
/// keeps in memory outside its cache now outlives one. A green run in this mode says the suite
/// passed with that sharing; only a precise run says each test passed alone.
/// </para>
/// </remarks>
internal static class TestMode
{
    private const string Variable = "SILEXGIS_TEST_MODE";

    /// <summary>True when the tests of a class share a running application.</summary>
    public static bool Fast { get; } = Read();

    private static bool Read()
    {
        var value = Environment.GetEnvironmentVariable(Variable)?.Trim();
        if (string.IsNullOrEmpty(value) || string.Equals(value, "precise", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(value, "fast", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A mode nobody defined must not quietly become one of the two: a run asked to be fast
        // that came out precise is only slow, but the other way round is a verdict that claims
        // more than it checked.
        throw new InvalidOperationException(
            $"{Variable} is \"{value}\"; it is either \"fast\" or \"precise\" (the default when unset).");
    }
}

/// <summary>
/// The running applications that tests of a class take turns on, in fast mode.
/// </summary>
/// <remarks>
/// <para>
/// One application per database and per set of settings: two factories asking for the same
/// database with the same settings are asking for the same application, and the second is handed
/// the first one's instead of building its own. Only one factory holds an application at a time.
/// A second factory that asks while the first still holds it is a test deliberately standing two
/// applications on one database — a second node, a restart — and gets one of its own.
/// </para>
/// <para>
/// A class whose every test asks for something no other test asked for — a setting with a value
/// made up per test — would otherwise leave an application running for each of them until the
/// class is over. So only a few are kept waiting per database; the one longest unused is stopped
/// when another is added.
/// </para>
/// </remarks>
internal static class SharedHosts
{
    /// <summary>
    /// How many applications may wait unused on one database. Two are what a class whose
    /// constructor stands up a pair of differently configured applications needs between its
    /// tests; the rest is slack for a class that alternates between a few.
    /// </summary>
    private const int KeptWaiting = 4;

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly ConcurrentDictionary<string, int> BuiltFor = new();
    private static readonly ConcurrentDictionary<string, Tally> ByMaker = new();
    private static int built;
    private static int borrowed;
    private static int stoppedEarly;
    private static int numbered;

    /// <summary>Applications built so far in this process, in either mode.</summary>
    public static int Built => Volatile.Read(ref built);

    /// <summary>Times a factory was handed an application that was already running.</summary>
    public static int Borrowed => Volatile.Read(ref borrowed);

    /// <summary>Applications stopped before their class was over, to make room for another.</summary>
    public static int StoppedEarly => Volatile.Read(ref stoppedEarly);

    /// <summary>Why applications were built, and how many for each reason.</summary>
    public static IReadOnlyDictionary<string, int> BuiltBecause => BuiltFor;

    /// <summary>
    /// The same two counts and the reasons, for each class that made a factory — known in fast
    /// mode only, where a factory looks at who is making it. It is what says which classes still
    /// build an application per test, and why.
    /// </summary>
    public static IReadOnlyDictionary<string, Tally> ByClass => ByMaker;

    /// <summary>Counts one application built, under the reason it could not be a borrowed one.</summary>
    public static void CountBuilt(string because, string? maker = null)
    {
        Interlocked.Increment(ref built);
        BuiltFor.AddOrUpdate(because, 1, (_, count) => count + 1);
        if (maker is not null)
        {
            ByMaker.GetOrAdd(maker, _ => new Tally()).CountBuilt(because);
        }
    }

    /// <summary>
    /// Takes the application for this database and these settings, or says there is none to take.
    /// </summary>
    /// <returns>
    /// The entry and whether it already runs — a new entry is handed out not yet running, for its
    /// taker to build and <see cref="Entry.Started"/>. Null when another factory holds it.
    /// </returns>
    public static (Entry Entry, bool Running)? Take(string connectionString, string settingsKey)
    {
        var key = connectionString + "\n" + settingsKey;
        while (true)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                lock (existing)
                {
                    if (existing.Gone)
                    {
                        continue; // Removed between the lookup and the lock; look again.
                    }

                    if (existing.Held)
                    {
                        return null;
                    }

                    existing.Held = true;
                    return (existing, true);
                }
            }

            var fresh = new Entry(connectionString, key, Interlocked.Increment(ref numbered)) { Held = true };
            if (Entries.TryAdd(key, fresh))
            {
                StopThoseWaitingTooLong(connectionString);
                return (fresh, false);
            }
        }
    }

    /// <summary>Counts one test handed an application that was already running.</summary>
    public static void CountBorrowed(string? maker = null)
    {
        Interlocked.Increment(ref borrowed);
        if (maker is not null)
        {
            ByMaker.GetOrAdd(maker, _ => new Tally()).CountBorrowed();
        }
    }

    /// <summary>Gives an application back for the next test of the class.</summary>
    public static void Release(Entry entry)
    {
        lock (entry)
        {
            entry.Held = false;
            entry.LastUsed = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Stops the applications on a database that have waited unused the longest, beyond the few
    /// that are kept.
    /// </summary>
    private static void StopThoseWaitingTooLong(string connectionString)
    {
        var waiting = Entries.Values
            .Where(e => e.ConnectionString == connectionString && !e.Held && !e.Gone)
            .OrderByDescending(e => e.LastUsed)
            .Skip(KeptWaiting)
            .ToList();
        foreach (var entry in waiting)
        {
            IHost? host;
            lock (entry)
            {
                if (entry.Held || entry.Gone)
                {
                    continue; // Taken, or stopped with its class, since the list was made.
                }

                entry.Gone = true;
                host = entry.Host;
                entry.Host = null;
            }

            Entries.TryRemove(entry.Key, out _);
            if (host is not null)
            {
                // Waited for, as a factory's own disposal is: a constructor is where this runs.
                host.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                host.Dispose();
                Interlocked.Increment(ref stoppedEarly);
            }

            entry.RemoveStores();
        }
    }

    /// <summary>Forgets an entry whose application never came up.</summary>
    public static void Abandon(Entry entry)
    {
        lock (entry)
        {
            entry.Gone = true;
        }

        Entries.TryRemove(entry.Key, out _);
        entry.RemoveStores();
    }

    /// <summary>Stops every application running against a database that is about to be dropped.</summary>
    public static async Task StopAllOnAsync(string connectionString)
    {
        foreach (var entry in Entries.Values.Where(e => e.ConnectionString == connectionString).ToList())
        {
            IHost? host;
            lock (entry)
            {
                entry.Gone = true;
                host = entry.Host;
                entry.Host = null;
            }

            Entries.TryRemove(entry.Key, out _);
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }

            entry.RemoveStores();
        }
    }

    /// <summary>How many applications one class built and borrowed, and why it built them.</summary>
    internal sealed class Tally
    {
        private readonly ConcurrentDictionary<string, int> because = new();
        private int built;
        private int borrowed;

        public int Built => Volatile.Read(ref built);

        public int Borrowed => Volatile.Read(ref borrowed);

        public IReadOnlyDictionary<string, int> Because => because;

        public void CountBuilt(string reason)
        {
            Interlocked.Increment(ref built);
            because.AddOrUpdate(reason, 1, (_, count) => count + 1);
        }

        public void CountBorrowed() => Interlocked.Increment(ref borrowed);
    }

    internal sealed class Entry(string connectionString, string key, int number)
    {
        private readonly ConcurrentBag<string> links = [];

        public string ConnectionString { get; } = connectionString;

        public string Key { get; } = key;

        /// <summary>Tells this application's directories from another's on the same database.</summary>
        public int Number { get; } = number;

        public bool Held { get; set; }

        public bool Gone { get; set; }

        /// <summary>When it was last given back; the longest unused is the first to be stopped.</summary>
        public long LastUsed { get; set; }

        /// <summary>The running application, once its first taker has built it.</summary>
        public IHost? Host { get; set; }

        /// <summary>What the application sends its messages into; emptied for each taker.</summary>
        public MessageCapture Messages { get; } = new();

        /// <summary>
        /// The class's directories that stand in for the ones each test names for itself, in the
        /// order the settings name them. See <see cref="PerTestStores"/>.
        /// </summary>
        public IReadOnlyList<string> Stores { get; set; } = [];

        public void Started(IHost host)
        {
            lock (this)
            {
                Host = host;
            }
        }

        /// <summary>Remembers a link a test was given into one of the stores, to remove with the class.</summary>
        public void Linked(string link) => links.Add(link);

        /// <summary>Removes the stores and whatever links into them the tests left behind.</summary>
        public void RemoveStores()
        {
            foreach (var link in links)
            {
                PerTestStores.RemoveLink(link);
            }

            foreach (var store in Stores)
            {
                try
                {
                    Directory.Delete(store, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left for `dotnet clean`, like everything else under the build output.
                }
            }
        }
    }
}

/// <summary>
/// The directories a test names for itself in a factory's settings, and what stands in for them
/// when the application is shared.
/// </summary>
/// <remarks>
/// <para>
/// Many classes give every test a file store of its own — a new directory under the scratch root,
/// named in <c>Files:Root</c> and its neighbours — so no two factories of such a class ever ask
/// for the same settings, and none could share. What they are asking for is a store that is
/// theirs, not that particular path. So in fast mode a directory under the scratch root that does
/// not exist yet is read as "a store of this test's own": the settings are compared with each such
/// directory replaced by its position, the application is built on directories belonging to the
/// class, and every test's own path is made a link to the class's directory.
/// </para>
/// <para>
/// The link is what keeps this honest. A test that looks under the path it named — for the file
/// an upload should have written, or should not have — is looking at the store the application
/// really writes to, not at an empty directory that would agree with anything. And a test that
/// removes its directory when it is done removes a link, not the store.
/// </para>
/// <para>
/// A directory that already exists is somebody's real directory and is left exactly as named; a
/// factory naming one shares only with a factory naming the same one. One that is already such a
/// link is the directory of a factory made a moment ago, so this is a second application meant to
/// stand on the same store, and it is built for itself. Where links cannot be made the factory
/// builds its own application too, as in precise mode.
/// </para>
/// <para>
/// The limit of the reading: it takes a directory nobody has made yet to be one test's. A
/// directory that several classes name and none makes before building its application would be
/// linked to the first class's store and go with it.
/// </para>
/// </remarks>
internal static class PerTestStores
{
    /// <summary>
    /// Reads the per-test directories out of a factory's settings.
    /// </summary>
    public static Named In(IDictionary<string, string?>? settings)
    {
        if (settings is null)
        {
            return new Named(string.Empty, [], AlreadyStandIns: false, _ => []);
        }

        var root = Path.TrimEndingDirectorySeparator(TestScratch.Root) + Path.DirectorySeparatorChar;
        var directories = new List<string>();
        var alreadyStandIns = false;
        var parts = new List<(string Key, string? Value, int Store, string Tail)>();
        foreach (var (key, value) in settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var store = -1;
            var rest = string.Empty;
            if (value is not null && value.StartsWith(root, StringComparison.Ordinal))
            {
                var below = value[root.Length..];
                var cut = below.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                var name = cut < 0 ? below : below[..cut];
                var directory = root + name;
                if (name.Length > 0 && new DirectoryInfo(directory).LinkTarget is not null)
                {
                    alreadyStandIns = true;
                }
                else if (name.Length > 0 && !Directory.Exists(directory) && !File.Exists(directory))
                {
                    store = directories.IndexOf(directory);
                    if (store < 0)
                    {
                        directories.Add(directory);
                        store = directories.Count - 1;
                    }

                    rest = cut < 0 ? string.Empty : below[cut..];
                }
            }

            parts.Add((key, value, store, rest));
        }

        var asKey = string.Join(
            "\n",
            parts.Select(part => part.Store < 0
                ? $"{part.Key}={part.Value ?? "\0"}"
                : $"{part.Key}=\u0001{part.Store}{part.Tail}"));
        return new Named(
            asKey,
            directories,
            alreadyStandIns,
            stores => parts.ToDictionary(
                part => part.Key,
                part => part.Store < 0 ? part.Value : stores[part.Store] + part.Tail));
    }

    /// <summary>What a factory's settings say about directories.</summary>
    /// <param name="Key">The settings, with each per-test directory replaced by its position.</param>
    /// <param name="Directories">The per-test directories, in the order of their positions.</param>
    /// <param name="AlreadyStandIns">True when the settings name a directory that is already a link.</param>
    /// <param name="Over">Writes the settings again over other directories.</param>
    internal sealed record Named(
        string Key,
        IReadOnlyList<string> Directories,
        bool AlreadyStandIns,
        Func<IReadOnlyList<string>, Dictionary<string, string?>> Over);

    /// <summary>
    /// Makes each of a test's own directories a link to the class's, or reports that it could not.
    /// </summary>
    public static bool TryLink(IReadOnlyList<string> own, IReadOnlyList<string> stores, Action<string> linked)
    {
        var made = new List<string>();
        try
        {
            for (var i = 0; i < own.Count; i++)
            {
                Directory.CreateDirectory(stores[i]);
                Directory.CreateSymbolicLink(own[i], stores[i]);
                made.Add(own[i]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            foreach (var link in made)
            {
                RemoveLink(link);
            }

            return false;
        }

        foreach (var link in made)
        {
            linked(link);
        }

        return true;
    }

    /// <summary>Removes a link if it is still one; a directory somebody put in its place is theirs.</summary>
    public static void RemoveLink(string path)
    {
        try
        {
            // Asked of the link itself, so that one whose store has already gone is still found.
            var entry = new DirectoryInfo(path);
            if (entry.LinkTarget is not null)
            {
                entry.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A link nobody could remove points at a directory about to go; it is litter, not a fault.
        }
    }
}
