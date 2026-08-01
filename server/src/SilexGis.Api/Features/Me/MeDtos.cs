// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// The audience the account holder chose for each governed profile field. The same record is
/// read and written — a profile save is a full-DTO PUT.
/// </summary>
public sealed record ProfileVisibilityDto(
    ProfileVisibility RealName,
    ProfileVisibility Bio,
    ProfileVisibility Email,
    ProfileVisibility Phone,
    ProfileVisibility CavingClub,
    ProfileVisibility Address,
    ProfileVisibility AddressPoint);

public sealed record UserAddressDto(
    Guid Id,
    string Label,
    string? Country,
    string? City,
    string? AddressText,
    GeoJsonPoint? Geom,
    int SortOrder);

public sealed record UserAddressWriteRequest(
    string Label,
    string? Country,
    string? City,
    string? AddressText,
    GeoJsonPoint? Geom,
    int SortOrder);

/// <summary>
/// Everything the account holder's own settings pages need, in one response: profile fields,
/// the visibility choices, the addresses and a ready-to-render avatar URL. What the caller
/// may *do* is not here — the capabilities and permission-group routes answer that.
/// </summary>
/// <remarks>
/// Deliberately carries no ETag. The client replays the ETag of a single-resource GET as
/// If-Match on the matching PUT, so emitting one here would silently give every settings save
/// optimistic-concurrency semantics and 412 a tab left open. A single-user resource is
/// last-write-wins by design.
/// </remarks>
public sealed record MeDto(
    Guid Id,
    string UserName,
    string Email,
    bool EmailConfirmed,
    string? PendingEmail,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Bio,
    string? PhoneNumber,
    Guid? CavingClubId,
    string Locale,
    string? AvatarUrl,
    string? AvatarPreset,
    ProfileVisibilityDto Visibility,
    IReadOnlyList<UserAddressDto> Addresses,
    DateTimeOffset CreatedAt);

/// <summary>
/// Profile save. Carries neither the email address nor the user name: both are credentials with
/// their own endpoints, so a profile save can never move one.
/// </summary>
public sealed record MeUpdateRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Bio,
    string? PhoneNumber,
    Guid? CavingClubId,
    string Locale,
    ProfileVisibilityDto Visibility);

public sealed record AvatarPresetRequest(string Preset);

public sealed record AvatarPresetsDto(IReadOnlyList<string> Presets);

/// <summary>
/// The built-in avatars a user can pick instead of uploading one. The ids are the stored wire
/// values; the artwork ships with the client keyed by the same ids, so the set has one home.
/// </summary>
public static class AvatarPresets
{
    public static IReadOnlyList<string> Ids { get; } =
        ["bat", "helmet", "lamp", "rope", "karst", "stalactite", "compass", "sump"];

    public static bool IsKnown(string id) => Ids.Contains(id, StringComparer.Ordinal);
}

public sealed class MeUpdateRequestValidator : AbstractValidator<MeUpdateRequest>
{
    public MeUpdateRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.DisplayName).MaximumLength(100);
        RuleFor(x => x.Bio).MaximumLength(2000);

        // Permissive on purpose: international numbers are written many ways and the server has
        // no way to verify one. This rejects free text, not formatting choices.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .Matches("^[0-9+ ()./-]*$").WithMessage("The phone number contains unexpected characters.")
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

        // A language tag rather than an allow-list, so shipping a third translation needs no
        // server change.
        RuleFor(x => x.Locale)
            .NotEmpty()
            .MaximumLength(10)
            .Matches("^[a-z]{2}(-[A-Z]{2})?$").WithMessage("The locale must be a language tag such as 'en' or 'ro'.");

        RuleFor(x => x.Visibility).NotNull();
        RuleFor(x => x.Visibility.RealName).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.Bio).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.Email).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.Phone).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.CavingClub).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.Address).IsInEnum().When(x => x.Visibility is not null);
        RuleFor(x => x.Visibility.AddressPoint).IsInEnum().When(x => x.Visibility is not null);
    }
}

public sealed class UserAddressWriteRequestValidator : AbstractValidator<UserAddressWriteRequest>
{
    public UserAddressWriteRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.AddressText).MaximumLength(4000);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);

        When(x => x.Geom is not null, () =>
        {
            RuleFor(x => x.Geom!.Type).Equal("Point");
            RuleFor(x => x.Geom!.Coordinates)
                .NotNull()
                .Must(c => c.Length is 2 or 3).WithMessage("A point needs two or three coordinates.");
            RuleFor(x => x.Geom!.Coordinates[0])
                .InclusiveBetween(-180, 180)
                .When(x => x.Geom!.Coordinates?.Length >= 2);
            RuleFor(x => x.Geom!.Coordinates[1])
                .InclusiveBetween(-90, 90)
                .When(x => x.Geom!.Coordinates?.Length >= 2);
        });
    }
}

public sealed class AvatarPresetRequestValidator : AbstractValidator<AvatarPresetRequest>
{
    public AvatarPresetRequestValidator() =>
        RuleFor(x => x.Preset)
            .NotEmpty()
            .Must(AvatarPresets.IsKnown).WithMessage("Unknown avatar.");
}

/// <summary>Shared mapping for the account holder's own view of their account.</summary>
internal static class MeMapping
{
    public static MeDto ToDto(
        SilexGisUser user,
        IReadOnlyList<UserAddress> addresses,
        IFileAccessTokenService tokens) =>
        new(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PendingEmail,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.Bio,
            user.PhoneNumber,
            user.CavingClubId,
            user.Locale,
            AvatarUrl(user.AvatarFileId, tokens),
            user.AvatarPreset,
            new ProfileVisibilityDto(
                user.RealNameVisibility,
                user.BioVisibility,
                user.EmailVisibility,
                user.PhoneVisibility,
                user.CavingClubVisibility,
                user.AddressVisibility,
                user.AddressPointVisibility),
            [.. addresses.OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt).Select(ToDto)],
            user.CreatedAt);

    public static UserAddressDto ToDto(UserAddress address) => new(
        address.Id,
        address.Label,
        address.Country,
        address.City,
        address.AddressText,
        address.Geom is null ? null : GeoJsonPoint.From(address.Geom),
        address.SortOrder);

    /// <summary>
    /// A ready-to-render avatar URL carrying a short-lived delivery token. Built here rather
    /// than shared with the file slice, which owns its own mapping internals.
    /// </summary>
    public static string? AvatarUrl(Guid? avatarFileId, IFileAccessTokenService tokens) =>
        avatarFileId is null
            ? null
            : $"/api/v1/files/{avatarFileId}/thumbnail?size=480&token={Uri.EscapeDataString(tokens.CreateToken(avatarFileId.Value))}";
}
