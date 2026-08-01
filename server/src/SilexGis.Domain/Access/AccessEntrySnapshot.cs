// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Access;

/// <summary>
/// The immutable shape of one access entry as the evaluator consumes it — resolved into
/// the request's <see cref="AccessContext"/> once, so decisions never re-read storage.
/// Carries both homes so the explainer can name where a deciding rule came from.
/// </summary>
public sealed record AccessEntrySnapshot(
    long EntryId,
    Guid? PermissionGroupId,
    AccessSubjectKind? SubjectKind,
    Guid? SubjectId,
    AccessEffect Effect,
    AccessDomain Domain,
    AccessAction Actions,
    AccessScopeKind ScopeKind,
    Guid? ScopeFeatureId,
    Guid? ScopeId,
    FeatureKind? FeatureKind,
    long? FeatureTypeId);
