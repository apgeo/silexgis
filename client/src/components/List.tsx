// SPDX-License-Identifier: AGPL-3.0-or-later
import { Fragment, type CSSProperties, type HTMLAttributes, type ReactNode } from 'react';
import { Empty, Flex, Spin, theme } from 'antd';
import './List.css';

/**
 * A vertical list of records: rows separated by a hairline, each with an optional icon, a
 * title, a line of secondary detail, and controls pinned to the right.
 *
 * This exists because the component library deprecated its own list and offers nothing in its
 * place, so the choice was between a bespoke arrangement at each of the dozen places one is
 * drawn and one component that draws them all. The shape below is deliberately only what those
 * places actually ask for — no pagination, no grid, no header, no footer — because the point
 * was to stop depending on a deprecated component, not to reimplement it.
 *
 * The prop names match the ones they replace, so a caller changes its import and nothing else.
 * That is not an accident: a prop added later should take the name the library used for the
 * same thing, so that reading a call site never requires knowing which list it is.
 */

interface ListProps<T> {
  /**
   * The records to draw. Undefined is a list whose rows have not arrived yet and draws as an
   * empty one — most callers hand this straight from a query, where "no rows" and "not yet"
   * are the same absent value, and the component this replaces treated them the same way.
   */
  dataSource: readonly T[] | undefined;
  renderItem: (item: T, index: number) => ReactNode;
  /** Tighter rows, for a list inside a card or a panel rather than one that is the page. */
  size?: 'default' | 'small';
  /** Covers the rows while they are being fetched, exactly as the replaced component did. */
  loading?: boolean;
  /**
   * What stands in for the rows when there are none. Omitted, an empty list says so in the
   * interface language; a caller with nothing worth saying passes a blank string to say
   * nothing at all, which is the only reason this takes a node rather than a message.
   */
  locale?: { emptyText?: ReactNode };
  style?: CSSProperties;
}

function ListRoot<T>({ dataSource, renderItem, size, loading, locale, style }: ListProps<T>) {
  const { token } = theme.useToken();
  const records = dataSource ?? [];

  const rows =
    records.length > 0 ? (
      <ul
        className="silex-list"
        data-size={size ?? 'default'}
        // The hairline between rows is drawn in the stylesheet, because only a stylesheet can
        // say "every row but the last"; its colour is the theme's, so it follows light/dark.
        style={{ '--silex-list-split': token.colorSplit } as CSSProperties}
      >
        {dataSource.map((item, index) => (
          // Keyed by position: no caller passes a row identity, and every one of them renders
          // a whole list from a single query result, so a row's position is exactly as stable
          // as the list it came from.
          <Fragment key={index}>{renderItem(item, index)}</Fragment>
        ))}
      </ul>
    ) : (
      (locale?.emptyText ?? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />)
    );

  return <div style={style}>{loading ? <Spin>{rows}</Spin> : rows}</div>;
}

interface ListItemProps extends Omit<HTMLAttributes<HTMLLIElement>, 'title'> {
  /** Controls for this row, laid out from the right edge in the order given. */
  actions?: ReactNode[];
  children?: ReactNode;
}

/**
 * One row. Anything not named here lands on the row element itself, which is how a caller
 * marks a row for a test to find, and why this is the element a click handler belongs on.
 */
function ListItem({ actions, children, className, ...rest }: ListItemProps) {
  return (
    <li className={className ? `silex-list-item ${className}` : 'silex-list-item'} {...rest}>
      <div className="silex-list-item-body">{children}</div>
      {actions && actions.length > 0 && (
        <Flex align="center" gap={8} className="silex-list-item-actions">
          {actions}
        </Flex>
      )}
    </li>
  );
}

interface ListItemMetaProps {
  avatar?: ReactNode;
  title?: ReactNode;
  description?: ReactNode;
}

/** The usual contents of a row: an icon, what the row is, and a quieter line about it. */
function ListItemMeta({ avatar, title, description }: ListItemMetaProps) {
  const { token } = theme.useToken();
  return (
    <Flex align="flex-start" gap={12}>
      {avatar !== undefined && avatar !== null && avatar !== false && (
        <div className="silex-list-item-avatar">{avatar}</div>
      )}
      <Flex vertical gap={2} style={{ minWidth: 0 }}>
        {title !== undefined && title !== null && <div>{title}</div>}
        {description !== undefined && description !== null && (
          <div style={{ color: token.colorTextDescription, fontSize: token.fontSizeSM }}>
            {description}
          </div>
        )}
      </Flex>
    </Flex>
  );
}

/**
 * Reached as `List.Item` and `List.Item.Meta`, the way the component this replaces was, so the
 * call sites read the same.
 */
const List = Object.assign(ListRoot, {
  Item: Object.assign(ListItem, { Meta: ListItemMeta }),
});

export default List;
