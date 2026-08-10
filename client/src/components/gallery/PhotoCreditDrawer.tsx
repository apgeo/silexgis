// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, Button, Drawer, Flex, Form, Input, Segmented, Select, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCapabilities,
  useCavers,
  usePhoto,
  useSetPhotoPublic,
  useUpdatePhotoCredit,
} from '../../api/hooks.ts';

/** The licence codes, most permissive first — the order somebody deciding reads them in. */
const Licences = [
  'cc0',
  'cc-by',
  'cc-by-sa',
  'cc-by-nc',
  'cc-by-nc-sa',
  'cc-by-nd',
  'cc-by-nc-nd',
  'all-rights-reserved',
] as const;

interface CreditForm {
  attribution: 'roster' | 'other';
  photographerCaverId?: string;
  photographerName?: string;
  caption?: string;
  licenceCode?: string;
  placeName?: string;
}

export interface PhotoCreditDrawerProps {
  documentId: string | null;
  onClose: () => void;
}

/**
 * Saying who took a photograph, and what may be done with it.
 *
 * <p>
 * The photographer is a caver from the roster where possible, and free text otherwise. Those are
 * alternatives rather than a pair — the server keeps only one of them — so the form makes the
 * choice explicit instead of leaving two fields both fillable and letting the answer depend on
 * which one somebody happened to type in.
 * </p>
 * <p>
 * Crediting a caver is what makes "everything Ana photographed" answerable, which a typed name
 * never is: spellings drift, people marry, and a club archive accumulates four spellings of the
 * same person over twenty years.
 * </p>
 * <p>
 * Publishing sits here too, and only for a full administrator. It is deliberately not the read
 * audience: making a picture readable by every account in the installation is an ordinary
 * editorial act, and putting it on the open internet is a different decision.
 * </p>
 */
export default function PhotoCreditDrawer({ documentId, onClose }: PhotoCreditDrawerProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<CreditForm>();

  const { data: photo, isPending } = usePhoto(documentId ?? undefined);
  const { data: cavers } = useCavers();
  const { data: capabilities } = useCapabilities();
  const updateCredit = useUpdatePhotoCredit();
  const setPublic = useSetPhotoPublic();

  const attribution = Form.useWatch('attribution', form);

  // Loaded rather than initial values: the drawer stays mounted while a different picture is
  // opened from the viewer behind it, so the form has to be refilled rather than constructed.
  useEffect(() => {
    if (!photo) {
      return;
    }

    form.setFieldsValue({
      attribution: photo.credit.photographerCaverId ? 'roster' : 'other',
      photographerCaverId: photo.credit.photographerCaverId ?? undefined,
      photographerName: photo.credit.photographerName ?? undefined,
      caption: photo.credit.caption ?? undefined,
      licenceCode: photo.credit.licenceCode ?? undefined,
      placeName: photo.credit.placeName ?? undefined,
    });
  }, [photo, form]);

  const submit = async () => {
    if (!documentId) {
      return;
    }

    const values = await form.validateFields();
    try {
      await updateCredit.mutateAsync({
        documentId,
        // Only the chosen one is sent. The server drops the other regardless, and sending both
        // would make the request read as if it could set both.
        photographerCaverId: values.attribution === 'roster' ? values.photographerCaverId : null,
        photographerName: values.attribution === 'other' ? values.photographerName : null,
        caption: values.caption ?? null,
        licenceCode: values.licenceCode ?? null,
        placeName: values.placeName ?? null,
      });

      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const publish = async (published: boolean) => {
    if (!documentId) {
      return;
    }

    try {
      await setPublic.mutateAsync({ documentId, published });
      message.success(t(published ? 'gallery.published' : 'gallery.unpublished'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Drawer
      open={documentId !== null}
      onClose={onClose}
      placement="right"
      size="large"
      title={t('gallery.editCredit')}
      destroyOnHidden
      extra={
        <Button type="primary" loading={updateCredit.isPending} onClick={() => void submit()}>
          {t('common.save')}
        </Button>
      }
    >
      {isPending || !photo ? null : (
        <Form form={form} layout="vertical" requiredMark={false} data-testid="photo-credit-form">
          <Form.Item name="attribution" label={t('gallery.photographer')}>
            <Segmented
              options={[
                { value: 'roster', label: t('gallery.fromRoster') },
                { value: 'other', label: t('gallery.someoneElse') },
              ]}
            />
          </Form.Item>

          {attribution === 'other' ? (
            <Form.Item name="photographerName" rules={[{ max: 200 }]}>
              <Input placeholder={t('gallery.photographerName')} allowClear />
            </Form.Item>
          ) : (
            <Form.Item name="photographerCaverId">
              <Select
                allowClear
                showSearch
                optionFilterProp="label"
                placeholder={t('gallery.photographer')}
                options={(cavers ?? []).map((caver) => ({ value: caver.id, label: caver.name }))}
              />
            </Form.Item>
          )}

          <Form.Item name="caption" label={t('gallery.caption')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={3} />
          </Form.Item>

          {/* Where it was taken, in words, and deliberately not a position: anybody who may read
              the picture may read this, and the coordinates are answered by a different rule. */}
          <Form.Item name="placeName" label={t('gallery.place')} rules={[{ max: 300 }]}>
            <Input allowClear />
          </Form.Item>

          <Form.Item name="licenceCode" label={t('gallery.licence')}>
            <Select
              allowClear
              placeholder={t('gallery.licenceUnsaid')}
              options={Licences.map((code) => ({
                value: code,
                label: t(`gallery.licences.${code}`),
              }))}
            />
          </Form.Item>

          {capabilities?.isFullAdmin && (
            <Flex vertical gap={4} style={{ marginTop: 8 }}>
              <Flex align="center" gap={8}>
                <Switch
                  checked={photo.credit.inPublicGallery}
                  loading={setPublic.isPending}
                  onChange={(checked) => void publish(checked)}
                  aria-label={t('gallery.inPublicGallery')}
                />
                <Typography.Text>{t('gallery.inPublicGallery')}</Typography.Text>
              </Flex>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {t('gallery.inPublicGalleryHint')}
              </Typography.Text>
            </Flex>
          )}
        </Form>
      )}
    </Drawer>
  );
}
