// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Flex, Form } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import SymbolPicker, { SymbolPreview, SymbolSourcesNote } from '../../components/map/SymbolPicker.tsx';
import { api } from '../../api/client.ts';
import { queryKeys, type AdminSettings, type MapSymbolSettings } from '../../api/hooks.ts';
import { DEFAULT_APPROXIMATE_ENTRANCE_SYMBOL, DEFAULT_ENTRANCE_SYMBOL } from '../../map/entranceSymbols.ts';
import { entrancePalette } from '../../map/markerPalette.ts';

/**
 * The markers the installation draws its cave entrances with: one for an entrance at its surveyed
 * position, one for an entrance shown only approximately. Saved for everyone; the map, the 3D view
 * and shared map links pick the choice up with the map's configuration.
 */
export default function MapSymbolsSettingsForm({
  settings,
  onSaved,
}: {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<MapSymbolSettings>();
  const [saving, setSaving] = useState(false);
  const entrance = Form.useWatch('entranceSymbol', form);
  const approximate = Form.useWatch('approximateEntranceSymbol', form);

  useEffect(() => {
    form.setFieldsValue(settings.mapSymbols);
  }, [settings, form]);

  const save = async (values: MapSymbolSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/map-symbols', { body: values });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      // The map draws from its configuration, which is held fresh for a while; this administrator
      // expects to see the new symbol on their own map at once.
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
      message.success(t('common.saved'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 720 }}>
      <Alert type="info" showIcon title={t('mapSymbols.intro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="entranceSymbol"
        label={t('mapSymbols.entrance')}
        extra={t('mapSymbols.entranceHint')}
        rules={[{ required: true }]}
      >
        <SymbolPicker layerColor={entrancePalette.point} testId="admin-map-symbols-entrance" />
      </Form.Item>
      <Form.Item
        name="approximateEntranceSymbol"
        label={t('mapSymbols.approximate')}
        extra={t('mapSymbols.approximateHint')}
        rules={[{ required: true }]}
      >
        <SymbolPicker layerColor={entrancePalette.approximate} approximate testId="admin-map-symbols-approximate" />
      </Form.Item>
      <Form.Item label={t('mapSymbols.preview')} extra={t('mapSymbols.layerColourHint')}>
        <Flex gap={24} align="center" style={{ minHeight: 40 }} data-testid="admin-map-symbols-preview">
          {[false, true].map((isApproximate) => (
            <Flex key={String(isApproximate)} gap={8} align="center" style={{ lineHeight: 0 }}>
              {[16, 24, 32].map((size) => (
                <SymbolPreview
                  key={size}
                  reference={isApproximate ? approximate : entrance}
                  color={isApproximate ? entrancePalette.approximate : entrancePalette.point}
                  approximate={isApproximate}
                  size={size}
                />
              ))}
            </Flex>
          ))}
        </Flex>
      </Form.Item>
      <SymbolSourcesNote />
      <Flex gap={8}>
        <Button type="primary" htmlType="submit" loading={saving} data-testid="admin-map-symbols-save">
          {t('common.save')}
        </Button>
        <Button
          onClick={() =>
            form.setFieldsValue({
              entranceSymbol: DEFAULT_ENTRANCE_SYMBOL,
              approximateEntranceSymbol: DEFAULT_APPROXIMATE_ENTRANCE_SYMBOL,
            })
          }
        >
          {t('mapSymbols.reset')}
        </Button>
      </Flex>
    </Form>
  );
}
