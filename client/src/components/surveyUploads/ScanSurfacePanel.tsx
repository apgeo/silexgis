// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, App, Flex, Modal, Radio, Select, Typography } from 'antd';
import { BuildOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import ObjectSelector from '../selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';
import { parameterGroups } from '../scanProfiles/parameterSchema.ts';
import { SCOPE_LABEL_KEYS } from '../scanProfiles/scopeLabels.ts';
import { preferredProfileId } from './recordingProfile.ts';
import { surveyUploadRequestErrorText } from './surveyUploadProblems.ts';
import {
  SURFACE_STEP_ID,
  surveyUploadUnsettled,
  useMakeScanSurface,
  useProcessingProfiles,
  useRememberedProcessingProfiles,
  useScanStepParameterSchema,
  useSurveyModels,
  type SurveyUpload,
} from '../../api/hooks.ts';

const CAVE_SCOPES: readonly SelectorScope[] = [
  { id: 'caves', labelKey: 'filters.scopes.caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
];

/** The cave a surface was asked for in this visit to the page, so the result can be pointed at. */
interface AskedSurface {
  caveId: string;
  caveName: string;
}

/**
 * "Make a surface": the walls of a processed scan reconstructed by the worker and saved as a wall
 * mesh of a cave — the scan's own unless another is chosen — standing where the scan stands.
 *
 * Offered to somebody who may write the cave, once the scan is tiled. It needs the scan placed,
 * because the mesh takes the scan's placement into its vertices; the placement is shown only to a
 * reader with the exact location, so a scan whose anchor arrives empty is treated as not placed,
 * and the server refuses the request in that case anyway. Every check on the chosen cave (that it
 * can be written and placed by the caller) is the server's: the picker only offers what the
 * caller can already find.
 *
 * Progress is the run's own, drawn by the page's progress card. What this panel adds is where the
 * result went: once the run it started has finished, a link to the cave whose wall meshes now
 * hold the surface.
 */
export default function ScanSurfacePanel({
  upload,
}: {
  upload: Pick<SurveyUpload, 'id' | 'status' | 'cave' | 'pointCloud'>;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const make = useMakeScanSurface();
  const { data: models, isPending: modelsPending } = useSurveyModels(upload.cave.id);
  const [open, setOpen] = useState(false);
  const [asked, setAsked] = useState<AskedSurface | null>(null);

  const cloud = upload.pointCloud;
  const scanModel = models?.find((model) => model.id === cloud?.modelId);
  const placed = scanModel !== undefined && scanModel.anchorLongitude !== null && scanModel.anchorLatitude !== null;
  const unsettled = surveyUploadUnsettled(upload.status);

  if (!cloud || cloud.tilesetUrl === null) {
    return null;
  }

  return (
    <Flex vertical gap={8} data-testid="scan-surface">
      <Typography.Text strong>{t('surveyUploads.surface.title')}</Typography.Text>
      <Typography.Text type="secondary">{t('surveyUploads.surface.intro')}</Typography.Text>
      {!modelsPending && !placed && (
        <Typography.Text type="secondary" data-testid="scan-surface-unplaced">
          {t('surveyUploads.surface.needsPlacement')}
        </Typography.Text>
      )}
      {asked && unsettled && (
        <Alert
          type="info"
          showIcon
          data-testid="scan-surface-running"
          title={t('surveyUploads.surface.running', { cave: asked.caveName })}
        />
      )}
      {asked && upload.status === 'succeeded' && (
        <Alert
          type="success"
          showIcon
          data-testid="scan-surface-saved"
          title={t('surveyUploads.surface.saved', { cave: asked.caveName })}
          action={
            <Link to={`/caves/${asked.caveId}`} data-testid="scan-surface-open-cave">
              {t('surveyUploads.surface.openCave')}
            </Link>
          }
        />
      )}
      {asked && upload.status === 'failed' && (
        <Alert type="warning" showIcon data-testid="scan-surface-failed" title={t('surveyUploads.surface.failed')} />
      )}
      <Flex>
        <Button
          icon={<BuildOutlined />}
          disabled={!placed || unsettled}
          loading={make.isPending}
          onClick={() => setOpen(true)}
          data-testid="scan-surface-make"
        >
          {t('surveyUploads.surface.make')}
        </Button>
      </Flex>
      {open && (
        <SurfaceDialog
          scanCave={upload.cave}
          sending={make.isPending}
          onCancel={() => setOpen(false)}
          onSend={async (cave, profileId) => {
            try {
              await make.mutateAsync({ id: upload.id, caveId: cave.caveId, profileId });
              setAsked(cave);
              setOpen(false);
              message.success(t('surveyUploads.surface.started'));
            } catch (error) {
              message.error(surveyUploadRequestErrorText(error, t));
            }
          }}
        />
      )}
    </Flex>
  );
}

/**
 * The choices a surface is made with: a profile of the surface step (the one used last, while it
 * is still offered, or a built-in that changes nothing) and the cave its walls are saved for.
 */
function SurfaceDialog({
  scanCave,
  sending,
  onCancel,
  onSend,
}: {
  scanCave: SurveyUpload['cave'];
  sending: boolean;
  onCancel(): void;
  onSend(cave: AskedSurface, profileId: string | null): void | Promise<void>;
}) {
  const { t } = useTranslation();
  const { data: profiles } = useProcessingProfiles(SURFACE_STEP_ID);
  const { data: remembered } = useRememberedProcessingProfiles(SURFACE_STEP_ID);
  const { data: schema } = useScanStepParameterSchema(SURFACE_STEP_ID);
  const groups = useMemo(() => (schema ? parameterGroups(schema.schema, schema.defaults) : undefined), [schema]);
  const offered = useMemo(() => (profiles ?? []).filter((profile) => profile.stepId === SURFACE_STEP_ID), [profiles]);

  // Undefined until the person picks: the preselection follows the lists as they arrive.
  const [profileId, setProfileId] = useState<string | null | undefined>(undefined);
  const shown = profileId !== undefined ? profileId : preferredProfileId(SURFACE_STEP_ID, offered, remembered, groups);
  const chosen = offered.find((profile) => profile.id === shown);

  const [target, setTarget] = useState<'scan' | 'other'>('scan');
  const [other, setOther] = useState<AskedSurface | null>(null);
  const cave: AskedSurface | null =
    target === 'scan' ? { caveId: scanCave.id, caveName: scanCave.name } : other;

  return (
    <Modal
      open
      title={t('surveyUploads.surface.dialogTitle')}
      okText={t('surveyUploads.surface.send')}
      cancelText={t('common.cancel')}
      onCancel={onCancel}
      onOk={() => cave && void onSend(cave, chosen ? chosen.id : null)}
      okButtonProps={{ disabled: cave === null, loading: sending }}
      destroyOnHidden
    >
      <Flex vertical gap={16} data-testid="scan-surface-dialog">
        <Flex vertical gap={4}>
          <Typography.Text strong>{t('surveyUploads.surface.profile')}</Typography.Text>
          <Select<string>
            allowClear
            value={shown ?? undefined}
            placeholder={t('surveyUploads.surface.profileDefaults')}
            onChange={(value) => setProfileId(value ?? null)}
            options={offered.map((profile) => ({
              value: profile.id,
              label: `${profile.name} · ${t(SCOPE_LABEL_KEYS[profile.scope])}`,
            }))}
            aria-label={t('surveyUploads.surface.profile')}
            data-testid="scan-surface-profile"
          />
          {chosen?.description && <Typography.Text type="secondary">{chosen.description}</Typography.Text>}
        </Flex>
        <Flex vertical gap={4}>
          <Typography.Text strong>{t('surveyUploads.surface.cave')}</Typography.Text>
          <Radio.Group
            value={target}
            onChange={(event) => setTarget(event.target.value as 'scan' | 'other')}
            data-testid="scan-surface-target"
          >
            <Flex vertical gap={4}>
              <Radio value="scan" data-testid="scan-surface-target-scan">
                {t('surveyUploads.surface.scanCave', { cave: scanCave.name })}
              </Radio>
              <Radio value="other" data-testid="scan-surface-target-other">
                {t('surveyUploads.surface.otherCave')}
              </Radio>
            </Flex>
          </Radio.Group>
          {target === 'other' && (
            <ObjectSelector
              scopes={CAVE_SCOPES}
              presentation="dropdown"
              allowClear
              sorts={[]}
              exclude={[scanCave.id]}
              value={other?.caveId ?? null}
              onPick={(hit) => setOther(hit ? { caveId: hit.id, caveName: hit.title } : null)}
              placeholder={t('surveyUploads.surface.otherCavePlaceholder')}
              aria-label={t('surveyUploads.surface.otherCave')}
              data-testid="scan-surface-other-cave"
            />
          )}
        </Flex>
        <Typography.Text type="secondary">{t('surveyUploads.surface.savedAs')}</Typography.Text>
      </Flex>
    </Modal>
  );
}
