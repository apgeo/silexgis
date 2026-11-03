// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Domain;

namespace SilexGis.Api.Features.Geofiles;

public sealed class GeofileUpdateRequestValidator : AbstractValidator<GeofileUpdateRequest>
{
    public GeofileUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Visibility).IsInEnum();

        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be a band that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");
        RuleFor(x => x.Style)
            .Must(s => s is null || s.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            .WithMessage("Style must be a JSON object.");
    }
}

public sealed class GeofileReimportRequestValidator : AbstractValidator<GeofileReimportRequest>
{
    /// <summary>The longest column heading a choice may name; a header cell is never longer in practice.</summary>
    private const int MaxColumnNameLength = 200;

    public GeofileReimportRequestValidator()
    {
        When(x => x.SourceOptions?.Delimited is not null, () =>
        {
            // The reader splits on one character (or a tab, spelled "\t"). A longer value would
            // be cut to its first character in silence, and a quote or a line break would make
            // every row unreadable, so neither is accepted as a separator.
            RuleFor(x => x.SourceOptions!.Delimited!.Delimiter)
                .Must(d => string.IsNullOrEmpty(d) || d == "\\t" || (d.Length == 1 && d[0] is not ('"' or '\r' or '\n')))
                .WithMessage("The delimiter must be a single character, or \\t for a tab.");
            RuleFor(x => x.SourceOptions!.Delimited!.LatitudeColumn).MaximumLength(MaxColumnNameLength);
            RuleFor(x => x.SourceOptions!.Delimited!.LongitudeColumn).MaximumLength(MaxColumnNameLength);
            RuleFor(x => x.SourceOptions!.Delimited!.ElevationColumn).MaximumLength(MaxColumnNameLength);
            RuleFor(x => x.SourceOptions!.Delimited!.WktColumn).MaximumLength(MaxColumnNameLength);
            RuleFor(x => x.SourceOptions!.Delimited!.Encoding).IsInEnum();
        });
    }
}
