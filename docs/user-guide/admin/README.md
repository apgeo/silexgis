# Administration

[← Back to the guide](../README.md)

---

Pages for whoever holds administrative rights on the installation. These are still *user*
documentation — how to operate the application, not how to install it.

For installing, upgrading, backing up and TLS, see [INSTALL.md](../../INSTALL.md) and
[DEPLOY-SERVER.md](../../DEPLOY-SERVER.md).

| Page | Covers |
|---|---|
| [Permissions explained](permissions.md) | Permission groups, rules, scopes, per-object grants, "why can this person see that?" |
| [Location protection](location-protection.md) | The protection mechanism end to end, and every path it covers |
| [Vocabularies your club owns](vocabularies.md) | Document kinds, trip purposes, trip roles, report layouts, link relations, detection rules, message texts, feature sets |
| [Messaging and delivery](messaging.md) | Email, SMS, message texts, sign-in policy, watching the mail go out |
| [Building terrain](terrain-builds.md) | Turning elevation data into the ground the 3D scene draws |

---

## "Administrator" is not a rank

There is no single admin role. **Each administration page follows its own domain**, and a
person's rights happen to include some of them and not others.

That means your rail may show *Audit* but not *Messaging*, or *Terrain* but not *Permission
groups*. That is working correctly.

The one genuine exception is **Full Administrators**: membership itself is the grant, decided
before any rule is consulted. That group holds no rules, and the application refuses any
change that would leave no active Full Administrator able to sign in.

## The rights each administration page needs

| Page | Needs |
|---|---|
| Audit | Read on the **audit** domain |
| Permission groups | Rights over the **permissionGroups** domain |
| Messaging, Message delivery | Rights over **settings** |
| Message texts | Rights over **messageTemplates** |
| Terrain | Read on **terrain** (starting a build is a separate right) |
| Feature sets | Rights over **featureSets** |
| Document kinds, trip purposes, trip roles, report layouts | **Write** on taxonomies — because authoring a kind's schema decides what every record of that kind may say |
| Link relations | Full Administrators |
| Detection rules | Anybody who can create features. Only *promoting* a set to what a group or the installation inherits is an administrator's act |

## Things worth setting up early

1. **Permission groups** — before there is data to get wrong.
2. **Caving groups** and the caver roster — most other things reference them.
3. **Document kinds** and **trip purposes** — because they decide what every document and
   report of that kind is asked for, and changing them later is easier than un-filing a
   thousand documents.
4. **Messaging** — or accept that links and codes go to the server log instead of being sent.
5. **Your club's rule on when a location is protected** — a policy decision, not a setting.
