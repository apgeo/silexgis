// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { SelectOutlined } from '@ant-design/icons';
import { App } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Button from '../Button.tsx';
import { fetchWorkingSetRows, type WorkingSetList, type WorkingSetQuery } from '../../api/hooks/workingSet.ts';
import { mapAddressForList } from '../../filters/tableMapHandoff.ts';
import { resultQuestion, takeIntoWorkingSet } from '../../filters/workingSet.ts';
import { MAX_SELECTION_SET, useWorkspaceStore } from '../../stores/workspaceStore.ts';

interface SelectResultButtonProps {
  list: WorkingSetList;
  /** What the list's own request sends, so the set holds what the list shows. */
  query: WorkingSetQuery;
  /** The list's count for this reader; the button is off when there is nothing to take. */
  total: number | undefined;
  'data-testid'?: string;
}

/**
 * "Select all N on the map": the list's result becomes the session's working set, and the map
 * opens on the list's filter with it.
 *
 * The set is capped, and the page says so with both numbers when the cap bit — a working set that
 * silently held the first two hundred of twelve hundred would make every bulk action afterwards a
 * quiet lie about "all". The result is read again through the list's own route rather than taken
 * from the rows on screen, which are only one page.
 */
export default function SelectResultButton({
  list,
  query,
  total,
  'data-testid': testId,
}: SelectResultButtonProps) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const count = total ?? 0;

  const take = async () => {
    setBusy(true);
    try {
      const { rows, total: found } = await fetchWorkingSetRows(list, query, MAX_SELECTION_SET);
      const result = takeIntoWorkingSet(rows, found, MAX_SELECTION_SET);
      // A result cut to the map's window cannot be saved as a filter: a saved filter keeps the
      // question and never a place, so the window would fall away and the filter would find more.
      // Nor can one whose question names a point or an outline, and the place a nearest-first
      // list was measured from goes with neither the saved question nor the map's.
      const question = resultQuestion(query.filter, query.bbox !== undefined);
      useWorkspaceStore.getState().setSelectionSetFromResult(result.refs, {
        document: question.savable,
        total: result.total,
      });
      message.info(
        result.truncated
          ? t('workingSet.tookFirst', {
              taken: result.taken.toLocaleString(i18n.language),
              total: result.total.toLocaleString(i18n.language),
            })
          : t('workingSet.tookAll', { count: result.taken }),
      );
      navigate(mapAddressForList(question.mapToken));
    } catch {
      message.error(t('workingSet.failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Button
      icon={<SelectOutlined />}
      data-testid={testId}
      disabled={count === 0}
      loading={busy}
      onClick={() => void take()}
    >
      {t('workingSet.selectAll', { count })}
    </Button>
  );
}
