// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>The text of one template in one language. <see cref="Subject"/> is null for SMS.</summary>
public sealed record MessageTemplateText(string? Subject, string Body);

/// <summary>
/// Everything the application knows about one kind of message: which channel carries it, which
/// placeholders it may use, and the wording shipped with the product.
/// </summary>
public sealed record MessageTemplateDefinition(
    string Key,
    MessageChannel Channel,
    string Description,
    IReadOnlyList<string> Placeholders,
    IReadOnlyDictionary<string, MessageTemplateText> Defaults);

/// <summary>
/// The message vocabulary, in one place. An operator may rewrite any of these from the admin
/// settings, but may not invent new ones: a template exists only because some code path sends it.
/// </summary>
/// <remarks>
/// <para>
/// Placeholders are written <c>{name}</c> and are declared per template. Editing rejects a
/// placeholder that is not on the template's list, because an unknown one would silently render
/// as nothing — the sort of mistake only discovered when a user cannot find the code they were
/// promised. The renderer leaves declared-but-unsupplied placeholders empty for the same reason
/// it does not throw: a broken template must not stop a password reset.
/// </para>
/// <para>
/// Bodies are plain text. HTML mail would need a sanitiser, a multipart builder and a second
/// editing surface for what is, in every case here, four lines telling someone a code or a link.
/// </para>
/// </remarks>
public static class MessageTemplateCatalog
{
    /// <summary>Confirms the address an account already holds (registration, or a re-send).</summary>
    public const string EmailConfirmAddress = "email.confirm-address";

    /// <summary>Confirms a new address before it replaces the live one.</summary>
    public const string EmailChangeAddress = "email.change-address";

    public const string EmailPasswordReset = "email.password-reset";

    /// <summary>Second-factor code delivered by mail.</summary>
    public const string EmailTwoFactorCode = "email.two-factor-code";

    /// <summary>Second-factor code delivered by text message.</summary>
    public const string SmsTwoFactorCode = "sms.two-factor-code";

    /// <summary>Confirms a phone number before it may carry second-factor codes.</summary>
    public const string SmsVerifyPhone = "sms.verify-phone";

    // Notifications. Unlike the messages above — which answer something the recipient just did —
    // these report something that happened to them, and every one of them is governed by the
    // notification settings. Their wording deliberately names no role, kind or status word: those
    // would have to arrive as placeholders in one language and would then be untranslatable.

    public const string NotifyCavingGroupJoined = "notify.caving-group-joined";

    public const string NotifyCavingGroupRoleChanged = "notify.caving-group-role-changed";

    public const string NotifyCavingGroupRemoved = "notify.caving-group-removed";

    public const string NotifyPermissionGranted = "notify.permission-granted";

    public const string NotifyTripParticipation = "notify.trip-participation";

    // A trip being planned. All four name the trip and its date and nothing else: the places a
    // trip is about are readable by fewer people than its roster, and a message is as much an
    // outbound copy of that as anything the API returns.

    /// <summary>Someone was asked whether they are coming on a trip being planned.</summary>
    public const string NotifyTripPlanInvitation = "notify.trip-plan-invitation";

    /// <summary>A trip somebody is on was changed while it was still being planned.</summary>
    public const string NotifyTripPlanChanged = "notify.trip-plan-changed";

    /// <summary>A trip somebody is on was called off.</summary>
    public const string NotifyTripPlanCancelled = "notify.trip-plan-cancelled";

    /// <summary>A trip somebody is on is coming up.</summary>
    /// <remarks>
    /// Sent by the pass that watches for overdue parties rather than written ahead of time onto
    /// the queue, so that a trip put back or called off stops reminding people about a date that
    /// is no longer true.
    /// </remarks>
    public const string NotifyTripPlanReminder = "notify.trip-plan-reminder";

    /// <summary>
    /// A party said when they would be back, the time has gone by, and nobody has said they are
    /// out.
    /// </summary>
    /// <remarks>
    /// It names the trip, the date and the hour that passed, and — like the four above and for the
    /// same reason — no cave. The temptation is strongest here, because an overdue party feels
    /// like the one message that ought to say where they are; but the message goes to everybody
    /// the trip names, and where a cave is remains readable by fewer people than that. Whoever
    /// runs a search reads the trip, where the answer is already kept for them.
    /// It carries no opt-out line: nobody may switch this one off, so a link that could not work
    /// would be a lie.
    /// </remarks>
    public const string NotifyTripCalloutOverdue = "notify.trip-callout-overdue";

    /// <summary>
    /// Somebody asked on a trip cannot open a cave the trip is about, and the people who could
    /// change that are being told.
    /// </summary>
    /// <remarks>
    /// The one message about a trip that names a cave, and it may only be sent to somebody whose
    /// own access already opens that cave — which is what makes naming it there a reminder of
    /// something they can see rather than a disclosure of something they cannot.
    /// </remarks>
    public const string NotifyTripInviteeCannotOpenCave = "notify.trip-invitee-cannot-open-cave";

    public const string NotifyJobCompleted = "notify.job-completed";

    public const string NotifyJobFailed = "notify.job-failed";

    public const string NotifySecurityPasswordChanged = "notify.security-password-changed";

    /// <summary>Warns the address an account is moving away from. Sent to the old address.</summary>
    public const string NotifySecurityEmailChanged = "notify.security-email-changed";

    public const string NotifySecurityTwoFactorDisabled = "notify.security-two-factor-disabled";

    /// <summary>The daily summary. Its lines are the other messages' own subjects.</summary>
    public const string NotifyDigest = "notify.digest";

    /// <summary>Languages every template ships in. A locale outside this set falls back to English.</summary>
    public static IReadOnlyList<string> Locales { get; } = ["en", "ro"];

    public const string FallbackLocale = "en";

    /// <summary>Placeholder every template may use: the installation's name.</summary>
    private const string AppName = "appName";

    /// <summary>Placeholder every template may use: how the recipient is addressed.</summary>
    private const string DisplayName = "displayName";

    /// <summary>Notification placeholder: whoever did the thing being reported.</summary>
    private const string ActorName = "actorName";

    /// <summary>Notification placeholder: where the installation lives, for a "go and look" link.</summary>
    private const string SiteUrl = "siteUrl";

    /// <summary>
    /// Notification placeholder: the one-click opt-out line. Filled by the worker, which is the
    /// only thing that knows the recipient and can sign a token for them.
    /// </summary>
    private const string UnsubscribeUrl = "unsubscribeUrl";

    public static IReadOnlyList<MessageTemplateDefinition> All { get; } =
    [
        new(
            EmailConfirmAddress,
            MessageChannel.Email,
            "Sent when an account's own address needs confirming — after registration, or on request.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Confirm your {appName} address",
                    """
                    Hello {displayName},

                    Confirm this address to finish setting up your {appName} account:

                    {url}

                    The link is valid for {expiresHours} hours. If you did not create this account,
                    ignore this message — nothing further will happen.
                    """),
                ["ro"] = new(
                    "Confirmați adresa dumneavoastră {appName}",
                    """
                    Bună ziua {displayName},

                    Confirmați această adresă pentru a finaliza configurarea contului {appName}:

                    {url}

                    Linkul este valabil {expiresHours} ore. Dacă nu dumneavoastră ați creat acest cont,
                    ignorați acest mesaj — nu se va întâmpla nimic.
                    """),
            }),

        new(
            EmailChangeAddress,
            MessageChannel.Email,
            "Sent to the proposed new address when someone changes the address on their account.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Confirm your new {appName} address",
                    """
                    Hello {displayName},

                    Confirm this address to make it the one on your {appName} account:

                    {url}

                    The link is valid for {expiresHours} hours. Until then your current address is
                    unchanged. If you did not ask for this, ignore this message — nothing has changed.
                    """),
                ["ro"] = new(
                    "Confirmați noua adresă {appName}",
                    """
                    Bună ziua {displayName},

                    Confirmați această adresă pentru a deveni adresa contului dumneavoastră {appName}:

                    {url}

                    Linkul este valabil {expiresHours} ore. Până atunci adresa actuală rămâne neschimbată.
                    Dacă nu dumneavoastră ați cerut acest lucru, ignorați mesajul — nimic nu s-a schimbat.
                    """),
            }),

        new(
            EmailPasswordReset,
            MessageChannel.Email,
            "Sent when someone asks to reset a forgotten password.",
            [AppName, DisplayName, "url", "expiresHours"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Reset your {appName} password",
                    """
                    Hello {displayName},

                    Open this link to choose a new password:

                    {url}

                    The link is valid for {expiresHours} hours and can be used once. If you did not
                    ask for a reset, ignore this message — your password stays as it is.
                    """),
                ["ro"] = new(
                    "Resetați parola {appName}",
                    """
                    Bună ziua {displayName},

                    Deschideți acest link pentru a alege o parolă nouă:

                    {url}

                    Linkul este valabil {expiresHours} ore și poate fi folosit o singură dată. Dacă nu
                    dumneavoastră ați cerut resetarea, ignorați mesajul — parola rămâne neschimbată.
                    """),
            }),

        new(
            EmailTwoFactorCode,
            MessageChannel.Email,
            "Carries a sign-in code to someone whose second factor is their email address.",
            [AppName, DisplayName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{code} is your {appName} sign-in code",
                    """
                    Hello {displayName},

                    Your sign-in code is {code}. It expires in {expiresMinutes} minutes.

                    If you are not signing in right now, someone else knows your password — change it
                    as soon as you can.
                    """),
                ["ro"] = new(
                    "{code} este codul dumneavoastră de autentificare {appName}",
                    """
                    Bună ziua {displayName},

                    Codul de autentificare este {code}. Expiră în {expiresMinutes} minute.

                    Dacă nu vă autentificați chiar acum, altcineva vă cunoaște parola — schimbați-o
                    cât mai curând.
                    """),
            }),

        new(
            SmsTwoFactorCode,
            MessageChannel.Sms,
            "Carries a sign-in code to someone whose second factor is their phone.",
            [AppName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(null, "{code} is your {appName} sign-in code. It expires in {expiresMinutes} minutes."),
                ["ro"] = new(null, "{code} este codul dumneavoastră de autentificare {appName}. Expiră în {expiresMinutes} minute."),
            }),

        new(
            SmsVerifyPhone,
            MessageChannel.Sms,
            "Confirms a phone number before it is allowed to carry sign-in codes.",
            [AppName, "code", "expiresMinutes"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(null, "{code} is your {appName} phone verification code. It expires in {expiresMinutes} minutes."),
                ["ro"] = new(null, "{code} este codul dumneavoastră de verificare a telefonului {appName}. Expiră în {expiresMinutes} minute."),
            }),

        new(
            NotifyCavingGroupJoined,
            MessageChannel.Email,
            "Someone was added to a caving group.",
            [AppName, DisplayName, ActorName, "cavingGroupName", SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "You were added to {cavingGroupName}",
                    """
                    Hello {displayName},

                    {actorName} added you to the caving group {cavingGroupName} on {appName}.

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Ați fost adăugat în {cavingGroupName}",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a adăugat în grupul {cavingGroupName} pe {appName}.

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyCavingGroupRoleChanged,
            MessageChannel.Email,
            "Someone's role within a caving group was changed.",
            [AppName, DisplayName, ActorName, "cavingGroupName", SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Your role in {cavingGroupName} changed",
                    """
                    Hello {displayName},

                    {actorName} changed your role in the caving group {cavingGroupName}. You can see what you can
                    now do from the caving group's page:

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Rolul dumneavoastră în {cavingGroupName} s-a schimbat",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a schimbat rolul în grupul {cavingGroupName}. Puteți vedea ce puteți face
                    acum din pagina grupului:

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyCavingGroupRemoved,
            MessageChannel.Email,
            "Someone was removed from a caving group.",
            [AppName, DisplayName, ActorName, "cavingGroupName", SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "You were removed from {cavingGroupName}",
                    """
                    Hello {displayName},

                    {actorName} removed you from the caving group {cavingGroupName} on {appName}. You may no longer
                    have access to what that caving group could see.

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Ați fost eliminat din {cavingGroupName}",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a eliminat din grupul {cavingGroupName} pe {appName}. Este posibil să nu
                    mai aveți acces la ce mai vedea acel grup.

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyPermissionGranted,
            MessageChannel.Email,
            "Someone was given access to a record — directly, or through a caving group they belong to.",
            [AppName, DisplayName, ActorName, "objectName", "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "You were given access to {objectName}",
                    """
                    Hello {displayName},

                    {actorName} gave you access to {objectName} on {appName}:

                    {url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Ați primit acces la {objectName}",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a dat acces la {objectName} pe {appName}:

                    {url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripParticipation,
            MessageChannel.Email,
            "Someone was listed as taking part in a trip.",
            [AppName, DisplayName, ActorName, "tripTitle", "tripDate", "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "You were listed on the trip {tripTitle}",
                    """
                    Hello {displayName},

                    {actorName} listed you as taking part in {tripTitle} on {tripDate}:

                    {url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Ați fost trecut în tura {tripTitle}",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a trecut ca participant la {tripTitle} în data de {tripDate}:

                    {url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripPlanInvitation,
            MessageChannel.Email,
            "Someone was asked whether they are coming on a trip being planned.",
            [AppName, DisplayName, ActorName, "tripTitle", "tripDate", SiteUrl, "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "You were invited to {tripTitle}",
                    """
                    Hello {displayName},

                    {actorName} invited you to {tripTitle} on {tripDate}. You can answer here:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Ați fost invitat la {tripTitle}",
                    """
                    Bună ziua {displayName},

                    {actorName} v-a invitat la {tripTitle} în data de {tripDate}. Puteți răspunde aici:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripPlanChanged,
            MessageChannel.Email,
            "A trip someone is on was changed while it was still being planned.",
            [AppName, DisplayName, ActorName, "tripTitle", "tripDate", SiteUrl, "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{tripTitle} has changed",
                    """
                    Hello {displayName},

                    {actorName} changed {tripTitle}, planned for {tripDate}:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "S-a modificat {tripTitle}",
                    """
                    Bună ziua {displayName},

                    {actorName} a modificat {tripTitle}, planificată pentru {tripDate}:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripPlanCancelled,
            MessageChannel.Email,
            "A trip someone is on was called off.",
            [AppName, DisplayName, ActorName, "tripTitle", "tripDate", SiteUrl, "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{tripTitle} was called off",
                    """
                    Hello {displayName},

                    {actorName} called off {tripTitle}, planned for {tripDate}. It is not going ahead.

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "{tripTitle} a fost anulată",
                    """
                    Bună ziua {displayName},

                    {actorName} a anulat {tripTitle}, planificată pentru {tripDate}. Tura nu mai are loc.

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripPlanReminder,
            MessageChannel.Email,
            "A trip someone is on is coming up.",
            [AppName, DisplayName, "tripTitle", "tripDate", SiteUrl, "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{tripTitle} is coming up",
                    """
                    Hello {displayName},

                    {tripTitle} is on {tripDate}. What has been arranged for it is here:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Se apropie {tripTitle}",
                    """
                    Bună ziua {displayName},

                    {tripTitle} are loc în data de {tripDate}. Ce s-a stabilit pentru ea găsiți aici:

                    {siteUrl}{url}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyTripCalloutOverdue,
            MessageChannel.Email,
            "A party is past the time they said they would be back, and nobody has stood the alarm down.",
            [AppName, DisplayName, "tripTitle", "tripDate", "expectedReturn", SiteUrl, "url"],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{tripTitle} is overdue",
                    """
                    Hello {displayName},

                    {tripTitle} on {tripDate} was due back by {expectedReturn}, and nobody has said
                    it is out. You are being told because the trip names you.

                    If you know the party is safe, say so here — anyone the trip names can:

                    {siteUrl}{url}

                    If nobody can reach them, the trip records what was arranged for this.
                    """),
                ["ro"] = new(
                    "{tripTitle} a depășit ora de întoarcere",
                    """
                    Bună ziua {displayName},

                    {tripTitle} din data de {tripDate} trebuia să se încheie până la
                    {expectedReturn}, iar nimeni nu a confirmat că echipa a ieșit. Primiți acest
                    mesaj pentru că tura vă numește.

                    Dacă știți că echipa este în siguranță, confirmați aici — o poate face oricine
                    este numit pe tură:

                    {siteUrl}{url}

                    Dacă nimeni nu îi poate contacta, tura consemnează ce s-a stabilit pentru acest caz.
                    """),
            }),

        new(
            NotifyTripInviteeCannotOpenCave,
            MessageChannel.Email,
            "Someone asked on a trip cannot open a cave the trip is about.",
            [AppName, DisplayName, ActorName, "inviteeName", "caveName", SiteUrl, "url", UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "{inviteeName} cannot open {caveName}",
                    """
                    Hello {displayName},

                    {actorName} asked {inviteeName} on a trip to {caveName}, which {inviteeName} has
                    no access to. Being asked on a trip grants none: only somebody who may change
                    that cave's permissions can.

                    {siteUrl}{url}

                    This message goes to the cave's owner and to full administrators, and to nobody
                    else — somebody who may grant access to it in another way, through a club for
                    instance, has not been told. Please pass it on if it is not yours to act on.

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "{inviteeName} nu are acces la {caveName}",
                    """
                    Bună ziua {displayName},

                    {actorName} a invitat pe {inviteeName} la o tură în {caveName}, la care
                    {inviteeName} nu are acces. Invitația la o tură nu acordă acces: numai cineva
                    care poate schimba permisiunile peșterii poate face asta.

                    {siteUrl}{url}

                    Acest mesaj ajunge la proprietarul peșterii și la administratorii deplini, și la
                    nimeni altcineva — cineva care poate acorda acces altfel, printr-un club de
                    exemplu, nu a fost înștiințat. Vă rugăm să-l transmiteți mai departe dacă nu vă
                    revine dumneavoastră.

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyJobCompleted,
            MessageChannel.Email,
            "A background task someone started finished successfully.",
            [AppName, DisplayName, SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Your {appName} upload has finished processing",
                    """
                    Hello {displayName},

                    A task you started has finished processing and the result is ready:

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Sarcina dumneavoastră {appName} s-a încheiat",
                    """
                    Bună ziua {displayName},

                    O sarcină pe care ați pornit-o s-a încheiat, iar rezultatul este gata:

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
            }),

        new(
            NotifyJobFailed,
            MessageChannel.Email,
            "A background task someone started could not be completed.",
            [AppName, DisplayName, "error", SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Your {appName} upload could not be processed",
                    """
                    Hello {displayName},

                    A task you started could not be completed:

                    {error}

                    You can try again from {siteUrl} — if it keeps failing, tell an administrator.

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Sarcina dumneavoastră {appName} nu a putut fi finalizată",
                    """
                    Bună ziua {displayName},

                    O sarcină pe care ați pornit-o nu a putut fi finalizată:

                    {error}

                    Puteți încerca din nou de la {siteUrl} — dacă eșuează în continuare, anunțați un
                    administrator.

                    {unsubscribeUrl}
                    """),
            }),

        // The three security alerts carry no unsubscribe link: the settings page refuses to switch
        // this category off, so offering a link that cannot work would be a lie.
        new(
            NotifySecurityPasswordChanged,
            MessageChannel.Email,
            "Warns an account holder that the password on their account was changed.",
            [AppName, DisplayName, SiteUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "The password on your {appName} account was changed",
                    """
                    Hello {displayName},

                    The password on your {appName} account has just been changed.

                    If that was you, nothing more is needed. If it was not, someone else has access
                    to your account — reset your password immediately and tell an administrator:

                    {siteUrl}
                    """),
                ["ro"] = new(
                    "Parola contului dumneavoastră {appName} a fost schimbată",
                    """
                    Bună ziua {displayName},

                    Parola contului dumneavoastră {appName} tocmai a fost schimbată.

                    Dacă dumneavoastră ați făcut acest lucru, nu mai este nimic de făcut. Dacă nu,
                    altcineva are acces la contul dumneavoastră — resetați parola imediat și anunțați
                    un administrator:

                    {siteUrl}
                    """),
            }),

        new(
            NotifySecurityEmailChanged,
            MessageChannel.Email,
            "Warns the address an account is moving away from. Sent to the old address, not the new one.",
            [AppName, DisplayName, "newEmail", SiteUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "The address on your {appName} account was changed",
                    """
                    Hello {displayName},

                    The address on your {appName} account has been changed to {newEmail}. This
                    message is the last one this address will receive.

                    If that was not you, someone else has access to your account — tell an
                    administrator now, because you can no longer reset the password yourself:

                    {siteUrl}
                    """),
                ["ro"] = new(
                    "Adresa contului dumneavoastră {appName} a fost schimbată",
                    """
                    Bună ziua {displayName},

                    Adresa contului dumneavoastră {appName} a fost schimbată în {newEmail}. Acesta
                    este ultimul mesaj primit la această adresă.

                    Dacă nu dumneavoastră ați făcut acest lucru, altcineva are acces la contul
                    dumneavoastră — anunțați imediat un administrator, deoarece nu mai puteți reseta
                    singur parola:

                    {siteUrl}
                    """),
            }),

        new(
            NotifySecurityTwoFactorDisabled,
            MessageChannel.Email,
            "Warns an account holder that two-factor sign-in was switched off on their account.",
            [AppName, DisplayName, SiteUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Two-factor sign-in was switched off on your {appName} account",
                    """
                    Hello {displayName},

                    Two-factor sign-in has been switched off on your {appName} account. Your password
                    is now the only thing protecting it.

                    If that was not you, switch it back on and change your password:

                    {siteUrl}
                    """),
                ["ro"] = new(
                    "Autentificarea în doi pași a fost dezactivată pe contul {appName}",
                    """
                    Bună ziua {displayName},

                    Autentificarea în doi pași a fost dezactivată pe contul dumneavoastră {appName}.
                    Parola este acum singurul lucru care îl protejează.

                    Dacă nu dumneavoastră ați făcut acest lucru, reactivați-o și schimbați-vă parola:

                    {siteUrl}
                    """),
            }),

        new(
            NotifyDigest,
            MessageChannel.Email,
            "The daily summary, for people who asked for one message a day rather than each as it happens.",
            [AppName, DisplayName, "itemCount", "items", SiteUrl, UnsubscribeUrl],
            new Dictionary<string, MessageTemplateText>
            {
                ["en"] = new(
                    "Your {appName} summary: {itemCount} update(s)",
                    """
                    Hello {displayName},

                    Here is what happened on {appName} since your last summary:

                    {items}

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
                ["ro"] = new(
                    "Rezumatul dumneavoastră {appName}: {itemCount} noutăți",
                    """
                    Bună ziua {displayName},

                    Iată ce s-a întâmplat pe {appName} de la ultimul rezumat:

                    {items}

                    {siteUrl}

                    {unsubscribeUrl}
                    """),
            }),
    ];

    public static MessageTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>The shipped wording, falling back to English for a locale a template lacks.</summary>
    public static MessageTemplateText Default(MessageTemplateDefinition definition, string locale) =>
        definition.Defaults.TryGetValue(Normalise(locale), out var text)
            ? text
            : definition.Defaults[FallbackLocale];

    /// <summary>
    /// Reduces "ro-RO" to "ro" and anything unrecognised to English, so a user whose locale
    /// drifted (or arrived from a provider claim) still receives readable mail.
    /// </summary>
    public static string Normalise(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return FallbackLocale;
        }

        var primary = locale.Split('-')[0].Trim().ToLowerInvariant();
        return Locales.Contains(primary) ? primary : FallbackLocale;
    }
}
