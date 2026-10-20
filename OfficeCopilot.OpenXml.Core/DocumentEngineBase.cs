using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Shared plumbing for format engines: open/save/close lifecycle, raw part
    /// access and schema validation. Format engines subclass this and supply
    /// their typed package plus the format-specific create/merge entry points.
    /// </summary>
    public abstract class DocumentEngineBase : IDocumentEngine
    {
        private readonly OpenXmlPackage _package;
        private bool _disposed;

        public string FilePath { get; }
        public bool IsEditable { get; }
        public abstract string Format { get; }

        /// <summary>The underlying Open XML package. Exposed for typed accessors on subclasses.</summary>
        protected OpenXmlPackage Package => _package;

        protected DocumentEngineBase(string filePath, bool editable, OpenXmlPackage package)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required.", nameof(filePath));
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            FilePath = Path.GetFullPath(filePath);
            IsEditable = editable;
            _package = package;
        }

        public void Save()
        {
            ThrowIfDisposed();
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be saved.");
            _package.Save();
        }

        public void Close(bool discardChanges = false)
        {
            if (_disposed)
                return;

            if (IsEditable && !discardChanges)
                _package.Save();
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _package.Dispose();
        }

        public IReadOnlyList<string> GetPartUris()
        {
            ThrowIfDisposed();
            return _package.GetAllParts().Select(p => p.Uri.ToString()).ToList();
        }

        public string ReadPartXml(string partUri)
        {
            ThrowIfDisposed();
            var part = FindPart(partUri);
            return part != null && part.RootElement != null ? part.RootElement.OuterXml : null;
        }

        public void WritePartXml(string partUri, string xml)
        {
            ThrowIfDisposed();
            var part = FindPart(partUri);
            if (part == null)
                throw new ArgumentException(string.Format("Part not found: {0}", partUri), nameof(partUri));

            using (var stream = part.GetStream(FileMode.Create))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(xml);
            }

            // A later Save() would otherwise re-serialize the stale in-memory DOM
            // over the raw content just written. Drop the loaded root element so
            // the SDK treats the part as unloaded (and lazily reloads our bytes).
            ClearLoadedRoot(part);
        }

        private static void ClearLoadedRoot(OpenXmlPart part)
        {
            try
            {
                for (var type = part.GetType(); type != null; type = type.BaseType)
                {
                    var field = type.GetField(
                        "_rootElement",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(part, null);
                        return;
                    }
                }
            }
            catch
            {
                // Best-effort: the raw write already went to the part stream; a
                // later Save() may still re-serialize the stale DOM if reflection
                // cannot reach the field (e.g. SDK internals changed).
            }
        }

        public IReadOnlyList<string> Validate()
        {
            ThrowIfDisposed();
            var validator = new OpenXmlValidator();
            var result = new List<string>();
            foreach (var error in validator.Validate(_package))
            {
                var partName = error.Part != null ? error.Part.Uri.ToString() : "(package)";
                result.Add(string.Format("[{0}] {1} (part: {2})", error.ErrorType, error.Description, partName));
            }
            return result;
        }

        private OpenXmlPart FindPart(string partUri)
        {
            var normalized = partUri.Length > 0 && partUri[0] == '/' ? partUri : "/" + partUri;
            return _package.GetAllParts()
                .FirstOrDefault(p => string.Equals(p.Uri.ToString(), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}
