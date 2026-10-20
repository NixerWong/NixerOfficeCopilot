using System;
using System.Collections.Generic;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// File-level document engine contract. Reads and writes an Office document
    /// file directly through the Open XML SDK, independent of any Office host
    /// process. Implementations are format-scoped (docx / xlsx / pptx), not
    /// host-scoped — any Office host can drive any format engine.
    /// </summary>
    public interface IDocumentEngine : IDisposable
    {
        /// <summary>Absolute path of the backing file.</summary>
        string FilePath { get; }

        /// <summary>Canonical format token: "docx" | "xlsx" | "pptx".</summary>
        string Format { get; }

        /// <summary>True when the file was opened read-write.</summary>
        bool IsEditable { get; }

        /// <summary>Flush in-memory changes to the backing file.</summary>
        void Save();

        /// <summary>Flush (unless <paramref name="discardChanges"/>) and release the package.</summary>
        void Close(bool discardChanges = false);

        /// <summary>All part URIs in the package, e.g. "/word/document.xml".</summary>
        IReadOnlyList<string> GetPartUris();

        /// <summary>Read the raw XML of a part, or null when the part does not exist.</summary>
        string ReadPartXml(string partUri);

        /// <summary>Replace the raw XML of a part. Universal fallback for arbitrary OOXML edits.</summary>
        void WritePartXml(string partUri, string xml);

        /// <summary>Validate the whole package against the Open XML schema. Empty = clean.</summary>
        IReadOnlyList<string> Validate();
    }
}
