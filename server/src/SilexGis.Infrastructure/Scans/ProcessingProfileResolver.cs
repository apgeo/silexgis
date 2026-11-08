// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Scans;

/// <summary>Why the profiles named for a run could not be applied: a stable code and the words that name the profile or key.</summary>
public sealed record ProfileResolutionProblem(string Code, string Detail);

/// <summary>What applying profiles to a recipe gave: the recipe with their values resolved, and which profile each step took.</summary>
public sealed record ProfileResolution(ScanRecipe Recipe, IReadOnlyDictionary<string, Guid> Chosen);

/// <summary>
/// Applies the processing profiles an upload or a rerun names to its recipe, and remembers each
/// person's last choice per step.
/// </summary>
/// <remarks>
/// <para>
/// A profile is copied into the recipe, never referred to: its values are laid over the step's
/// defaults and the recipe stores the result, so editing or deleting the profile later never
/// changes what a run meant. HDMapping's odometry is laid over the defaults the installed program
/// dumped, the same file the run completes a set with, so the stored set names every key the
/// program reads; while no worker has ever dumped them the profile's own keys are stored and the
/// run completes them once it knows the defaults.
/// </para>
/// <para>
/// A profile the caller may not read is answered as unknown, never as forbidden, exactly as the
/// profile routes answer it. A step's values are checked against the schema of the version that
/// will run them, so a profile written for another version that names a key this one does not have
/// is refused here, with the key named, rather than by the worker an hour later.
/// </para>
/// <para>
/// The thinning step takes no profile: how much of a cloud to show is the display choice the same
/// request makes, and two answers to one question in one request would need a rule for which wins.
/// </para>
/// </remarks>
public sealed class ProcessingProfileResolver(SilexGisDbContext db, ScanStepParameterSchemas schemas, TimeProvider clock)
{
    /// <summary>A profile named does not exist, or is not one the caller may use.</summary>
    public const string UnknownCode = "survey_upload.profile_unknown";

    /// <summary>A profile is for a step this upload's recipe does not run.</summary>
    public const string StepNotInRecipeCode = "survey_upload.profile_step_not_in_recipe";

    /// <summary>Two profiles named are for the same step.</summary>
    public const string StepTwiceCode = "survey_upload.profile_step_twice";

    /// <summary>A profile for the thinning step, which the display choice decides.</summary>
    public const string DisplayStepCode = "survey_upload.profile_display_step";

    /// <summary>A profile's values do not fit the step's schema for the version that will run; the detail names each key.</summary>
    public const string ValuesInvalidCode = "survey_upload.profile_values_invalid";

    /// <summary>The most profiles one request may name; far more than a recipe has steps.</summary>
    public const int MaxProfiles = 16;

    public async Task<(ProfileResolution? Resolution, ProfileResolutionProblem? Problem)> ApplyAsync(
        AccessContext ctx, ScanRecipe recipe, IReadOnlyList<Guid>? profileIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(recipe);
        var chosen = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (profileIds is null || profileIds.Count == 0)
        {
            return (new ProfileResolution(recipe, chosen), null);
        }

        var ids = profileIds.Distinct().ToList();
        var rows = await db.ProcessingProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var id in ids)
        {
            if (!rows.TryGetValue(id, out var profile) || !ProcessingProfileRules.MayRead(ctx, profile))
            {
                return (null, new ProfileResolutionProblem(UnknownCode, $"No processing profile {id} is available to you."));
            }

            if (chosen.ContainsKey(profile.StepId))
            {
                return (null, new ProfileResolutionProblem(StepTwiceCode, $"Two profiles are named for the step '{profile.StepId}'."));
            }

            if (profile.StepId == PointCloudDownsampleStep.StepId)
            {
                return (null, new ProfileResolutionProblem(DisplayStepCode, "How much of the cloud to show is chosen with the display choice, not with a profile."));
            }

            if (ScanSteps.Find(profile.StepId) is not { } step || recipe.Steps.All(s => s.StepId != profile.StepId))
            {
                return (null, new ProfileResolutionProblem(StepNotInRecipeCode, $"The profile '{profile.Name}' is for the step '{profile.StepId}', which this upload does not run."));
            }

            var schema = await schemas.ForAsync(step, ct);
            using var values = JsonDocument.Parse(profile.Values);
            JsonElement parameters;
            IReadOnlyList<string> errors;
            if (step.Id == HdmappingOdometryStep.StepId)
            {
                // The schema derived from the dump describes the parameters a profile may name, not
                // the dump's machine sections, so the profile's own keys are what it checks. It is laid over the
                // editable defaults, which hold no machine section and no non-finite value the worker would refuse.
                errors = schemas.Validate(schema, values.RootElement);
                parameters = schema.DumpedDefaults.ValueKind == JsonValueKind.Object
                    ? ProcessingProfileValues.LayOver(schema.Defaults, values.RootElement)
                    : values.RootElement.Clone();
            }
            else
            {
                parameters = ProcessingProfileValues.LayOver(schema.Defaults, values.RootElement);
                errors = schemas.Validate(schema, parameters);
            }

            if (errors.Count > 0)
            {
                return (null, new ProfileResolutionProblem(
                    ValuesInvalidCode, $"The profile '{profile.Name}' does not fit this version of its step: {string.Join("; ", errors)}"));
            }

            recipe = PointCloudRecipes.WithStepParameters(recipe, step.Id, parameters);
            chosen[step.Id] = profile.Id;
        }

        return (new ProfileResolution(recipe, chosen), null);
    }

    /// <summary>
    /// Records that this person used these profiles last, one per step. Called once the run they
    /// were named for is saved, so a refused upload remembers nothing.
    /// </summary>
    public async Task RememberAsync(Guid userId, IReadOnlyDictionary<string, Guid> chosen, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var now = clock.GetUtcNow();
        foreach (var (stepId, profileId) in chosen)
        {
            var updated = await db.ScanProfileChoices
                .Where(c => c.UserId == userId && c.StepId == stepId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProfileId, profileId).SetProperty(c => c.ChosenAt, now), ct);
            if (updated > 0)
            {
                continue;
            }

            var choice = new ScanProfileChoice { UserId = userId, StepId = stepId, ProfileId = profileId, ChosenAt = now };
            db.ScanProfileChoices.Add(choice);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A second request of the same person stored a choice for this step first, or the
                // profile went in between. Either way the last word on "used last" is not worth
                // failing a run that has already been saved.
            }
            finally
            {
                db.Entry(choice).State = EntityState.Detached;
            }
        }
    }

}
