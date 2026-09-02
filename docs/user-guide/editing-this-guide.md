# Editing this guide

🇬🇧 **English** · 🇷🇴 [Română](ro/editing-this-guide.md)

[← Back to the guide](README.md)

---

This guide is meant to be edited — by people, and by an AI agent working in this repository.
This page is the contract between the two, so that neither undoes the other's work.

---

## Where it lives

```
docs/user-guide/
├── README.md                  the front page and full table of contents
├── concept.md                 what the application is
├── first-steps.md             getting oriented
├── core-concepts.md           the six ideas everything is built on
├── glossary.md                the vocabulary
├── editing-this-guide.md      this page
├── workflows/                 whole jobs, start to finish
├── features/                  one page per part of the application
├── admin/                     for whoever holds administrative rights
└── ro/                        the Romanian guide — identical file names
    ├── TERMINOLOGIE.md        the EN↔RO term map
    ├── workflows/ features/ admin/
    └── …
```

**Plain Markdown, no build step, no site generator.** It renders on GitHub, in any Markdown
viewer, in an editor, and it can be pasted into a wiki. Links are relative, so the tree can be
moved or published as-is.

## The two languages

The guide ships in **English and Romanian**, matching the application itself. Every page exists
in both, under the same file name, and each carries a language switcher under its title.

**English is canonical.** When something changes, it changes in English first and in Romanian
second. A Romanian page that has fallen behind is a defect, not a variant — say so plainly rather
than letting the two drift.

**Romanian pages use the exact words the interface shows**, collected in
[`ro/TERMINOLOGIE.md`](ro/TERMINOLOGIE.md) and extracted from the application's own translation
file. A guide that says *dosare* where the screen says *dulapuri* helps nobody. If you add a term,
add it there too.

Note the path depth: from `ro/features/x.md`, anything outside `docs/user-guide/` needs one extra
`../` compared with the English page.

## What each directory is for

| Directory | Answers |
|---|---|
| Top level | *What is this and how do I get started?* |
| `workflows/` | *How do I do this whole job?* — narrative, ordered steps, links out for detail |
| `features/` | *What does this screen do?* — reference, complete, browsable |
| `admin/` | *How do I run this installation?* — still user-facing, not deployment |

If you are unsure where something belongs: **if it has an order, it is a workflow; if it has a
surface, it is a feature page.**

---

## Conventions

**Every page starts with a breadcrumb line** — a link back to its section index, and usually
to related pages.

**Every page ends with links onward.** Nothing should be a dead end.

**Use relative links.** From the top level, `features/trips.md`; from within `features/`,
just `trips.md`; from a sibling directory, `../features/trips.md`.

**Tables for anything enumerable.** Field lists, states, options, error meanings. They are far
easier to scan and to keep correct than prose.

**Quote the application's own words** when it says something well, in a blockquote. Much of
this application's interface text is itself good documentation, and quoting it means the guide
and the screen agree.

**Say what does *not* happen.** A large part of what makes this application predictable is what
it refuses to do. Sections titled *What X never does* are a deliberate pattern.

**No implementation detail** unless it changes what the reader should do. No file paths into
`server/` or `client/`, no API endpoints, no database tables, no code.

**British spelling**, matching the application's own interface text (*organise*, *licence*
as a noun, *metre*).

---

## Working with an AI agent on this guide

Ask for what you want in ordinary words. Some patterns that work well:

> *"Add a page under `features/` for the new X screen, following the conventions in
> `docs/user-guide/editing-this-guide.md`, and link it from the features index and the guide
> home page."*

> *"Re-read `client/src/i18n/locales/en.json` and check the Trips page against what the
> interface actually says now."*

> *"The workflow for Y is out of date — walk the actual screens and correct it."*

### Things to tell an agent explicitly

- **Update the indexes.** A new page must be linked from at least `README.md` of its directory
  and, if it is significant, from the guide home page. An unlinked page is an invisible page.
- **Do the Romanian too**, or say clearly that you did not. Use the terminology map.
- **Add glossary entries** for any new term.
- **Do not invent behaviour.** If the application's behaviour is unclear, the honest move is a
  short paragraph saying what is unknown, not a confident guess. This guide's value is that it
  is trustworthy.
- **Check the interface text** (`client/src/i18n/locales/en.json`) rather than the
  specification, when the two might differ. The specification describes intent; the interface
  text describes what a user actually reads.

### A good source order for an agent

1. `client/src/i18n/locales/en.json` and `ro.json` — what the interface actually says
2. `client/src/App.tsx` and `client/src/components/navItems.tsx` — what pages exist and how
   they are grouped
3. The repository `README.md` — the feature narrative, in the application's own voice
4. `.claude/docs/` — the specification, for intent and for anything the interface does not
   spell out

---

## Keeping it honest

Two failure modes to watch for as this grows:

**Drift.** A screen changes and the page describing it does not. The cheapest defence is to
correct a page the moment you notice, rather than filing it as a task.

**Confident wrongness.** A page that is 90% right and 10% invented is worse than a page that
says *"this is not documented yet"*, because a reader cannot tell which 10%. Prefer a gap to a
guess, and mark gaps plainly:

```markdown
> **Not yet documented.** [What is missing, and who would know.]
```

---

## What is not in this guide

| Topic | Where |
|---|---|
| Installing, upgrading, backups, TLS | [INSTALL.md](../INSTALL.md) |
| Deploying to a server | [DEPLOY-SERVER.md](../DEPLOY-SERVER.md) |
| The phone sync wire contract | [`docs/speleoloc-sync/`](../speleoloc-sync/) |
| Anything about the code | Not here. This guide has no developer content by design |

---

[← Back to the guide](README.md)
