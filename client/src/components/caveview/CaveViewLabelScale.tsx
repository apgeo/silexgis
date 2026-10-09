// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Dropdown } from 'antd';
import { useTranslation } from 'react-i18next';
import { MODEL_LABEL_SCALES, modelLabelScale } from '../../caveview/labelScale.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/**
 * The control that sets how large a model's station names are written.
 *
 * On the model rather than in a settings page, because the answer is found by looking: the reader
 * turns the names on, sees a wall of text over the passage they are trying to read, and makes it
 * smaller until it is not. What they choose is theirs and is kept for every model they open.
 */
export default function CaveViewLabelScale() {
  const { t } = useTranslation();
  const stored = useUiPrefsStore((state) => state.modelLabelScale);
  const setScale = useUiPrefsStore((state) => state.setModelLabelScale);
  const current = modelLabelScale(stored);

  return (
    <Dropdown
      trigger={['click']}
      menu={{
        selectable: true,
        selectedKeys: [String(current)],
        items: MODEL_LABEL_SCALES.map((scale) => ({
          key: String(scale),
          label: t('caveview.labelSizeValue', { percent: Math.round(scale * 100) }),
        })),
        onClick: ({ key }) => setScale(Number(key)),
      }}
    >
      <Button
        className="caveview-panel-label-scale"
        size="small"
        title={t('caveview.labelSize')}
        aria-label={t('caveview.labelSize')}
        data-testid="caveview-label-scale"
      >
        Aa
      </Button>
    </Dropdown>
  );
}
