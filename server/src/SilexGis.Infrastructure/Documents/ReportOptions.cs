// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Files;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// The bounds on the two things a generated write-up takes from outside this application: a
/// picture somebody hands it to be placed in it, and the wait for a service that turns it into
/// a portable document.
/// </summary>
/// <remarks>
/// A write-up is otherwise built entirely from what this application already holds, so these
/// are the only numbers on that path an installation has to be able to move: they bound an
/// upload and a call to another service, the two parts of it this application does not control.
/// </remarks>
public sealed class ReportOptions
{
    public const string SectionName = "Reports";

    /// <summary>The largest picture of a map a write-up's download takes, in bytes.</summary>
    /// <remarks>
    /// Sized against what the page itself sends — a map of fixed size, a few hundred kilobytes
    /// as it leaves the browser and a megabyte or two over photographic imagery — with room
    /// above it, and far below anything that would make the document it goes into awkward to
    /// mail. The picture is held in memory for the one request
    /// that brought it and is kept nowhere afterwards.
    /// </remarks>
    public long MaxMapBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>The most pixels such a picture may hold: its width times its height.</summary>
    /// <remarks>
    /// Separate from the byte bound because the two measure different things. A picture is held
    /// decoded while it is redrawn, at a few bytes a pixel whatever its file weighed, and a file
    /// of a few kilobytes can declare a size whose pixels would take gigabytes — so the declared
    /// size is read first and the picture is decoded only once it has passed this.
    /// </remarks>
    public long MaxMapPixels { get; set; } = 8_000_000;

    /// <summary>
    /// The request-body ceiling for the route that takes the picture: the picture's own bound
    /// plus room for the multipart envelope around it.
    /// </summary>
    /// <remarks>
    /// Above the picture's bound rather than on it, for the reason the upload routes give: a
    /// request cut off by the web server is refused with a bare status that names nothing, so a
    /// picture a little over the bound has to be let through to the check that can say which
    /// bound it exceeded.
    /// </remarks>
    public long MaxMapRequestBytes => Math.Max(0, MaxMapBytes) + FilesOptions.MultipartEnvelopeBytes;

    /// <summary>
    /// How long somebody who asked for a write-up as a portable document is kept waiting for
    /// the conversion service, in seconds.
    /// </summary>
    /// <remarks>
    /// Shorter than the service's own time limit on purpose. That limit is sized for work done
    /// in the background on a long upload; this one is for a person watching a button spin, who
    /// is better told "it did not answer" than left with a request held open for minutes. A
    /// write-up is a few pages and a dozen pictures and converts in seconds on a healthy service.
    /// </remarks>
    public int PdfTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// The wait as it is applied: never less than a second and never more than five minutes,
    /// so a mistyped value can neither refuse every request nor hold one open all day.
    /// </summary>
    public TimeSpan PdfWait => TimeSpan.FromSeconds(Math.Clamp(PdfTimeoutSeconds, 1, 300));
}
