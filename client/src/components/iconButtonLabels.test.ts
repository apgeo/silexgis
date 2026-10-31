// SPDX-License-Identifier: AGPL-3.0-or-later
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

/**
 * Every button that shows an icon and no words must be given a label of its own.
 *
 * The icon set draws each glyph as an image named after itself (`delete`, `aim`), in English
 * whatever the interface language, so an unlabelled icon button is announced by the glyph's name.
 * `components/Button.tsx` reports one at run time, and the unit tests fail on that report — but
 * only for what a test renders. This reads every source file, so a button on a screen no test
 * opens is held to the same rule.
 */
const sourceText = import.meta.glob('../**/*.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

function unlabelledIconButtons(file: string, text: string): string[] {
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const found: string[] = [];
  const visit = (node: ts.Node): void => {
    const opening = ts.isJsxSelfClosingElement(node)
      ? node
      : ts.isJsxElement(node)
        ? node.openingElement
        : null;
    if (opening !== null && opening.tagName.getText() === 'Button') {
      const attributes = opening.attributes.properties;
      const named = (name: string) =>
        attributes.some((a) => ts.isJsxAttribute(a) && a.name.getText() === name);
      const children = ts.isJsxElement(node)
        ? node.children.filter((child) => !(ts.isJsxText(child) && child.text.trim() === ''))
        : [];
      // `{count > 1 ? `v${count}` : null}` is words some of the time: when it is not, the icon is
      // all there is, so a button whose only content is an expression that can be empty counts too.
      const maybeEmpty =
        children.length > 0 &&
        children.every((child) => ts.isJsxExpression(child) && /\b(null|false|undefined)\b|''/.test(child.getText()));
      if (
        named('icon') &&
        (children.length === 0 || maybeEmpty) &&
        !named('aria-label') &&
        !named('aria-labelledby')
      ) {
        const { line } = source.getLineAndCharacterOfPosition(opening.getStart());
        found.push(`${file.replace('../', '')}:${line + 1}`);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return found;
}

describe('icon-only buttons', () => {
  const files = Object.entries(sourceText).filter(([file]) => !file.includes('.test.') && !file.startsWith('../api/'));

  it('reads the application (so the scan cannot silently go blind)', () => {
    expect(files.length).toBeGreaterThan(300);
  });

  it('all carry an aria-label or aria-labelledby of their own', () => {
    const unlabelled = files.flatMap(([file, text]) => unlabelledIconButtons(file, text));
    expect(
      unlabelled,
      'An icon-only Button needs aria-label={t(…)} (EN and RO keys together): the icon is announced by its English glyph name otherwise.',
    ).toEqual([]);
  });

  it('finds a button that has no label (the scan can fail)', () => {
    const bare = `export const a = () => <Button size="small" icon={<DeleteOutlined />} />;`;
    const labelled = `export const a = () => <Button icon={<DeleteOutlined />} aria-label="Delete" />;`;
    const worded = `export const a = () => <Button icon={<DeleteOutlined />}>Delete</Button>;`;
    expect(unlabelledIconButtons('x.tsx', bare)).toHaveLength(1);
    expect(unlabelledIconButtons('x.tsx', labelled)).toHaveLength(0);
    expect(unlabelledIconButtons('x.tsx', worded)).toHaveLength(0);
    const sometimes = `export const a = () => <Button icon={<HistoryOutlined />}>{n > 1 ? 'v' + n : null}</Button>;`;
    expect(unlabelledIconButtons('x.tsx', sometimes)).toHaveLength(1);
  });
});
