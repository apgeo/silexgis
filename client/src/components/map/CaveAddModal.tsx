// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { App, Checkbox, Flex, Form, Input, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  createEntranceFor,
  useCaveTypes,
  useCreateCave,
  useEntranceTypes,
  type CaveWrite,
} from '../../api/hooks.ts';
import { useCoordinateFormatter } from '../../hooks/useCoordinateFormat.ts';
import { reloadEntrances } from '../../map/entranceLayer.ts';
import type { PlacementMode } from '../../map/mapEdit.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import DialogHost from '../DialogHost.tsx';
import EntranceEditorModal from '../caves/EntranceEditorModal.tsx';
import ObjectSelector from '../selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';

interface CaveAddModalProps {
  /** Which placement tool produced the click; null keeps everything closed. */
  mode: PlacementMode | null;
  lonLat: [number, number] | null;
  onClose: () => void;
}

/** Only a cave can receive a new entrance, so the picker asks for caves alone. */
const CAVE_SCOPES: readonly SelectorScope[] = [
  { id: 'caves', labelKey: 'filters.scopes.caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
];

interface NewCaveFormValues {
  name: string;
  caveTypeId: number;
  entranceTypeId: number;
  visibility: 'private' | 'cavingGroup' | 'authenticated' | 'public';
  locationProtected: boolean;
}

/**
 * The map's cave-placement dialogs. "New cave" creates the cave plus its main
 * entrance at the clicked spot; "new entrance" attaches to an existing cave
 * picked by autocomplete, then hands over to the full entrance editor seeded
 * with the click. Location protection stays server-side: we submit what the
 * editor placed, and the map afterwards renders only what the visibility-
 * filtered endpoints return.
 */
export default function CaveAddModal({ mode, lonLat, onClose }: CaveAddModalProps) {
  const { t } = useTranslation();
  const formatPosition = useCoordinateFormatter();
  const { message } = App.useApp();
  const [form] = Form.useForm<NewCaveFormValues>();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  const { data: caveTypes } = useCaveTypes();
  const { data: entranceTypes } = useEntranceTypes();
  const createCave = useCreateCave();
  const [saving, setSaving] = useState(false);

  // "New entrance" flow: pick the cave first, then edit the entrance itself. Whether the picked
  // cave may take one is the server's decision when the entrance is saved; the list only offers
  // the caves this reader can find.
  const [pickedCaveId, setPickedCaveId] = useState<string | null>(null);
  // Each use of the tool gets a fresh picker. The dialog's own unmounting waits for its closing
  // animation, so it cannot be relied on to have emptied the search box by the next click.
  const [pickerSession, setPickerSession] = useState(0);

  useEffect(() => {
    if (mode === null) {
      form.resetFields();
      setPickedCaveId(null);
      setPickerSession((n) => n + 1);
    }
  }, [mode, form]);

  const submitNewCave = async () => {
    if (!lonLat) {
      return;
    }
    const values = await form.validateFields();
    setSaving(true);
    try {
      // The write contract requires every field; everything not asked in this
      // quick dialog is null (editable later on the full cave form).
      const body: CaveWrite = {
        name: values.name,
        caveTypeId: values.caveTypeId,
        visibility: values.visibility,
        locationProtected: values.locationProtected ?? false,
        isShowCave: false,
        explorationStatus: 'unknown',
        properties: null,
        parentId: null,
        cavingGroupId: null,
        otherToponyms: null,
        identificationCode: null,
        description: null,
        website: null,
        region: null,
        hydrographicBasin: null,
        valley: null,
        tributaryRiver: null,
        closestAddress: null,
        landRegistryNumber: null,
        locationNotes: null,
        rockTypeId: null,
        rockAge: null,
        surveyedLength: null,
        estimatedLength: null,
        realExtension: null,
        projectedExtension: null,
        depth: null,
        positiveDepth: null,
        negativeDepth: null,
        potentialDepth: null,
        altitude: null,
        volume: null,
        area: null,
        ramificationIndex: null,
        caveAge: null,
        protectionClass: null,
        showCaveLength: null,
        discoveryDate: null,
        discoverer: null,
      };
      const saved = await createCave.mutateAsync(body);
      await createEntranceFor(saved.id, {
        name: null,
        entranceTypeId: values.entranceTypeId,
        isMain: true,
        geom: { type: 'Point', coordinates: lonLat },
        altitude: null,
        description: null,
        positionQuality: 'map',
        surveyedAt: null,
      });
      reloadEntrances();
      setSelection({ kind: 'cave', caveId: saved.id });
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <DialogHost
        kind="cave-add"
        title={t('mapEdit.newCaveHere')}
        open={mode === 'add-cave' && lonLat !== null}
        onCancel={onClose}
        onOk={() => void submitNewCave()}
        okLoading={saving}
      >
        <Typography.Paragraph type="secondary">
          {t('entrances.coordinates')}: {lonLat ? formatPosition(lonLat[0], lonLat[1]) : ''}
        </Typography.Paragraph>
        <Form<NewCaveFormValues>
          form={form}
          layout="vertical"
          initialValues={{ visibility: 'private', locationProtected: false }}
        >
          <Form.Item name="name" label={t('caves.name')} rules={[{ required: true }, { max: 255 }]}>
            <Input autoFocus />
          </Form.Item>
          <Flex gap={12} wrap>
            <Form.Item
              name="caveTypeId"
              label={t('caves.type')}
              rules={[{ required: true }]}
              style={{ width: 220 }}
            >
              <Select options={caveTypes?.map((x) => ({ value: Number(x.id), label: x.name }))} />
            </Form.Item>
            <Form.Item
              name="entranceTypeId"
              label={t('mapEdit.entranceType')}
              rules={[{ required: true }]}
              style={{ width: 220 }}
            >
              <Select options={entranceTypes?.map((x) => ({ value: Number(x.id), label: x.name }))} />
            </Form.Item>
          </Flex>
          <Flex gap={12} wrap>
            <Form.Item name="visibility" label={t('caves.visibility')} style={{ width: 220 }}>
              <Select
                options={['private', 'cavingGroup', 'authenticated', 'public'].map((v) => ({
                  value: v,
                  label: t(`caves.visibilityValues.${v}`),
                }))}
              />
            </Form.Item>
            <Form.Item name="locationProtected" valuePropName="checked" label=" ">
              <Checkbox>{t('mapEdit.locationProtected')}</Checkbox>
            </Form.Item>
          </Flex>
        </Form>
      </DialogHost>

      <DialogHost
        kind="cave-add"
        title={t('mapEdit.newEntranceHere')}
        open={mode === 'add-entrance' && lonLat !== null && pickedCaveId === null}
        onCancel={onClose}
      >
        <Typography.Paragraph type="secondary">
          {t('mapEdit.pickCaveForEntrance')}
        </Typography.Paragraph>
        <ObjectSelector
          key={pickerSession}
          scopes={CAVE_SCOPES}
          autoFocus
          pageSize={8}
          sorts={[]}
          placeholder={t('features.linkedCavePlaceholder')}
          aria-label={t('mapEdit.pickCaveForEntrance')}
          value={pickedCaveId}
          onChange={(value) => setPickedCaveId(typeof value === 'string' ? value : null)}
          data-testid="new-entrance-cave-picker"
        />
      </DialogHost>

      {pickedCaveId !== null && lonLat !== null && (
        <EntranceEditorModal
          caveId={pickedCaveId}
          entrance={null}
          defaultCenter={lonLat}
          open
          onClose={() => {
            reloadEntrances();
            onClose();
          }}
        />
      )}
    </>
  );
}
