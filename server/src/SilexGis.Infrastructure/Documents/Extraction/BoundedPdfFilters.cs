// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// The PDF library's stream decoders, each of the ones that can expand its input measured
/// before it runs, and refused past a ceiling per stream and per document.
/// </summary>
/// <remarks>
/// <para>
/// A PDF stream may name a chain of decoders, each fed the previous one's output, and the
/// library decodes every stream it reads wholly into memory. Deflate expands about a thousand
/// to one, so a stream naming it twice turns a megabyte of upload into a gigabyte, and a third
/// time into a terabyte — all of it inside the job worker of the server process. Counting the
/// output as it is produced, without keeping it, costs one extra pass over a stream that is
/// within the ceiling and stops a stream that is not after reading only as much as the ceiling.
/// </para>
/// <para>
/// Only the decoders that expand are measured: deflate, LZW (about three thousand to one at
/// worst) and run-length (128 to one). The text decoders shrink their input, and the image
/// decoders are not reached by text extraction. A stream past the ceiling is a document this
/// reader refuses, which the extraction records as too large rather than retrying.
/// </para>
/// <para>
/// One instance per document: the document ceiling is a running total of what that document has
/// decoded, so a file of a thousand streams each just under the stream ceiling is stopped too.
/// </para>
/// </remarks>
internal sealed class BoundedPdfFilters : BaseFilterProvider
{
    /// <summary>The most any single stream may decode to.</summary>
    public const long DefaultMaxStreamBytes = ExtractionStreams.MaxBytes;

    /// <summary>
    /// The most one document may decode to across every stream it is read for — enough for a
    /// long report whose pages each carry their own content and fonts.
    /// </summary>
    public const long DefaultMaxDocumentBytes = 16L * ExtractionStreams.MaxBytes;

    private static readonly string[] FlateNames = ["FlateDecode", "Fl"];
    private static readonly string[] LzwNames = ["LZWDecode", "LZW"];
    private static readonly string[] RunLengthNames = ["RunLengthDecode", "RL"];

    private static readonly string[] OtherNames =
    [
        "ASCII85Decode", "A85", "ASCIIHexDecode", "AHx", "CCITTFaxDecode", "CCF",
        "DCTDecode", "DCT", "JBIG2Decode", "JPXDecode",
    ];

    private BoundedPdfFilters(IReadOnlyDictionary<string, IFilter> filters)
        : base(filters)
    {
    }

    /// <summary>A provider for one document, with the given ceilings.</summary>
    public static BoundedPdfFilters ForOneDocument(
        long maxStreamBytes = DefaultMaxStreamBytes, long maxDocumentBytes = DefaultMaxDocumentBytes)
    {
        var budget = new Budget(maxStreamBytes, maxDocumentBytes);
        var filters = new Dictionary<string, IFilter>(StringComparer.Ordinal);

        void Wrap(string[] names, Func<ReadOnlyMemory<byte>, DictionaryToken, int, long, long> measure)
        {
            foreach (var name in names)
            {
                if (Default(name) is { } inner)
                {
                    filters[name] = new MeasuredFilter(inner, measure, budget);
                }
            }
        }

        Wrap(FlateNames, (input, _, _, limit) => FlateOutputLength(input, limit));
        Wrap(LzwNames, (input, parameters, index, limit) => LzwOutputLength(input.Span, EarlyChange(parameters, index), limit));
        Wrap(RunLengthNames, (input, _, _, limit) => RunLengthOutputLength(input.Span, limit));

        foreach (var name in OtherNames)
        {
            if (Default(name) is { } inner)
            {
                filters[name] = inner;
            }
        }

        return new BoundedPdfFilters(filters);
    }

    /// <summary>The library's own decoder registered under <paramref name="name"/>, or null.</summary>
    private static IFilter? Default(string name)
    {
        try
        {
            return DefaultFilterProvider.Instance.GetNamedFilters([NameToken.Create(name)]) is [var filter]
                ? filter
                : null;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// How many bytes a deflate stream decodes to, counted up to one past
    /// <paramref name="limit"/>. The library accepts both the wrapped and the bare form of the
    /// format, so the bare form is tried when the wrapped one does not read; a stream that turns
    /// out damaged part-way is counted as far as it decodes, which is as far as the library's
    /// own decoder will get with it too.
    /// </summary>
    internal static long FlateOutputLength(ReadOnlyMemory<byte> input, long limit)
    {
        var wrapped = Count(new ZLibStream(new ReadOnlyMemoryStream(input), CompressionMode.Decompress), limit);
        if (wrapped > 0 || input.Length < 2)
        {
            return wrapped;
        }

        return Count(new DeflateStream(new ReadOnlyMemoryStream(input[2..]), CompressionMode.Decompress), limit);
    }

    private static long Count(Stream decoder, long limit)
    {
        var scratch = new byte[81920];
        long total = 0;
        try
        {
            using (decoder)
            {
                int read;
                while (total <= limit && (read = decoder.Read(scratch)) > 0)
                {
                    total += read;
                }
            }
        }
        catch (InvalidDataException)
        {
            // Damaged from here on: what came out before it is what the library will decode.
        }

        return total;
    }

    /// <summary>
    /// How many bytes an LZW stream decodes to, counted up to one past <paramref name="limit"/>
    /// without building any of them: only the length of each table entry is kept, which is all
    /// the count needs.
    /// </summary>
    internal static long LzwOutputLength(ReadOnlySpan<byte> input, int earlyChange, long limit)
    {
        const int clearTable = 256;
        const int endOfData = 257;
        const int maxCodes = 4096;

        var lengths = new int[maxCodes];
        for (var i = 0; i < 256; i++)
        {
            lengths[i] = 1;
        }

        var next = 258;
        var codeLength = 9;
        var previous = -1;
        long total = 0;
        long bitPosition = 0;
        var totalBits = (long)input.Length * 8;

        while (total <= limit && bitPosition + codeLength <= totalBits)
        {
            var code = 0;
            for (var i = 0; i < codeLength; i++, bitPosition++)
            {
                var bit = (input[(int)(bitPosition >> 3)] >> (7 - (int)(bitPosition & 7))) & 1;
                code = (code << 1) | bit;
            }

            if (code == clearTable)
            {
                next = 258;
                codeLength = 9;
                previous = -1;
                continue;
            }

            if (code == endOfData)
            {
                break;
            }

            int produced;
            if (previous < 0)
            {
                if (code > 255)
                {
                    break;
                }

                produced = 1;
            }
            else if (code < next)
            {
                produced = lengths[code];
                AddEntry(lengths[previous] + 1);
            }
            else if (code == next)
            {
                produced = lengths[previous] + 1;
                AddEntry(produced);
            }
            else
            {
                break;
            }

            total += produced;
            previous = code;

            var threshold = next + earlyChange;
            codeLength = threshold >= 2048 ? 12 : threshold >= 1024 ? 11 : threshold >= 512 ? 10 : 9;
        }

        return total;

        void AddEntry(int length)
        {
            if (next < maxCodes)
            {
                lengths[next++] = length;
            }
        }
    }

    /// <summary>How many bytes a run-length stream decodes to, counted up to one past <paramref name="limit"/>.</summary>
    internal static long RunLengthOutputLength(ReadOnlySpan<byte> input, long limit)
    {
        long total = 0;
        var i = 0;
        while (total <= limit && i < input.Length)
        {
            var length = input[i++];
            if (length == 128)
            {
                break;
            }

            if (length < 128)
            {
                total += length + 1;
                i += length + 1;
            }
            else
            {
                total += 257 - length;
                i++;
            }
        }

        return total;
    }

    /// <summary>The LZW early-change parameter of the filter at <paramref name="index"/>; 1 unless stated.</summary>
    private static int EarlyChange(DictionaryToken parameters, int index)
    {
        var decodeParameters = parameters.TryGet(NameToken.DecodeParms, out var found)
            || parameters.TryGet(NameToken.Dp, out found)
                ? found
                : null;

        var forThisFilter = decodeParameters switch
        {
            DictionaryToken single => single,
            ArrayToken many when index >= 0 && index < many.Length => many[index] as DictionaryToken,
            _ => null,
        };

        return forThisFilter is not null
            && forThisFilter.TryGet(NameToken.EarlyChange, out var value)
            && value is NumericToken number
                ? number.Int
                : 1;
    }

    /// <summary>What one document has decoded so far, and its two ceilings.</summary>
    private sealed class Budget(long maxStreamBytes, long maxDocumentBytes)
    {
        private long decoded;

        public long MaxStreamBytes { get; } = maxStreamBytes;

        /// <summary>Charges <paramref name="bytes"/> to the document, refusing past either ceiling.</summary>
        public void Charge(long bytes)
        {
            if (bytes > MaxStreamBytes)
            {
                throw new ContentTooLargeException(
                    $"A stream in the document decodes to more than the {MaxStreamBytes / (1024 * 1024)} MB a reader may take.");
            }

            if (Interlocked.Add(ref decoded, bytes) > maxDocumentBytes)
            {
                throw new ContentTooLargeException(
                    $"The document's streams decode to more than the {maxDocumentBytes / (1024 * 1024)} MB a reader may take.");
            }
        }
    }

    /// <summary>A library decoder run only once its output has been counted and charged.</summary>
    private sealed class MeasuredFilter(
        IFilter inner,
        Func<ReadOnlyMemory<byte>, DictionaryToken, int, long, long> measure,
        Budget budget) : IFilter
    {
        public bool IsSupported => inner.IsSupported;

        public Memory<byte> Decode(
            Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
        {
            budget.Charge(measure(input, streamDictionary, filterIndex, budget.MaxStreamBytes));
            return inner.Decode(input, streamDictionary, filterProvider, filterIndex);
        }
    }

    /// <summary>A read-only stream over bytes already in memory, without copying them.</summary>
    private sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> bytes) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, bytes.Length - position);
            bytes.Span.Slice(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
