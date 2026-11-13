// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FlybyCaption, FlybyChange, FlybyDocument, FlybySection, FlybySubject } from './flybyDocument.ts';

// A flyby about the caves a saved filter selects, written out for the caves it selects today.
//
// The document says "the caves of this filter, at most twelve" and "for each of them: hop over,
// circle, stop". Which caves those are is a question for the server, asked when the flyby is
// prepared and answered under the rights of whoever plays it — never written into the document,
// which would then disclose them to whoever is handed the file, and would go on flying to a cave
// the filter no longer keeps. Once the answer is in, the document is written out here as a plain
// one: each of the filter's caves becomes a subject of its own, and every run of sections marked
// `each` is repeated once per cave, with the changes and captions tied to those sections repeated
// with it. Everything after — finding the subjects, the path, the look — then works on an ordinary
// document and never needs to know a filter was involved.

/** The caves each subject drawn from a saved filter stands for now, by the subject's position. */
export type FlybyFilterCaves = ReadonlyMap<number, readonly string[]>;

export interface FlybyExpanded {
  /** The document with every filter's caves written out; a document without a filter comes back as it was. */
  document: FlybyDocument;
  /** For each section of the written-out document, the position of the section it was copied from. */
  sectionOf: number[];
  /** For each subject drawn from a filter, where its caves are among the written-out subjects. */
  groups: Map<number, number[]>;
}

/** The positions of the document's subjects that are drawn from saved filters. */
export function filterSubjects(document: Pick<FlybyDocument, 'subjects'>): number[] {
  return document.subjects.flatMap((subject, index) => (subject.kind === 'filter' ? [index] : []));
}

/** Copied once for each cave: a written-out id is the original's with the cave's place in its run. */
function copyId(id: string, ordinal: number): string {
  return `${id}~${ordinal + 1}`;
}

/**
 * The runs of sections to be flown for each cave: consecutive sections marked `each` and about
 * the same subject drawn from a filter, as [first, last] positions.
 */
function eachRuns(document: FlybyDocument): { from: number; to: number; subject: number }[] {
  const runs: { from: number; to: number; subject: number }[] = [];
  document.sections.forEach((section, index) => {
    const subject = section.subject;
    if (!section.each || subject === 'all' || document.subjects[subject]?.kind !== 'filter') {
      return;
    }
    const last = runs[runs.length - 1];
    if (last && last.to === index - 1 && last.subject === subject) {
      last.to = index;
    } else {
      runs.push({ from: index, to: index, subject });
    }
  });
  return runs;
}

/**
 * The document written out for the caves its filters stand for. A filter subject stays where it
 * was, standing for all of its caves together — what a section about it as a whole, or the opening,
 * looks at — and its caves are appended as subjects of their own.
 */
export function expandFlyby(document: FlybyDocument, caves: FlybyFilterCaves): FlybyExpanded {
  const filters = filterSubjects(document);
  if (filters.length === 0) {
    return { document, sectionOf: document.sections.map((_, index) => index), groups: new Map() };
  }
  const subjects: FlybySubject[] = [...document.subjects];
  const groups = new Map<number, number[]>();
  for (const index of filters) {
    const found = caves.get(index) ?? [];
    groups.set(
      index,
      found.map((caveId) => subjects.push({ kind: 'cave', caveId }) - 1),
    );
  }

  const runs = eachRuns(document);
  const sections: FlybySection[] = [];
  const sectionOf: number[] = [];
  /** For a section copied per cave: the copies' ids and the cave each is about, in order. */
  const copies = new Map<string, { id: string; subject: number }[]>();
  let index = 0;
  while (index < document.sections.length) {
    const run = runs.find((candidate) => candidate.from === index);
    if (!run) {
      sections.push(document.sections[index]);
      sectionOf.push(index);
      index += 1;
      continue;
    }
    const members = groups.get(run.subject) ?? [];
    members.forEach((subject, ordinal) => {
      for (let at = run.from; at <= run.to; at += 1) {
        const original = document.sections[at];
        const id = copyId(original.id, ordinal);
        const copy: FlybySection = { ...original, id, subject };
        delete copy.each;
        sections.push(copy);
        sectionOf.push(at);
        copies.set(original.id, [...(copies.get(original.id) ?? []), { id, subject }]);
      }
    });
    index = run.to + 1;
  }

  // A change or a caption tied to a section flown for each cave is tied to every copy of it.
  const changes: FlybyChange[] = (document.changes ?? []).flatMap((change) => {
    const copied = copies.get(change.section);
    return copied ? copied.map((copy, ordinal) => ({ ...change, id: copyId(change.id, ordinal), section: copy.id })) : [change];
  });
  const captions: FlybyCaption[] = (document.texts?.captions ?? []).flatMap((caption) => {
    const copied = copies.get(caption.section);
    return copied ? copied.map((copy) => ({ ...caption, section: copy.id })) : [caption];
  });

  const expanded: FlybyDocument = { ...document, subjects, sections };
  if (document.changes) {
    expanded.changes = changes;
  }
  if (document.texts) {
    expanded.texts = { ...document.texts, ...(document.texts.captions ? { captions } : {}) };
  }
  return { document: expanded, sectionOf, groups };
}
