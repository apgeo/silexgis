// SPDX-License-Identifier: AGPL-3.0-or-later
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace SilexGis.Api.Auth;

/// <summary>
/// When a refresh token is revoked, the whole sign-in it belongs to is revoked with it — that one
/// sign-in, and no other of the account.
/// </summary>
/// <remarks>
/// <para>
/// The protocol's revocation endpoint cancels the token presented and nothing else. That is not
/// enough for signing out. Renewal rotates the refresh token, and the one it replaced stays
/// acceptable for a short while afterwards so that a client whose connection dropped mid-renewal
/// can retry; a copy of that earlier token, presented inside that while, would mint a fresh chain
/// the revocation never touched. So the grant the token hangs off — the authorization, which every
/// token of one sign-in shares and no other sign-in does — is revoked too, together with every
/// token issued under it.
/// </para>
/// <para>
/// Scoped to the one sign-in on purpose. Signing out of the laptop at the expedition hut is not
/// signing out of the phone; ending every session of an account is what changing the password is
/// for, and it does that through its own path.
/// </para>
/// </remarks>
internal sealed class SignInRevocation(
    IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens)
    : IOpenIddictServerHandler<HandleRevocationRequestContext>
{
    /// <summary>Runs after the token itself has been revoked, and only for a request that got that far.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleRevocationRequestContext>()
            .UseScopedHandler<SignInRevocation>()
            .SetOrder(OpenIddictServerHandlers.Revocation.RevokeToken.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(HandleRevocationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsRejected
            || context.GenericTokenPrincipal is not { } principal
            || principal.GetTokenType() != TokenTypeIdentifiers.RefreshToken
            || principal.GetAuthorizationId() is not { Length: > 0 } authorizationId)
        {
            return;
        }

        var ct = context.CancellationToken;
        if (await authorizations.FindByIdAsync(authorizationId, ct) is { } authorization)
        {
            await authorizations.TryRevokeAsync(authorization, ct);
        }

        await tokens.RevokeByAuthorizationIdAsync(authorizationId, ct);
    }
}
