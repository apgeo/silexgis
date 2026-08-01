// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Features.FeatureShares;

public sealed class FeatureShareCreateRequestValidator : AbstractValidator<FeatureShareCreateRequest>
{
    public FeatureShareCreateRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
    }
}
