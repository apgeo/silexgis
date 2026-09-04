// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, AutoComplete, Form, Input, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useCreateFeatureFromLibraryPhoto,
  useFeatureTypes,
  useSearch,
  type LibraryPhotoFeatureCreated,
  type LibraryPhotoSource,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { reloadEntrances } from '../../map/entranceLayer.ts';
import type { LibraryPhotoFeatureTarget } from '../../map/libraryPhotoPopup.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import DialogHost from '../DialogHost.tsx';

interface LibraryPhotoFeatureModalProps {
  /** The photograph the balloon offered; null keeps the dialog closed. */
  target: LibraryPhotoFeatureTarget | null;
  onClose: () => void;
}

type Choice = 'cave' | 'caveEntrance' | 'generic';

interface FormValues {
  choice: Choice;
  name: string;
  featureTypeId?: number;
  caveFeatureId?: string;
}

/**
 * Why the server refused, said in words the reader can act on and keyed by the stable code it sends
 * rather than matched out of its sentence — that sentence is prose written for a person and may be
 * reworded or translated without anything here noticing.
 *
 * A table rather than one general phrase, because the general phrase is "try again" and that is the
 * right advice for exactly one of these. A photograph the library no longer reports, a kind this
 * installation does not have, a cave the reader may not add to, a properties schema that will not
 * hold the two lines saying where the position came from — none of them get better by pressing the
 * button a second time, and a reader told to retry does exactly that until they give up.
 */
const REFUSALS: Readonly<Record<string, string>> = {
  'photo_library.photograph_not_found': 'libraryPhotos.refusals.photographGone',
  'photo_library.answer_truncated': 'libraryPhotos.refusals.answerTruncated',
  'photo_library.cave_not_found': 'libraryPhotos.refusals.caveGone',
  'photo_library.cave_forbidden': 'libraryPhotos.refusals.caveForbidden',
  'photo_library.type_unknown': 'libraryPhotos.refusals.typeUnknown',
  'photo_library.position_invalid': 'libraryPhotos.refusals.positionInvalid',
  'photo_library.forbidden': 'libraryPhotos.refusals.notAllowed',
  'access.create_forbidden': 'libraryPhotos.refusals.createForbidden',
  // Raised by the service that owns feature creation rather than by this route, and reaching the
  // reader all the same: a kind whose attributes will not admit the provenance keys, one that has
  // no taxonomy row, and one that cannot exist without something to sit inside.
  'feature.properties_invalid': 'libraryPhotos.refusals.propertiesRefused',
  'feature.type_required': 'libraryPhotos.refusals.typeUnknown',
  'feature.parent_required': 'libraryPhotos.refusals.needsContainer',
};

/**
 * The library not answering is the one refusal where trying again is the honest advice: the
 * container beside this one may be busy or still starting, and nothing here is wrong.
 */
const LIBRARY_SILENT: Readonly<Record<string, true>> = {
  'photo_library.unavailable': true,
  'photo_library.unauthorized': true,
  'photo_library.not_configured': true,
  'photo_library.rejected': true,
};

/** The message key for a failure, and the general phrase when the server said nothing mapped. */
const refusalKey = (error: unknown): string => {
  const code = error instanceof ApiError ? (error.code ?? '') : '';
  if (LIBRARY_SILENT[code]) {
    return 'libraryPhotos.refusals.librarySilent';
  }
  return REFUSALS[code] ?? 'common.saveFailed';
};

/**
 * Turning a photograph held in a neighbouring library into an object in this installation's own
 * registry.
 *
 * <p>
 * Two questions and no more: what it is, and what to call it. Everything else a new object can
 * carry — its type, who may see it, the club it belongs to, where it sits in the hierarchy — is
 * left at its default and edited afterwards on the object's own form. This is a way of getting a
 * measured position into the registry at the moment somebody recognises the place in a picture,
 * not a second editor, and a dialogue that asked eight questions would be answered by nobody.
 * </p>
 * <p>
 * <b>No coordinate is sent, and there is nowhere here to type one.</b> The position is read on the
 * server from the library that holds the photograph. What this screen knows about it is only what
 * the balloon knew: which library, the library's own name for the photograph, and the rectangle
 * that photograph's position arrived in.
 * </p>
 * <p>
 * On success the dialogue stays open and changes what it says, rather than closing behind a toast.
 * It has two things to report and both are worth a second: what was created, with a way to open it,
 * and anything that was already standing near the same spot — which is the answer to forty
 * photographs of one entrance quietly becoming forty caves. The warning arrives with the object
 * already created, deliberately: the person who took the picture is the only one who can tell the
 * forty-first photograph of a known hole from a second hole four metres away, and refusing would
 * make that judgement for them.
 * </p>
 */
export default function LibraryPhotoFeatureModal({
  target,
  onClose,
}: LibraryPhotoFeatureModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  const { data: featureTypes } = useFeatureTypes();
  const create = useCreateFeatureFromLibraryPhoto();
  const [saving, setSaving] = useState(false);
  const [created, setCreated] = useState<LibraryPhotoFeatureCreated | null>(null);

  // Only a cave can receive a new entrance, and the server's hit budget is shared across kinds —
  // ask for caves rather than sifting them out of a mixed answer.
  const [caveQuery, setCaveQuery] = useState('');
  const debouncedQuery = useDebouncedValue(caveQuery);
  const { data: caveResults } = useSearch(debouncedQuery, 'cave');

  const choice = Form.useWatch('choice', form) ?? 'cave';

  useEffect(() => {
    if (target === null) {
      form.resetFields();
      setCaveQuery('');
      setCreated(null);
    } else {
      // The library's own title is a file name as often as a name, so it is offered rather than
      // imposed: it is the reader's starting point and they overwrite it in place.
      form.setFieldsValue({ choice: 'cave', name: target.title ?? '' });
    }
  }, [target, form]);

  /**
   * The kinds a photograph can become, and only those. A kind that cannot hold a point would be
   * refused by the server on geometry, and one that cannot exist outside a container would be
   * refused for having none — offering either would be offering a choice that fails on submission.
   */
  const pointKinds = (featureTypes ?? []).filter(
    (type) => !type.requiresParent && type.acceptedGeometryClasses.includes('point'),
  );

  const submit = async () => {
    if (!target) {
      return;
    }

    // An empty required field rejects here, and the form has already said so beside the field
    // itself. Caught rather than left to travel: the dialog's button cannot await this, so a
    // rejection escaping would surface as an unhandled promise rejection in the browser — recorded
    // as a defect by the sweep, and reported to the reader as nothing at all.
    let values: FormValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }

    setSaving(true);
    try {
      const made = await create.mutateAsync({
        source: target.source as LibraryPhotoSource,
        reference: target.reference,
        bbox: target.bbox,
        body: {
          kind: values.choice,
          name: values.name,
          featureTypeId: values.choice === 'generic' ? (values.featureTypeId ?? null) : null,
          caveFeatureId: values.choice === 'caveEntrance' ? (values.caveFeatureId ?? null) : null,
        },
      });
      setCreated(made);

      // The overlays are loaded per viewport rather than cached, so what they need is a reload of
      // the extent on screen. Both are asked: a cave arrives with its first entrance, and a feature
      // of another kind lands on the surface layer.
      reloadEntrances();
      surfaceFeaturesChanged();
    } catch (error) {
      message.error(t(refusalKey(error)));
    } finally {
      setSaving(false);
    }
  };

  const open = () => {
    if (!created) {
      return;
    }
    if (created.kind === 'cave') {
      setSelection({ kind: 'cave', caveId: created.featureId });
    } else {
      // An entrance is opened as the feature it is: the panel resolves any feature id, and the cave
      // above it is one step away from there — which is a step, rather than a second request made
      // here to find out which cave was named a moment ago.
      setSelection({ kind: 'feature', featureId: created.featureId });
    }
    onClose();
  };

  return (
    <DialogHost
      // Deliberately the placement preference the map's other "put something here" dialogue uses:
      // somebody who wants these docked beside the map wants all of them docked.
      kind="cave-add"
      title={t('libraryPhotos.createTitle')}
      open={target !== null}
      onCancel={onClose}
      onOk={created ? undefined : () => void submit()}
      okLoading={saving}
    >
      {created ? (
        <>
          <Alert
            type="success"
            showIcon
            message={t('libraryPhotos.created', { name: created.name })}
            action={
              <Typography.Link onClick={open}>{t('libraryPhotos.openCreated')}</Typography.Link>
            }
          />
          {created.nearby.length > 0 && (
            <Alert
              style={{ marginTop: 12 }}
              type="warning"
              showIcon
              message={t('libraryPhotos.nearbyTitle')}
              description={
                <>
                  <div>{t('libraryPhotos.nearbyHint')}</div>
                  <ul>
                    {created.nearby.map((hit) => (
                      <li key={hit.featureId}>
                        {t('libraryPhotos.nearbyItem', {
                          name: hit.name ?? t('features.unnamed'),
                          distance: hit.distanceMeters,
                        })}
                      </li>
                    ))}
                  </ul>
                </>
              }
            />
          )}
        </>
      ) : (
        <>
          <Typography.Paragraph type="secondary">
            {t('libraryPhotos.createHint')}
          </Typography.Paragraph>
          <Form<FormValues> form={form} layout="vertical" initialValues={{ choice: 'cave' }}>
            <Form.Item name="choice" label={t('libraryPhotos.kind')} rules={[{ required: true }]}>
              <Select
                options={[
                  { value: 'cave', label: t('libraryPhotos.kindCave') },
                  { value: 'caveEntrance', label: t('libraryPhotos.kindEntrance') },
                  { value: 'generic', label: t('libraryPhotos.kindOther') },
                ]}
              />
            </Form.Item>
            {choice === 'generic' && (
              <Form.Item
                name="featureTypeId"
                label={t('libraryPhotos.featureType')}
                rules={[{ required: true }]}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  options={pointKinds.map((type) => ({ value: Number(type.id), label: type.name }))}
                />
              </Form.Item>
            )}
            {choice === 'caveEntrance' && (
              <Form.Item
                name="caveFeatureId"
                label={t('libraryPhotos.cave')}
                rules={[{ required: true }]}
              >
                <AutoComplete
                  onSearch={setCaveQuery}
                  options={(caveResults?.features ?? []).map((feature) => ({
                    value: feature.id,
                    label: feature.name ?? t('features.unnamed'),
                  }))}
                  placeholder={t('libraryPhotos.pickCave')}
                />
              </Form.Item>
            )}
            <Form.Item
              name="name"
              label={t('libraryPhotos.name')}
              rules={[{ required: true }, { max: 255 }]}
            >
              <Input autoFocus />
            </Form.Item>
          </Form>
        </>
      )}
    </DialogHost>
  );
}
