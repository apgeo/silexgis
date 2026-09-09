// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>
/// One subject's readings of the metrics a clustering was asked for, in the order the columns were
/// named. A reading that was never recorded is null.
/// </summary>
/// <param name="SubjectId">Identity of the thing measured. It is carried through so a caller can
/// join the grouping back to whatever it clustered, and it is also the tie-break that makes the
/// answer independent of the order the rows arrived in.</param>
/// <param name="Values">One entry per named column, same order, same length. A null, a NaN and an
/// infinity all count as "not recorded": a measurement that cannot be a distance is not a
/// measurement, and treating it as zero would place the subject somewhere it was never observed.
/// </param>
public sealed record MetricVector(Guid SubjectId, IReadOnlyList<double?> Values);

/// <summary>
/// How much of the population recorded one metric, and what excluding it costs.
/// </summary>
/// <param name="Column">The metric, named as the caller named it.</param>
/// <param name="Recorded">Subjects offered that recorded a value for this metric.</param>
/// <param name="Missing">Subjects offered that did not.</param>
/// <param name="SoleReason">Subjects that were excluded from the clustering and would have been
/// eligible if this one metric had not been asked for — that is, they recorded every other selected
/// metric and not this one. It is the number that answers "what does this column cost me", which is
/// the question a reader has to be able to ask before the grouping means anything: a metric only a
/// tenth of the register records turns a statement about the register into a statement about its
/// best-measured tenth, and nothing downstream can recover that difference.</param>
public sealed record MetricColumnCoverage(string Column, int Recorded, int Missing, int SoleReason);

/// <summary>
/// The account of who was clustered and who was not.
/// </summary>
/// <param name="Considered">Subjects offered to the clustering.</param>
/// <param name="Eligible">Subjects that recorded every selected metric and were therefore
/// clustered.</param>
/// <param name="Excluded">The rest. Published beside the eligible count rather than derived from
/// it, because a figure a reader has to subtract is a figure a reader skips.</param>
/// <param name="Columns">Per-metric coverage, in the order the metrics were named.</param>
public sealed record MetricPopulation(
    int Considered,
    int Eligible,
    int Excluded,
    IReadOnlyList<MetricColumnCoverage> Columns);

/// <summary>
/// What one metric was divided by before any distance was taken.
/// </summary>
/// <param name="Column">The metric.</param>
/// <param name="Mean">Mean over the eligible subjects, subtracted from every reading.</param>
/// <param name="StandardDeviation">Spread over the eligible subjects, taken with the population
/// divisor, which every reading is then divided by. <b>Zero when every eligible subject recorded
/// the same value</b>, and the column then contributes nothing to any distance — it is reported
/// rather than dropped so a reader can see that a metric they chose did no work.</param>
public sealed record MetricScaling(string Column, double Mean, double StandardDeviation);

/// <summary>One group the clustering produced.</summary>
/// <param name="Index">Its number. <b>A label, not a rank and not a score.</b> Group 2 is not
/// larger, better or more interesting than group 1; the numbering falls out of the order the
/// starting positions were chosen in and carries no meaning a reader may lean on.</param>
/// <param name="Count">Subjects in it.</param>
/// <param name="Centre">Its middle, in the units the metrics arrived in, so it can be read.</param>
/// <param name="ScaledCentre">Its middle in the standardised space the distances were actually
/// taken in. Published because every spread figure below is measured there and a reader comparing
/// the two would otherwise be comparing different quantities.</param>
/// <param name="MeanDistanceToCentre">Mean distance from its members to its middle, standardised.
/// This is the group's own width.</param>
public sealed record MetricCluster(
    int Index,
    int Count,
    IReadOnlyList<double> Centre,
    IReadOnlyList<double> ScaledCentre,
    double MeanDistanceToCentre);

/// <summary>Which group one subject fell in.</summary>
/// <param name="SubjectId">The subject.</param>
/// <param name="Cluster">The group's <see cref="MetricCluster.Index"/>.</param>
/// <param name="DistanceToCentre">Its distance to that group's middle, standardised.</param>
public sealed record MetricAssignment(Guid SubjectId, int Cluster, double DistanceToCentre);

/// <summary>
/// How far apart the groups stand compared with how wide they are — the figure that lets a grouping
/// say it found nothing.
/// </summary>
/// <param name="MeanWithinDistance">Mean distance from a subject to its own group's middle, over
/// every clustered subject.</param>
/// <param name="MeanBetweenDistance">Mean distance between two group middles, over every pair of
/// groups.</param>
/// <param name="Ratio">The first divided by the second. <b>Small means the groups are real; near or
/// above one means they are not.</b> k-means returns exactly the number of groups it was asked for
/// whatever it is given, including noise, so the existence of a grouping is evidence of nothing and
/// this ratio is the only thing in the answer that can contradict it.
/// <para>
/// <b>Null when the middles do not separate at all</b>, which happens when every eligible subject
/// recorded identical values for every selected metric: each column then has no spread, every
/// standardised point is the origin, and all the middles land on top of each other. That is the
/// strongest possible "these groups are not real", so it is stated as a reading rather than
/// computed — the division would give a non-finite double, which is not a number that can be put
/// on a wire and would fail the serializer rather than describe the population.
/// </para></param>
public sealed record MetricSeparation(
    double MeanWithinDistance,
    double MeanBetweenDistance,
    double? Ratio);

/// <summary>
/// A grouping of subjects by their metrics, together with everything a reader needs in order to
/// know what it is a grouping of.
/// </summary>
/// <param name="Columns">The metrics the distances were taken over.</param>
/// <param name="RequestedClusterCount">Groups asked for.</param>
/// <param name="Population">Who was clustered and who was not.</param>
/// <param name="Scaling">What each metric was standardised by.</param>
/// <param name="Clusters">The groups. <b>Empty when the population was too small to group</b>, with
/// the population account still filled in — the answer to "nothing could be clustered" is the
/// account of why, not an error.</param>
/// <param name="Assignments">One entry per clustered subject, ordered by subject.</param>
/// <param name="Separation">Null exactly when <paramref name="Clusters"/> is empty.</param>
/// <param name="Iterations">Passes of the refinement that ran.</param>
/// <param name="Converged">False when the refinement was still moving subjects when it hit its
/// cap, which makes the grouping arbitrary in its details rather than wrong.</param>
public sealed record MetricClusterModel(
    IReadOnlyList<string> Columns,
    int RequestedClusterCount,
    MetricPopulation Population,
    IReadOnlyList<MetricScaling> Scaling,
    IReadOnlyList<MetricCluster> Clusters,
    IReadOnlyList<MetricAssignment> Assignments,
    MetricSeparation? Separation,
    int Iterations,
    bool Converged);

/// <summary>
/// k-means over per-subject metric vectors: which subjects resemble each other, computed as
/// distances and means and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The metrics are standardised before any distance is taken, and this is arithmetic rather than
/// taste.</b> A length in metres and a ratio between nought and one are not comparable distances:
/// unstandardised, the column with the widest raw spread decides every group on its own and the
/// others are decoration. Each column is therefore centred on its own mean and divided by its own
/// spread over the eligible subjects, and what was divided by is published. The observable
/// consequence, which is what makes it testable, is that re-expressing a metric in different units —
/// metres for kilometres, a fraction for a percentage — does not change the grouping.
/// </para>
/// <para>
/// <b>A subject missing any selected metric is excluded, never imputed.</b> Filling a gap with a
/// mean would place a cave in the middle of a population it was never measured against, and the
/// group it then joined would be an artefact of the filling. The exclusion is the honest answer, and
/// because it is silently devastating — most caves in a register have no survey, so a clustering
/// over survey-derived metrics can describe the best-measured tenth while appearing to describe the
/// whole — the account of who was excluded and for want of which metric is part of the answer rather
/// than a diagnostic beside it.
/// </para>
/// <para>
/// <b>The starting positions are chosen, not drawn.</b> There is no random seed anywhere here: the
/// first middle is the eligible subject furthest from the overall middle, and each next one is the
/// subject furthest from every middle already chosen, ties broken by subject identity. Subjects are
/// sorted by identity first, so the same measurements give the same grouping whatever order the
/// rows arrived in and however many times it is asked for. A grouping that moved between two
/// identical requests would be read as the data having changed.
/// </para>
/// <para>
/// <b>A group label says nothing on its own.</b> The algorithm returns the number of groups it was
/// asked for on any input whatever, so "there are three groups" is a restatement of the request.
/// <see cref="MetricSeparation.Ratio"/> is what distinguishes a grouping that found structure from
/// one that partitioned noise, and it is computed always rather than on request.
/// </para>
/// </remarks>
public static class MetricClustering
{
    /// <summary>Fewest groups that can be asked for. One group is the population.</summary>
    public const int MinimumClusterCount = 2;

    /// <summary>
    /// Most groups that can be asked for. The cap is not arithmetic: a grouping is read by eye, and
    /// past a handful of groups the reader is being handed a list of subjects rather than a
    /// description of a population — which, over a register of caves, is also the point at which the
    /// grouping starts to single individuals out.
    /// </summary>
    public const int MaximumClusterCount = 6;

    /// <summary>
    /// Fewest eligible subjects a grouping is offered over, and the same floor a distribution fit
    /// uses for the same reason: below it the groups exist arithmetically and each one restates the
    /// two or three subjects in it instead of describing anything.
    /// </summary>
    public const int MinimumEligibleCount = 8;

    /// <summary>
    /// Fewest subjects a group may hold before figures about that group stop being figures about a
    /// population. Nothing here suppresses a thinner group — k-means cannot be told to keep its
    /// groups above a size, and a thin group is itself a finding — but a surface that publishes
    /// per-group measurements has to consult this before it does, exactly as a histogram consults
    /// its own floor before publishing a sector.
    /// </summary>
    public const int MinimumPublishableClusterSize = 3;

    /// <summary>
    /// Passes of the refinement before it stops regardless. Reached only by a population with many
    /// near-ties, and reported rather than hidden.
    /// </summary>
    public const int MaximumIterations = 100;

    /// <summary>
    /// Groups <paramref name="subjects"/> by the metrics named in <paramref name="columns"/>.
    /// </summary>
    /// <param name="columns">The metrics, in the order every <see cref="MetricVector.Values"/> uses.
    /// At least one, and no duplicates — a metric named twice would be counted twice in every
    /// distance, which is a weighting nobody asked for.</param>
    /// <param name="subjects">The subjects. Order is irrelevant; duplicate identities are not
    /// permitted.</param>
    /// <param name="clusterCount">Groups to produce, between <see cref="MinimumClusterCount"/> and
    /// <see cref="MaximumClusterCount"/>.</param>
    /// <returns>The grouping, or — when fewer than <see cref="MinimumEligibleCount"/> subjects
    /// recorded every selected metric, or fewer than <paramref name="clusterCount"/> did — a model
    /// with no groups and a full population account.</returns>
    public static MetricClusterModel Compute(
        IReadOnlyList<string> columns,
        IReadOnlyList<MetricVector> subjects,
        int clusterCount)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns.Count, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(clusterCount, MinimumClusterCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clusterCount, MaximumClusterCount);

        var names = columns.ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException("A metric may be named only once.", nameof(columns));
        }

        var width = names.Length;
        var ordered = subjects.OrderBy(s => s.SubjectId).ToArray();
        if (ordered.Select(s => s.SubjectId).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("A subject may appear only once.", nameof(subjects));
        }

        foreach (var subject in ordered)
        {
            if (subject.Values.Count != width)
            {
                throw new ArgumentException(
                    "Every subject must carry one reading per named metric.", nameof(subjects));
            }
        }

        // Which readings a subject actually has, decided once and reused by the coverage account,
        // the eligibility test and the matrix build — three answers from one rule.
        var present = new bool[ordered.Length][];
        for (var i = 0; i < ordered.Length; i++)
        {
            present[i] = new bool[width];
            for (var c = 0; c < width; c++)
            {
                var value = ordered[i].Values[c];
                present[i][c] = value.HasValue && double.IsFinite(value.Value);
            }
        }

        var eligibleRows = new List<int>(ordered.Length);
        for (var i = 0; i < ordered.Length; i++)
        {
            var complete = true;
            for (var c = 0; c < width && complete; c++)
            {
                complete = present[i][c];
            }

            if (complete)
            {
                eligibleRows.Add(i);
            }
        }

        var coverage = new MetricColumnCoverage[width];
        for (var c = 0; c < width; c++)
        {
            var recorded = 0;
            var soleReason = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                if (present[i][c])
                {
                    recorded++;
                    continue;
                }

                // Excluded, and this column is why: every other selected metric is there.
                var otherwiseComplete = true;
                for (var other = 0; other < width && otherwiseComplete; other++)
                {
                    otherwiseComplete = other == c || present[i][other];
                }

                if (otherwiseComplete)
                {
                    soleReason++;
                }
            }

            coverage[c] = new MetricColumnCoverage(
                names[c], recorded, ordered.Length - recorded, soleReason);
        }

        var population = new MetricPopulation(
            ordered.Length, eligibleRows.Count, ordered.Length - eligibleRows.Count, coverage);

        var raw = new double[eligibleRows.Count][];
        for (var r = 0; r < eligibleRows.Count; r++)
        {
            raw[r] = new double[width];
            var values = ordered[eligibleRows[r]].Values;
            for (var c = 0; c < width; c++)
            {
                raw[r][c] = values[c]!.Value;
            }
        }

        var scaling = Standardise(names, raw, out var scaled);

        if (eligibleRows.Count < MinimumEligibleCount || eligibleRows.Count < clusterCount)
        {
            return new MetricClusterModel(
                names, clusterCount, population, scaling, [], [], null, 0, true);
        }

        var (labels, centres, iterations, converged) = Fit(scaled, clusterCount);

        var assignments = new MetricAssignment[scaled.Length];
        var counts = new int[clusterCount];
        var distanceSums = new double[clusterCount];
        for (var r = 0; r < scaled.Length; r++)
        {
            var label = labels[r];
            var distance = Distance(scaled[r], centres[label]);
            counts[label]++;
            distanceSums[label] += distance;
            assignments[r] = new MetricAssignment(
                ordered[eligibleRows[r]].SubjectId, label, distance);
        }

        var clusters = new MetricCluster[clusterCount];
        for (var k = 0; k < clusterCount; k++)
        {
            var centre = new double[width];
            for (var c = 0; c < width; c++)
            {
                var spread = scaling[c].StandardDeviation;
                centre[c] = scaling[c].Mean + (centres[k][c] * (spread > 0d ? spread : 1d));
            }

            clusters[k] = new MetricCluster(
                k,
                counts[k],
                centre,
                centres[k],
                counts[k] > 0 ? distanceSums[k] / counts[k] : 0d);
        }

        var within = distanceSums.Sum() / scaled.Length;
        var betweenSum = 0d;
        var betweenPairs = 0;
        for (var a = 0; a < clusterCount; a++)
        {
            for (var b = a + 1; b < clusterCount; b++)
            {
                betweenSum += Distance(centres[a], centres[b]);
                betweenPairs++;
            }
        }

        var between = betweenPairs > 0 ? betweenSum / betweenPairs : 0d;
        var separation = new MetricSeparation(
            within, between, between > 0d ? within / between : null);

        return new MetricClusterModel(
            names, clusterCount, population, scaling, clusters, assignments, separation,
            iterations, converged);
    }

    /// <summary>
    /// Centres each column on its mean and divides by its spread, writing the standardised matrix to
    /// <paramref name="scaled"/> and returning what was divided by.
    /// </summary>
    /// <remarks>
    /// The spread is the population standard deviation rather than the sample one. The choice does
    /// not change any grouping — dividing every column by the same extra constant is a uniform
    /// rescaling of the whole space — and the population form is the one that makes the reported
    /// figure the spread of the subjects that were actually clustered, which is what a reader will
    /// take it for.
    /// </remarks>
    private static MetricScaling[] Standardise(
        string[] names, double[][] raw, out double[][] scaled)
    {
        var width = names.Length;
        var scalings = new MetricScaling[width];
        scaled = new double[raw.Length][];
        for (var r = 0; r < raw.Length; r++)
        {
            scaled[r] = new double[width];
        }

        for (var c = 0; c < width; c++)
        {
            var mean = 0d;
            for (var r = 0; r < raw.Length; r++)
            {
                mean += raw[r][c];
            }

            mean = raw.Length > 0 ? mean / raw.Length : 0d;

            var variance = 0d;
            for (var r = 0; r < raw.Length; r++)
            {
                var d = raw[r][c] - mean;
                variance += d * d;
            }

            variance = raw.Length > 0 ? variance / raw.Length : 0d;
            var spread = Math.Sqrt(variance);

            // A column every subject recorded the same value for carries no information. Dividing
            // by its spread would be dividing by zero; dividing by one leaves every subject at the
            // same coordinate, which contributes nothing to any distance, which is the truth.
            var divisor = spread > 0d ? spread : 1d;
            for (var r = 0; r < raw.Length; r++)
            {
                scaled[r][c] = (raw[r][c] - mean) / divisor;
            }

            scalings[c] = new MetricScaling(names[c], mean, spread);
        }

        return scalings;
    }

    /// <summary>
    /// Lloyd's refinement from chosen starting positions, run until no subject changes group.
    /// </summary>
    private static (int[] Labels, double[][] Centres, int Iterations, bool Converged) Fit(
        double[][] points, int clusterCount)
    {
        var centres = ChooseStartingCentres(points, clusterCount);
        var labels = new int[points.Length];
        Array.Fill(labels, -1);

        var iterations = 0;
        var converged = false;
        while (iterations < MaximumIterations)
        {
            iterations++;
            var moved = false;
            for (var r = 0; r < points.Length; r++)
            {
                var best = 0;
                var bestDistance = double.PositiveInfinity;
                for (var k = 0; k < clusterCount; k++)
                {
                    var distance = Distance(points[r], centres[k]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = k;
                    }
                }

                if (labels[r] != best)
                {
                    labels[r] = best;
                    moved = true;
                }
            }

            RefillEmptyClusters(points, labels, clusterCount);
            centres = Recentre(points, labels, clusterCount, centres);

            if (!moved)
            {
                converged = true;
                break;
            }
        }

        return (labels, centres, iterations, converged);
    }

    /// <summary>
    /// Picks the starting positions by taking the subject furthest from the population's middle and
    /// then, repeatedly, the subject furthest from everything chosen so far.
    /// </summary>
    /// <remarks>
    /// Chosen rather than drawn so the answer is reproducible without a seed to carry around, and
    /// spread rather than arbitrary so a refinement started from it does not converge onto a
    /// grouping that splits one real group and merges two others. Ties fall to the earlier subject,
    /// and subjects were sorted by identity before this ran, so a tie resolves the same way every
    /// time.
    /// </remarks>
    private static double[][] ChooseStartingCentres(double[][] points, int clusterCount)
    {
        var width = points[0].Length;
        var origin = new double[width];
        var centres = new double[clusterCount][];

        var first = 0;
        var furthest = -1d;
        for (var r = 0; r < points.Length; r++)
        {
            // The columns are standardised, so the population's middle is the origin.
            var distance = Distance(points[r], origin);
            if (distance > furthest)
            {
                furthest = distance;
                first = r;
            }
        }

        centres[0] = (double[])points[first].Clone();

        var nearest = new double[points.Length];
        for (var r = 0; r < points.Length; r++)
        {
            nearest[r] = Distance(points[r], centres[0]);
        }

        for (var k = 1; k < clusterCount; k++)
        {
            var pick = 0;
            var best = -1d;
            for (var r = 0; r < points.Length; r++)
            {
                if (nearest[r] > best)
                {
                    best = nearest[r];
                    pick = r;
                }
            }

            centres[k] = (double[])points[pick].Clone();
            for (var r = 0; r < points.Length; r++)
            {
                nearest[r] = Math.Min(nearest[r], Distance(points[r], centres[k]));
            }
        }

        return centres;
    }

    /// <summary>
    /// Gives every group that lost all its members one subject back, so the answer holds as many
    /// groups as were asked for.
    /// </summary>
    /// <remarks>
    /// The subject moved is the one standing furthest from its own group's middle, taken from a
    /// group that can spare it. That is deterministic and it moves the subject the current grouping
    /// describes worst, which is the one a further group was most plausibly wanted for.
    /// </remarks>
    private static void RefillEmptyClusters(double[][] points, int[] labels, int clusterCount)
    {
        var counts = new int[clusterCount];
        foreach (var label in labels)
        {
            counts[label]++;
        }

        for (var k = 0; k < clusterCount; k++)
        {
            if (counts[k] > 0)
            {
                continue;
            }

            var centres = Recentre(points, labels, clusterCount, null);
            var pick = -1;
            var furthest = -1d;
            for (var r = 0; r < points.Length; r++)
            {
                if (counts[labels[r]] <= 1)
                {
                    continue;
                }

                var distance = Distance(points[r], centres[labels[r]]);
                if (distance > furthest)
                {
                    furthest = distance;
                    pick = r;
                }
            }

            if (pick < 0)
            {
                return;
            }

            counts[labels[pick]]--;
            labels[pick] = k;
            counts[k]++;
        }
    }

    /// <summary>
    /// Moves each group's middle to the mean of its members, leaving an empty group's middle where
    /// it was when there is a previous position to keep.
    /// </summary>
    private static double[][] Recentre(
        double[][] points, int[] labels, int clusterCount, double[][]? previous)
    {
        var width = points[0].Length;
        var sums = new double[clusterCount][];
        var counts = new int[clusterCount];
        for (var k = 0; k < clusterCount; k++)
        {
            sums[k] = new double[width];
        }

        for (var r = 0; r < points.Length; r++)
        {
            var label = labels[r];
            if (label < 0)
            {
                continue;
            }

            counts[label]++;
            for (var c = 0; c < width; c++)
            {
                sums[label][c] += points[r][c];
            }
        }

        var centres = new double[clusterCount][];
        for (var k = 0; k < clusterCount; k++)
        {
            if (counts[k] == 0)
            {
                centres[k] = previous is not null
                    ? (double[])previous[k].Clone()
                    : new double[width];
                continue;
            }

            centres[k] = new double[width];
            for (var c = 0; c < width; c++)
            {
                centres[k][c] = sums[k][c] / counts[k];
            }
        }

        return centres;
    }

    private static double Distance(double[] a, double[] b)
    {
        var total = 0d;
        for (var c = 0; c < a.Length; c++)
        {
            var d = a[c] - b[c];
            total += d * d;
        }

        return Math.Sqrt(total);
    }
}
