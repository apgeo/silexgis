// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Flex, Popconfirm, Tag, Typography } from 'antd';
import Button from '../../../components/Button.tsx';
import List from '../../../components/List.tsx';
import { useTranslation } from 'react-i18next';
import {
  useAddPermissionGroupMember,
  usePermissionGroupEntries,
  usePermissionGroupMembers,
  useRemovePermissionGroupMember,
  type AccessSubjectKind,
  type PermissionGroup,
} from '../../../api/hooks.ts';
import SubjectPicker from '../../../components/permissions/SubjectPicker.tsx';
import { accessRuleProblem } from './permissionGroupProblems.ts';
import ReviewRightWarning from '../../../components/permissions/ReviewRightWarning.tsx';
import { grantsUploadReview } from '../../../components/permissions/reviewRight.ts';

/**
 * The trustee list: users, and caving groups whose account-holding members inherit the
 * ruleset. Adding a trustee hands them everything the rules grant, so the server holds
 * it to the same no-amplification bound as writing the rules — surfaced here when it
 * refuses. Removing the last route into full administration is refused likewise.
 */
export default function MembersTab({ group }: { group: PermissionGroup }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members, isPending, isError, refetch } = usePermissionGroupMembers(group.id);
  const { data: entries } = usePermissionGroupEntries(group.id);
  // A member of this group is handed the right to review uploads when its rules give it, and
  // always when the group is full administration.
  const grantsReview =
    (group.isProtected && group.slug === 'full-administrators') || grantsUploadReview(entries);
  const addMember = useAddPermissionGroupMember(group.id);
  const removeMember = useRemovePermissionGroupMember(group.id);
  const [memberKind, setMemberKind] = useState<AccessSubjectKind>('user');
  const [memberId, setMemberId] = useState<string>();


  const onAdd = async () => {
    if (!memberId) {
      return;
    }
    try {
      await addMember.mutateAsync({ memberKind, memberId });
      setMemberId(undefined);
    } catch (error) {
      message.error(accessRuleProblem(error, t));
    }
  };

  return (
    <Flex vertical gap={12}>
      {grantsReview && <ReviewRightWarning />}
      <Flex gap={8} wrap>
        <SubjectPicker
          kind={memberKind}
          subjectId={memberId}
          onKindChange={(kind) => {
            setMemberKind(kind);
            setMemberId(undefined);
          }}
          onSubjectChange={(id) => setMemberId(id)}
        />
        <Button
          icon={<PlusOutlined />}
          onClick={() => void onAdd()}
          disabled={!memberId}
          loading={addMember.isPending}
        >
          {t('permissionGroups.addMember')}
        </Button>
      </Flex>

      {/* A list that could not be read is not an empty one: "no members" here would be a claim. */}
      {isError && (
        <Alert
          type="error"
          showIcon
          title={t('permissionGroups.membersLoadFailed')}
          action={
            <Button size="small" onClick={() => void refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      {!(isError && !members) && (
        <List
          size="small"
          loading={isPending}
          dataSource={members}
          locale={{ emptyText: t('permissionGroups.noMembers') }}
          renderItem={(member) => (
            <List.Item
              actions={[
                <Popconfirm
                  key="remove"
                  title={t('permissionGroups.removeMemberConfirm')}
                  onConfirm={() =>
                    removeMember
                      .mutateAsync({ memberKind: member.memberKind, memberId: member.memberId })
                      .catch((error: unknown) => message.error(accessRuleProblem(error, t)))
                  }
                >
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} aria-label={t('iconButtons.delete')} />
                </Popconfirm>,
              ]}
            >
              <Flex gap={8} align="center">
                <Tag>{t(`permissions.${member.memberKind}`)}</Tag>
                <Typography.Text>{member.memberName ?? member.memberId}</Typography.Text>
              </Flex>
            </List.Item>
          )}
        />
      )}
    </Flex>
  );
}
