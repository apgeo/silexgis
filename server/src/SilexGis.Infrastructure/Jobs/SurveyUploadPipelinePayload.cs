// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Jobs;

/// <summary>What a survey-upload processing job is about: the upload whose status row it writes.</summary>
public sealed record SurveyUploadPipelinePayload(Guid SurveyUploadId);
