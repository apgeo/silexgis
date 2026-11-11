// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { Alert, App, Form, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useChangeSurveyModelPlacement,
  type SurveyModelInfo,
  type SurveyModelPlacement,
} from '../../api/hooks.ts';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';
import { SurveyPlacementFields } from './SurveyPlacementFields.tsx';
import { useExactCaveOrigin, type SurveyPlacementForm } from './surveyPlacement.ts';

/** Which model's placement is being changed, and whether the form starts from the suggestion. */
export interface PlacementChange {
  model: SurveyModelInfo;
  fromSuggestion: boolean;
}

/**
 * What the form opens with. From the suggestion, the grid it names — or local metres about the
 * cave's entrance, where the entrance's true position is known. Otherwise, what the model was
 * declared with: its grid, or for a local file the origin it was placed by, which is its anchor.
 * The altitude is carried across either way, because a wrong grid says nothing about it.
 */
function initialValues(
  change: PlacementChange,
  exactOrigin: { longitude: number; latitude: number } | undefined,
): SurveyPlacementForm {
  const { model, fromSuggestion } = change;
  const originHeightM = model.anchorHeightM ?? undefined;
  if (fromSuggestion && typeof model.suggestedSourceEpsg === 'number') {
    return { coordinates: 'projected', sourceEpsg: model.suggestedSourceEpsg, originHeightM };
  }
  if (fromSuggestion && model.suggestsLocalMetres) {
    return {
      coordinates: 'local',
      originLongitude: exactOrigin?.longitude,
      originLatitude: exactOrigin?.latitude,
      originHeightM,
    };
  }
  if (typeof model.sourceEpsg === 'number') {
    return { coordinates: 'projected', sourceEpsg: model.sourceEpsg, originHeightM };
  }
  return {
    coordinates: 'local',
    originLongitude: model.anchorLongitude ?? exactOrigin?.longitude,
    originLatitude: model.anchorLatitude ?? exactOrigin?.latitude,
    originHeightM,
  };
}

/**
 * Says again where an uploaded survey sits, and has its stored file read again under that answer.
 *
 * This is how a survey declared in the wrong coordinates is put right: the file is already
 * stored, only the answer about what its numbers mean was wrong, and asking for the file again
 * would make the person find it, choose it and wait for it a second time for nothing. Opened from
 * a reading's suggestion, the form starts filled in with it; the person still confirms it,
 * because they are the one who knows the survey.
 */
export default function SurveyModelPlacementModal({
  caveId,
  change,
  onClose,
}: {
  caveId: string;
  change: PlacementChange | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SurveyPlacementForm>();
  const placement = useChangeSurveyModelPlacement();
  const { exactOrigin, originProblem } = useExactCaveOrigin(caveId);

  // Filled in each time the dialog opens on a model, and once the entrance is known if it was
  // not yet: what the person typed is never overwritten, since the dialog resets on close.
  const originLongitude = exactOrigin?.longitude;
  const originLatitude = exactOrigin?.latitude;
  useEffect(() => {
    if (change === null) {
      return;
    }
    const values = initialValues(
      change,
      originLongitude === undefined || originLatitude === undefined
        ? undefined
        : { longitude: originLongitude, latitude: originLatitude },
    );
    const fill: Partial<SurveyPlacementForm> = {};
    for (const [key, value] of Object.entries(values) as [keyof SurveyPlacementForm, unknown][]) {
      if (!form.isFieldTouched(key) && form.getFieldValue(key) === undefined) {
        (fill as Record<string, unknown>)[key] = value;
      }
    }
    form.setFieldsValue(fill);
  }, [change, originLongitude, originLatitude, form]);

  const close = () => {
    form.resetFields();
    onClose();
  };

  const submit = async () => {
    if (change === null) {
      return;
    }
    let values: SurveyPlacementForm;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    // Two whole literals, so each is checked against the contract name by name.
    const body: SurveyModelPlacement =
      values.coordinates === 'projected'
        ? {
            sourceEpsg: values.sourceEpsg ?? null,
            originLongitude: null,
            originLatitude: null,
            originHeightM: values.originHeightM ?? 0,
          }
        : {
            sourceEpsg: null,
            originLongitude: values.originLongitude ?? null,
            originLatitude: values.originLatitude ?? null,
            originHeightM: values.originHeightM ?? 0,
          };
    try {
      await placement.mutateAsync({ id: change.model.id, caveId, placement: body });
      message.success(t('surveyModels.placement.started'));
      close();
    } catch (error) {
      message.error(surveyModelProblemMessage(error, t, 'surveyModels.placement.failed'));
    }
  };

  return (
    <Modal
      title={t('surveyModels.placement.title')}
      open={change !== null}
      onCancel={close}
      onOk={() => void submit()}
      okText={t('surveyModels.placement.submit')}
      okButtonProps={{ loading: placement.isPending }}
      width="min(640px, 95vw)"
      destroyOnHidden
    >
      {change && (
        <Form form={form} layout="vertical" data-testid="survey-model-placement-form">
          <Typography.Paragraph strong style={{ marginBottom: 4 }}>
            {change.model.name}
          </Typography.Paragraph>
          <Typography.Paragraph type="secondary">{t('surveyModels.placement.hint')}</Typography.Paragraph>
          {change.fromSuggestion && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('surveyModels.placement.suggestedHere')}
            />
          )}
          <SurveyPlacementFields
            form={form}
            demanded
            exactOrigin={exactOrigin}
            originProblem={originProblem}
          />
        </Form>
      )}
    </Modal>
  );
}
