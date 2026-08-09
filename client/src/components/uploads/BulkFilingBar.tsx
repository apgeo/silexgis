// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Button, Flex, Modal, Select, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useBulkFiling, type CabinetInfo } from '../../api/hooks.ts';

export interface BulkFilingBarProps {
  /** The documents the table has selected. */
  documentIds: string[];
  cabinets: CabinetInfo[];
  /** The shelf being looked at, when there is one — the source of a move. */
  currentCabinetId?: string;
  onDone: () => void;
}

/**
 * Filing many documents at once.
 *
 * <p>
 * Copy and move are the same request with a different second half, because filing is
 * many-to-many: adding a shelf is a copy and costs nothing, and a move is that plus taking the
 * old one away. Doing both in one request is what keeps a move from half-succeeding and
 * leaving an archive on both shelves or on neither.
 * </p>
 * <p>
 * The answer is a partial result and is reported as one. A selection of two hundred documents
 * will, on a real archive, contain one somebody else owns — and refusing the whole thing for
 * it would make this unusable on exactly the archives it exists for.
 * </p>
 */
export default function BulkFilingBar({
  documentIds,
  cabinets,
  currentCabinetId,
  onDone,
}: BulkFilingBarProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const refile = useBulkFiling();

  const [target, setTarget] = useState<string>();
  const [mode, setMode] = useState<'copy' | 'move' | null>(null);

  const submit = async () => {
    if (!target || !mode) {
      return;
    }

    try {
      const result = await refile.mutateAsync({
        documentIds,
        fileIntoCabinetIds: [target],
        // A move only has a source when a shelf is being looked at; from the inbox there is
        // nothing to take the document off, so it is always a copy.
        unfileFromCabinetIds: mode === 'move' && currentCabinetId ? [currentCabinetId] : undefined,
      });

      const refused = Object.keys(result.refused).length;
      const missing = documentIds.length - result.filed.length - refused;

      if (refused === 0 && missing === 0) {
        message.success(t('cabinets.bulkFiled', { count: result.filed.length }));
      } else {
        // Named rather than counted where it can be: "3 could not be moved" is a thing
        // somebody can act on only if they are told which.
        message.warning(
          t('cabinets.bulkPartly', {
            filed: result.filed.length,
            refused: refused + missing,
          }),
        );
      }

      setMode(null);
      setTarget(undefined);
      onDone();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <>
      <Flex
        align="center"
        justify="space-between"
        gap={12}
        style={{ marginBottom: 12 }}
        data-testid="bulk-filing-bar"
      >
        <Typography.Text strong>
          {t('cabinets.selected', { count: documentIds.length })}
        </Typography.Text>
        <Space>
          <Button onClick={() => setMode('copy')}>{t('cabinets.copyTo')}</Button>
          {currentCabinetId && (
            <Button onClick={() => setMode('move')}>{t('cabinets.moveTo')}</Button>
          )}
        </Space>
      </Flex>

      <Modal
        open={mode !== null}
        title={mode === 'move' ? t('cabinets.moveTo') : t('cabinets.copyTo')}
        onCancel={() => setMode(null)}
        onOk={() => void submit()}
        okButtonProps={{ disabled: !target }}
        confirmLoading={refile.isPending}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">
          {mode === 'move' ? t('cabinets.moveHint') : t('cabinets.copyHint')}
        </Typography.Paragraph>
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder={t('cabinets.pickCabinet')}
          value={target}
          onChange={setTarget}
          options={cabinets
            .filter((cabinet) => cabinet.id !== currentCabinetId)
            .map((cabinet) => ({ value: cabinet.id, label: cabinet.name }))}
        />
      </Modal>
    </>
  );
}
