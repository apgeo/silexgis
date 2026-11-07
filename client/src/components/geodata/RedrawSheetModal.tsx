// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Checkbox, Flex, Form, InputNumber, Modal, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useRenderRasterMap, type RasterMapInfo } from '../../api/hooks.ts';
import Button from '../Button.tsx';
import { placementErrorKey } from '../../geodata/placement/errors.ts';

const PRESET_DPI = [150, 300, 600] as const;
const MIN_DPI = 72;
const MAX_DPI = 600;

interface Props {
  /** The sheet to draw again; null while the dialog is closed. */
  raster: RasterMapInfo | null;
  onClose: () => void;
}

/**
 * Draws a map sheet's document again with another page, other layers or another resolution.
 * A placement made by hand is kept by the server and applied to the new drawing.
 */
export default function RedrawSheetModal({ raster, onClose }: Props) {
  const { t } = useTranslation();
  return (
    <Modal
      open={raster !== null}
      onCancel={onClose}
      footer={null}
      destroyOnHidden
      title={raster ? t('geodata.redraw.title', { name: raster.name }) : undefined}
      data-testid="redraw-sheet"
    >
      {raster && <RedrawForm key={raster.id} raster={raster} onClose={onClose} />}
    </Modal>
  );
}

function RedrawForm({ raster, onClose }: { raster: RasterMapInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const render = useRenderRasterMap();
  const pageCount = Math.max(1, raster.sourcePageCount ?? 1);
  const layers = raster.sourceLayers ?? [];
  const currentDpi = raster.renderDpi ?? 300;
  const [page, setPage] = useState<number>(raster.sourcePage ?? 1);
  const [allLayers, setAllLayers] = useState(raster.renderLayers == null);
  const [chosen, setChosen] = useState<string[]>(raster.renderLayers ?? layers);
  const [preset, setPreset] = useState<number | 'custom'>(
    (PRESET_DPI as readonly number[]).includes(currentDpi) ? currentDpi : 'custom',
  );
  const [customDpi, setCustomDpi] = useState<number>(Math.min(MAX_DPI, Math.max(MIN_DPI, currentDpi)));

  const dpi = preset === 'custom' ? customDpi : preset;

  const submit = async () => {
    try {
      await render.mutateAsync({
        id: raster.id,
        body: {
          page,
          layers: layers.length === 0 || allLayers ? null : chosen,
          dpi,
          allLayers: layers.length > 0 && allLayers,
        },
      });
      message.success(t('geodata.redraw.queued'));
      onClose();
    } catch (error) {
      message.error(t(placementErrorKey(error instanceof ApiError ? error.code : undefined)));
    }
  };

  return (
    <Form layout="vertical" onFinish={() => void submit()}>
      <Form.Item label={t('geodata.redraw.page')} extra={t('geodata.redraw.pageOf', { pages: pageCount })}>
        <InputNumber<number>
          min={1}
          max={pageCount}
          precision={0}
          value={page}
          onChange={(next) => setPage(next ?? 1)}
          data-testid="redraw-page"
        />
      </Form.Item>
      {layers.length > 0 && (
        <Form.Item label={t('geodata.redraw.layers')}>
          <Flex vertical gap={8}>
            <Checkbox checked={allLayers} onChange={(e) => setAllLayers(e.target.checked)} data-testid="redraw-all-layers">
              {t('geodata.redraw.allLayers')}
            </Checkbox>
            <Checkbox.Group
              disabled={allLayers}
              value={allLayers ? layers : chosen}
              onChange={(next) => setChosen(next as string[])}
              options={layers.map((name) => ({ value: name, label: name }))}
              style={{ display: 'flex', flexDirection: 'column', gap: 4 }}
            />
          </Flex>
        </Form.Item>
      )}
      <Form.Item label={t('geodata.redraw.resolution')}>
        <Flex gap={8} align="center" wrap>
          <Select<number | 'custom'>
            value={preset}
            onChange={setPreset}
            style={{ width: 160 }}
            options={[
              ...PRESET_DPI.map((value) => ({ value, label: t('geodata.redraw.dpi', { value }) })),
              { value: 'custom' as const, label: t('geodata.redraw.customDpi') },
            ]}
            data-testid="redraw-dpi"
          />
          {preset === 'custom' && (
            <InputNumber<number>
              min={MIN_DPI}
              max={MAX_DPI}
              precision={0}
              value={customDpi}
              onChange={(next) => setCustomDpi(next ?? currentDpi)}
              suffix="dpi"
              aria-label={t('geodata.redraw.customDpi')}
            />
          )}
        </Flex>
      </Form.Item>
      <Typography.Paragraph type="secondary">{t('geodata.redraw.keepsPlacement')}</Typography.Paragraph>
      <Flex justify="end" gap={8}>
        <Button onClick={onClose}>{t('common.cancel')}</Button>
        <Button
          type="primary"
          htmlType="submit"
          loading={render.isPending}
          disabled={!allLayers && layers.length > 0 && chosen.length === 0}
          data-testid="redraw-submit"
        >
          {t('geodata.redraw.submit')}
        </Button>
      </Flex>
    </Form>
  );
}
