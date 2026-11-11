// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Surveys;
using Therion.Build;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// The survey upload run under the kind a point cloud is queued as. The run is the survey
/// upload's — the same stages, writes and notices, branching on the upload's origin — and only
/// the kind differs, because the kind is what puts the job on the long-work lane, where a wait
/// of minutes to hours on the worker stands in front of nothing anybody is watching.
/// </summary>
public sealed class PointCloudPipelineHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    ISurveyCompiler compiler,
    IOptions<SurveyUploadOptions> options,
    IOptions<SurveyCompilerOptions> compilerOptions,
    IOptions<FilesOptions> filesOptions,
    SurveyUploadArtefactStore artefacts,
    FeatureWriteService features,
    PointCloudProcessing pointClouds,
    ILogger<SurveyUploadPipelineHandler> logger)
    : SurveyUploadPipelineHandler(db, fileStore, compiler, options, compilerOptions, filesOptions, artefacts, features, pointClouds, logger)
{
    public override string Kind => ProcessingJobKinds.PointCloudPipeline;
}
