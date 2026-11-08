// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Flex, Form, InputNumber, Modal, Popconfirm, Tag, Tooltip, Typography } from 'antd';
import { AimOutlined, EnvironmentOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import {
  useCaveSummary,
  usePlaceSurveyModel,
  useUnplaceSurveyModel,
  type SurveyModelInfo,
  type SurveyModelPlacementRequest,
} from '../../api/hooks.ts';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';
import ScanControlPointsPlacer from './ScanControlPointsPlacer.tsx';

/** Where a placement came from, as the row words it; literal keys so the parity checks see each one. */
const SOURCE_LABEL_KEYS: Record<NonNullable<SurveyModelInfo['placementSource']>, string> = {
  manual: 'surveyModels.placement.byHand',
  fileCrs: 'surveyModels.placement.fromFile',
  controlPoints: 'surveyModels.placement.byControlPoints',
  gnss: 'surveyModels.placement.fromGnss',
};

interface PlacementForm {
  longitude: number;
  latitude: number;
  heightM: number;
  headingDeg: number;
}

/**
 * Where a scan sits on the earth, and the way to say so: a tag for the state it is in — not
 * placed, placed from the file's own coordinate system, placed by hand — and, for somebody who
 * may write the cave and see its exact position, a dialog that places it at the main entrance
 * turned to a bearing, corrects a placement the file supplied, or takes the placement away.
 *
 * The placement is the one thing about a scan that says where its cave is, which is why the
 * server publishes it only under the exact-location rule and why the row shows nothing of it to
 * a reader without that right: the anchor members arrive null for them, and the tag then says
 * nothing rather than "not placed", which would be false. Placing never touches the tiles — a
 * correction costs one request — so the dialog is offered as freely as any other edit.
 */
export default function ScanPlacementControl({
  model,
  caveId,
  canEdit,
}: {
  model: SurveyModelInfo;
  caveId: string;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: summary } = useCaveSummary(caveId);
  const place = usePlaceSurveyModel();
  const unplace = useUnplaceSurveyModel();
  const [open, setOpen] = useState(false);
  const [placingByPoints, setPlacingByPoints] = useState(false);
  const [form] = Form.useForm<PlacementForm>();

  const canViewExact = summary?.permissions.canViewExactLocation === true;
  const placed = model.anchorLongitude !== null && model.anchorLatitude !== null;
  // Only a true position is offered as a starting point: a blurred entrance would place the scan
  // at a point that is deliberately wrong, and the dialog would be the thing that made it so.
  const entrance = summary?.mainEntrance;
  const exactEntrance =
    canViewExact && entrance?.approximateLocation === false && entrance.geom
      ? { longitude: Number(entrance.geom.coordinates[0]), latitude: Number(entrance.geom.coordinates[1]) }
      : undefined;

  const openDialog = () => {
    form.setFieldsValue({
      longitude: model.anchorLongitude ?? exactEntrance?.longitude,
      latitude: model.anchorLatitude ?? exactEntrance?.latitude,
      heightM: model.anchorHeightM ?? undefined,
      headingDeg: model.placementHeadingDeg ?? 0,
    });
    setOpen(true);
  };

  const submit = async () => {
    const values = await form.validateFields();
    const placement: SurveyModelPlacementRequest = {
      longitude: values.longitude,
      latitude: values.latitude,
      heightM: values.heightM,
      headingDeg: values.headingDeg,
    };
    try {
      await place.mutateAsync({ id: model.id, caveId, placement });
      message.success(t('surveyModels.placement.placed'));
      setOpen(false);
    } catch (error) {
      message.error(surveyModelProblemMessage(error, t, 'surveyModels.placement.failed'));
    }
  };

  // A reader without the exact position is told nothing about where the scan is, not even that
  // it is nowhere: the members are withheld from them, not empty.
  if (!canViewExact) {
    return null;
  }

  return (
    <Flex gap={6} align="center" wrap data-testid={`scan-placement-${model.id}`}>
      {placed ? (
        <Tag color="geekblue" data-testid={`scan-placement-state-${model.id}`}>
          {t(SOURCE_LABEL_KEYS[model.placementSource ?? 'manual'])}
          {model.placementHeadingDeg !== null && (
            <> · {t('surveyModels.placement.bearing', { degrees: Math.round(model.placementHeadingDeg) })}</>
          )}
          {model.placementControlPoints && (
            <>
              {' · '}
              {t('surveyModels.placement.rms', {
                metres: (Math.round(model.placementControlPoints.rmsM * 100) / 100).toString(),
              })}
            </>
          )}
        </Tag>
      ) : (
        <Tag data-testid={`scan-placement-state-${model.id}`}>{t('surveyModels.placement.notPlaced')}</Tag>
      )}
      {canEdit && (
        <>
          <Tooltip title={t(placed ? 'surveyModels.placement.correct' : 'surveyModels.placement.place')}>
            <Button
              size="small"
              type="text"
              icon={<EnvironmentOutlined />}
              aria-label={t(placed ? 'surveyModels.placement.correct' : 'surveyModels.placement.place')}
              onClick={openDialog}
              data-testid={`scan-place-${model.id}`}
            />
          </Tooltip>
          {/* Picking points needs the tiles, so the scan has to have been tiled. */}
          {model.tilesetUrl !== null && (
            <Tooltip title={t('surveyModels.controlPoints.open')}>
              <Button
                size="small"
                type="text"
                icon={<AimOutlined />}
                aria-label={t('surveyModels.controlPoints.open')}
                onClick={() => setPlacingByPoints(true)}
                data-testid={`scan-place-points-${model.id}`}
              />
            </Tooltip>
          )}
          {placingByPoints && model.tilesetUrl !== null && (
            <ScanControlPointsPlacer
              model={{ ...model, tilesetUrl: model.tilesetUrl }}
              caveId={caveId}
              open
              onClose={() => setPlacingByPoints(false)}
            />
          )}
          {placed && (
            <Popconfirm
              title={t('surveyModels.placement.removeConfirm')}
              onConfirm={async () => {
                try {
                  await unplace.mutateAsync({ id: model.id, caveId });
                } catch (error) {
                  message.error(surveyModelProblemMessage(error, t, 'surveyModels.placement.failed'));
                }
              }}
            >
              <Button size="small" type="text" data-testid={`scan-unplace-${model.id}`}>
                {t('surveyModels.placement.remove')}
              </Button>
            </Popconfirm>
          )}
        </>
      )}
      <Modal
        open={open}
        title={t(placed ? 'surveyModels.placement.correct' : 'surveyModels.placement.place')}
        onCancel={() => setOpen(false)}
        onOk={() => void submit()}
        okText={t('surveyModels.placement.save')}
        confirmLoading={place.isPending}
        destroyOnHidden
      >
        <Flex vertical gap={12} data-testid="scan-placement-dialog">
          <Typography.Text type="secondary">{t('surveyModels.placement.hint')}</Typography.Text>
          {!placed && exactEntrance === undefined && (
            <Typography.Text type="warning" data-testid="scan-placement-no-entrance">
              {t('surveyModels.placement.noEntrance')}
            </Typography.Text>
          )}
          {model.placementSource === 'fileCrs' && (
            <Typography.Text type="secondary">{t('surveyModels.placement.fromFileHint')}</Typography.Text>
          )}
          <Form form={form} layout="vertical" requiredMark={false}>
            <Flex gap={12} wrap>
              <Form.Item
                name="longitude"
                label={t('surveyModels.originLongitude')}
                rules={[{ required: true, message: t('surveyModels.placement.required') }]}
              >
                <InputNumber min={-180} max={180} step={0.00001} style={{ width: 160 }} data-testid="scan-placement-longitude" />
              </Form.Item>
              <Form.Item
                name="latitude"
                label={t('surveyModels.originLatitude')}
                rules={[{ required: true, message: t('surveyModels.placement.required') }]}
              >
                <InputNumber min={-90} max={90} step={0.00001} style={{ width: 160 }} data-testid="scan-placement-latitude" />
              </Form.Item>
            </Flex>
            <Flex gap={12} wrap>
              <Form.Item
                name="heightM"
                label={t('surveyModels.originHeightM')}
                rules={[{ required: true, message: t('surveyModels.placement.required') }]}
              >
                <InputNumber min={-500} max={9000} style={{ width: 160 }} data-testid="scan-placement-height" />
              </Form.Item>
              <Form.Item
                name="headingDeg"
                label={t('surveyModels.placement.heading')}
                tooltip={t('surveyModels.placement.headingHint')}
                rules={[{ required: true, message: t('surveyModels.placement.required') }]}
              >
                <InputNumber min={0} max={360} style={{ width: 160 }} data-testid="scan-placement-heading" />
              </Form.Item>
            </Flex>
          </Form>
        </Flex>
      </Modal>
    </Flex>
  );
}
