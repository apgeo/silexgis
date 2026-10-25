// SPDX-License-Identifier: AGPL-3.0-or-later
import { LockOutlined, UndoOutlined } from '@ant-design/icons';
import { Flex, Popconfirm, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useBatchHistory, type ImportBatchUndoItem } from '../../api/hooks.ts';
import { formatValue, humanize, isGeometrySummary, toWriteField } from '../../components/history/historyModel.ts';
import { batchChanges } from './batchChanges.ts';

/** The words for a changed field: its translated label, or a readable form of its name. */
function useFieldLabel() {
  const { t } = useTranslation();
  return (prop: string) => t(`history.field.${toWriteField(prop)}`, { defaultValue: humanize(prop) });
}

/**
 * What a phone upload did to one row, as the row's own history records it: each field it changed
 * with the value before and after.
 *
 * Which fields an ordinary undo would leave because somebody changed them again is the server's
 * to say, by the same rule the undo applies: the result of an undo names them, and "Undo anyway"
 * is offered for any open line that changed fields.
 *
 * The history is read when the line is opened, not for the whole batch: a batch can hold
 * hundreds of lines, and most are never looked at. The server narrows it to the upload's own
 * events, so a busy row's later history cannot push them out of the page.
 */
export function UploadRowChanges({
  batchId,
  rowId,
  created,
  undone,
  onUndoAnyway,
}: {
  batchId: string;
  /** The row the line changed, or created. */
  rowId: string;
  created: boolean;
  undone: boolean;
  /** Puts this line back past the fields changed since; offered while the line is still open. */
  onUndoAnyway?: () => void;
}) {
  const { t } = useTranslation();
  const label = useFieldLabel();
  const history = useBatchHistory(rowId, batchId);

  if (history.isLoading) {
    return <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>;
  }
  const entries = batchChanges(history.data?.items ?? [], batchId, rowId);
  if (entries.length === 0) {
    return (
      <Typography.Text type="secondary" data-testid="upload-row-nothing">
        {created ? t('vectorImport.upload.createdRow') : t('vectorImport.upload.noRecordedChange')}
      </Typography.Text>
    );
  }

  const shown = (value: unknown) =>
    isGeometrySummary(value) ? t('history.geometrySummary', { points: value.points }) : formatValue(value);

  const changedFields = entries.some((entry) => entry.action === 'updated' && entry.fields.length > 0);

  return (
    <Flex vertical gap={6} data-testid="upload-row-changes">
      {entries.map((entry) => (
        <div key={entry.eventId}>
          {entry.action !== 'updated' && (
            <Tag>{t(`history.action.${entry.action}`, { defaultValue: entry.action })}</Tag>
          )}
          {entry.fields.map((field) => (
            <div key={field.prop} data-testid="upload-row-field">
              <Typography.Text type="secondary">{label(field.prop)}: </Typography.Text>
              {field.redacted ? (
                <Typography.Text type="secondary">
                  <LockOutlined /> {t('history.hidden')}
                </Typography.Text>
              ) : (
                <>
                  <Typography.Text delete type="secondary">{shown(field.old)}</Typography.Text>
                  {' → '}
                  <Typography.Text>{shown(field.new)}</Typography.Text>
                </>
              )}
            </div>
          ))}
        </div>
      ))}
      {changedFields && !undone && onUndoAnyway && (
        <div>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }} data-testid="upload-row-superseded-hint">
            {t('vectorImport.upload.supersededHint')}
          </Typography.Paragraph>
          <Popconfirm
            title={t('vectorImport.upload.undoAnywayRowConfirm')}
            description={t('vectorImport.upload.undoAnywayExplain')}
            onConfirm={onUndoAnyway}
            okButtonProps={{ danger: true }}
          >
            <Button size="small" icon={<UndoOutlined />} data-testid="upload-row-undo-anyway">
              {t('vectorImport.upload.undoAnyway')}
            </Button>
          </Popconfirm>
        </div>
      )}
    </Flex>
  );
}

const OUTCOME_COLOR: Record<string, string> = { undone: 'green', partly: 'gold', not_undone: 'red' };

/**
 * What the last undo did, line by line: how each line ended, why a line was left, and which of a
 * line's fields were put back and which were not, with the reason for each.
 */
export function UndoOutcome({
  lines,
  names,
}: {
  lines: ImportBatchUndoItem[];
  /** The name each line shows in the table, by line id. */
  names: Map<number, string>;
}) {
  const { t } = useTranslation();
  const label = useFieldLabel();
  return (
    <Flex vertical gap={8} data-testid="upload-undo-result">
      {lines.map((line) => (
        <div key={line.itemId} data-testid="upload-undo-line">
          <Tag color={OUTCOME_COLOR[line.outcome]}>{t(`vectorImport.upload.outcome.${line.outcome}`, { defaultValue: line.outcome })}</Tag>
          <Typography.Text strong>{names.get(line.itemId) ?? t('vectorImport.unnamed')}</Typography.Text>
          {line.reason && (
            <Typography.Text type="secondary">
              {' — '}
              {t(`vectorImport.upload.reason.${line.reason}`, { defaultValue: line.reason })}
            </Typography.Text>
          )}
          {line.skipped.length > 0 && (
            <ul style={{ margin: '4px 0 0', paddingInlineStart: 20 }}>
              {line.skipped.map((s) => (
                <li key={s.field}>
                  {t('vectorImport.upload.fieldLeft', {
                    field: label(s.field),
                    reason: t(`history.skipReason.${s.reason}`, { defaultValue: s.reason }),
                  })}
                </li>
              ))}
            </ul>
          )}
        </div>
      ))}
    </Flex>
  );
}
