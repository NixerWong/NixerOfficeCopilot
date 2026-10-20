using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class DocumentMarkdownService
    {
        private const int MaxCacheCharacters = 2000000;
        private const int MaxCacheEntries = 128;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, LinkedListNode<MarkdownCacheEntry>> CacheByKey = new Dictionary<string, LinkedListNode<MarkdownCacheEntry>>(StringComparer.Ordinal);
        private static readonly LinkedList<MarkdownCacheEntry> CacheLru = new LinkedList<MarkdownCacheEntry>();
        private static int _cachedCharacters;

        private readonly IList<IDocumentMarkdownConverter> _converters;
        private readonly MarkdownConversionOptions _options;
        private readonly LegacyOfficeFormatNormalizer _legacyNormalizer;

        public DocumentMarkdownService()
            : this(new IDocumentMarkdownConverter[]
            {
                new SpreadsheetMarkdownConverter(),
                new WordMarkdownConverter(),
                new PresentationMarkdownConverter()
            }, new MarkdownConversionOptions())
        {
        }

        public DocumentMarkdownService(IEnumerable<IDocumentMarkdownConverter> converters, MarkdownConversionOptions options)
        {
            _converters = new List<IDocumentMarkdownConverter>(converters ?? new IDocumentMarkdownConverter[0]);
            _options = options ?? new MarkdownConversionOptions();
            _legacyNormalizer = new LegacyOfficeFormatNormalizer();
        }

        public MarkdownDocument Convert(string path)
        {
            return Convert(path, CancellationToken.None);
        }

        public MarkdownDocument Convert(string path, CancellationToken cancellationToken)
        {
            if (_options.Timeout <= TimeSpan.Zero)
            {
                return ConvertCore(path, cancellationToken);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_options.Timeout);
                return ConvertCore(path, timeout.Token);
            }
        }

        private MarkdownDocument ConvertCore(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A document path is required.", nameof(path));
            }

            var extension = Path.GetExtension(path);
            var converter = _converters.FirstOrDefault(candidate => candidate != null && candidate.CanConvert(extension));
            if (converter != null)
            {
                return ConvertWithCache(path, extension, converter, cancellationToken,
                    () => ConvertDocument(converter, path, cancellationToken));
            }

            if (_legacyNormalizer.CanNormalize(extension))
            {
                var normalizedExtension = _legacyNormalizer.GetTargetExtension(extension);
                var normalizedConverter = _converters.FirstOrDefault(candidate => candidate != null && candidate.CanConvert(normalizedExtension));
                if (normalizedConverter == null)
                {
                    throw new NotSupportedException("No Markdown converter is registered for normalized format '" + normalizedExtension + "'.");
                }

                return ConvertWithCache(path, extension, normalizedConverter, cancellationToken, () =>
                {
                    var normalizedPath = _legacyNormalizer.GetOrCreateNormalizedCopy(path, cancellationToken);
                    var result = ConvertDocument(normalizedConverter, normalizedPath, cancellationToken);
                    result.SourceFormat = extension.TrimStart('.').ToLowerInvariant();
                    RestoreSourceFileName(result, Path.GetFileName(normalizedPath), Path.GetFileNameWithoutExtension(path) + normalizedExtension);
                    return result;
                });
            }

            throw new NotSupportedException("No Markdown converter is registered for '" + extension + "'.");
        }

        private static void RestoreSourceFileName(MarkdownDocument document, string cachedFileName, string sourceFileName)
        {
            // The normalized cache file is named by content hash; keep user-facing fallback titles on the original name.
            if (document == null || string.IsNullOrEmpty(cachedFileName))
            {
                return;
            }

            foreach (var section in document.Sections)
            {
                if (string.Equals(section.Title, cachedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    section.Title = sourceFileName;
                }
            }
            if (!string.IsNullOrEmpty(document.Outline))
            {
                document.Outline = document.Outline.Replace(cachedFileName, sourceFileName);
            }
        }

        private MarkdownDocument ConvertDocument(
            IDocumentMarkdownConverter converter,
            string path,
            CancellationToken cancellationToken)
        {
            var cancellable = converter as ICancellableDocumentMarkdownConverter;
            return cancellable == null
                ? converter.Convert(path, _options)
                : cancellable.Convert(path, _options, cancellationToken);
        }

        private MarkdownDocument ConvertWithCache(
            string path,
            string sourceExtension,
            IDocumentMarkdownConverter converter,
            CancellationToken cancellationToken,
            Func<MarkdownDocument> convert)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheKey = BuildCacheKey(path, sourceExtension, converter);
            MarkdownDocument cached;
            if (cacheKey != null && TryGetCached(cacheKey, out cached))
            {
                return Clone(cached);
            }

            var result = convert();
            cancellationToken.ThrowIfCancellationRequested();
            if (result == null)
            {
                return null;
            }

            if (cacheKey != null)
            {
                AddToCache(cacheKey, result);
            }

            return Clone(result);
        }

        private string BuildCacheKey(string path, string sourceExtension, IDocumentMarkdownConverter converter)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || converter == null)
            {
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (_options.MaxFileBytes > 0 && fileInfo.Length > _options.MaxFileBytes)
                {
                    return null;
                }

                string contentHash;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    contentHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }

                return string.Join("|", new[]
                {
                    sourceExtension ?? string.Empty,
                    converter.GetType().AssemblyQualifiedName ?? converter.GetType().FullName ?? string.Empty,
                    contentHash,
                    _options.MaxTotalChars.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _options.MaxSheetRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _options.MaxSheetColumns.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _options.WorksheetName ?? string.Empty,
                    _options.CellRange ?? string.Empty,
                    _options.MaxFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _options.MaxUncompressedPackageBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _options.IncludeFormulas.ToString(),
                    _options.IncludeHiddenSheets.ToString()
                });
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool TryGetCached(string key, out MarkdownDocument document)
        {
            lock (CacheLock)
            {
                LinkedListNode<MarkdownCacheEntry> node;
                if (!CacheByKey.TryGetValue(key, out node))
                {
                    document = null;
                    return false;
                }

                CacheLru.Remove(node);
                CacheLru.AddFirst(node);
                document = node.Value.Document;
                return true;
            }
        }

        private static void AddToCache(string key, MarkdownDocument document)
        {
            var copy = Clone(document);
            var characterCount = Math.Max(1, copy.ToMarkdown().Length);
            if (characterCount > MaxCacheCharacters)
            {
                return;
            }

            lock (CacheLock)
            {
                LinkedListNode<MarkdownCacheEntry> existing;
                if (CacheByKey.TryGetValue(key, out existing))
                {
                    _cachedCharacters -= existing.Value.CharacterCount;
                    CacheLru.Remove(existing);
                    CacheByKey.Remove(key);
                }

                var entry = new MarkdownCacheEntry(key, copy, characterCount);
                var node = CacheLru.AddFirst(entry);
                CacheByKey[key] = node;
                _cachedCharacters += characterCount;

                while ((_cachedCharacters > MaxCacheCharacters || CacheLru.Count > MaxCacheEntries) && CacheLru.Last != null)
                {
                    var oldest = CacheLru.Last;
                    CacheLru.RemoveLast();
                    CacheByKey.Remove(oldest.Value.Key);
                    _cachedCharacters -= oldest.Value.CharacterCount;
                }
            }
        }

        private static MarkdownDocument Clone(MarkdownDocument document)
        {
            if (document == null)
            {
                return null;
            }

            var clone = new MarkdownDocument
            {
                SourceFormat = document.SourceFormat,
                Outline = document.Outline,
                Truncated = document.Truncated
            };

            foreach (var section in document.Sections)
            {
                clone.Sections.Add(section == null ? null : new MarkdownSection
                {
                    Index = section.Index,
                    Title = section.Title,
                    Markdown = section.Markdown,
                    NeedsVisual = section.NeedsVisual,
                    VisualReason = section.VisualReason
                });
            }

            foreach (var warning in document.Warnings)
            {
                clone.Warnings.Add(warning);
            }

            return clone;
        }

        private sealed class MarkdownCacheEntry
        {
            public MarkdownCacheEntry(string key, MarkdownDocument document, int characterCount)
            {
                Key = key;
                Document = document;
                CharacterCount = characterCount;
            }

            public string Key { get; }

            public MarkdownDocument Document { get; }

            public int CharacterCount { get; }
        }
    }
}
