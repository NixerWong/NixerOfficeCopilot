using System;
using System.Collections.Generic;
using System.IO;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Extension-keyed registry of format engines. Hosts reference the format
    /// engine projects and call each engine's static Register() once at startup.
    /// After that, Open/Create/Merge work for every registered format, enabling
    /// cross-file-type operations (e.g. ExcelCopilot editing a .docx).
    /// </summary>
    public static class DocumentEngineRegistry
    {
        private static readonly Dictionary<string, Func<string, bool, IDocumentEngine>> OpenFactories =
            new Dictionary<string, Func<string, bool, IDocumentEngine>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Action<string>> CreateFactories =
            new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Func<string, string, MergeData, MergeResult>> MergeFactories =
            new Dictionary<string, Func<string, string, MergeData, MergeResult>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register a format engine. Called once per format at host startup.</summary>
        public static void Register(
            string extension,
            Func<string, bool, IDocumentEngine> openFactory,
            Action<string> createFactory,
            Func<string, string, MergeData, MergeResult> mergeFactory)
        {
            if (openFactory == null) throw new ArgumentNullException(nameof(openFactory));
            if (createFactory == null) throw new ArgumentNullException(nameof(createFactory));
            if (mergeFactory == null) throw new ArgumentNullException(nameof(mergeFactory));

            var ext = NormalizeExtension(extension);
            OpenFactories[ext] = openFactory;
            CreateFactories[ext] = createFactory;
            MergeFactories[ext] = mergeFactory;
        }

        /// <summary>True when a format engine is registered for the given extension or file path.</summary>
        public static bool IsSupported(string pathOrExtension)
        {
            if (string.IsNullOrWhiteSpace(pathOrExtension))
                return false;

            var ext = NormalizeExtension(Path.GetExtension(pathOrExtension));
            if (ext.Length == 0)
                ext = NormalizeExtension(pathOrExtension);
            return OpenFactories.ContainsKey(ext);
        }

        /// <summary>Open an existing file with the engine matching its extension.</summary>
        public static IDocumentEngine Open(string path, bool editable)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required.", nameof(path));

            var ext = NormalizeExtension(Path.GetExtension(path));
            if (!OpenFactories.TryGetValue(ext, out var factory))
                throw new NotSupportedException(string.Format(
                    "No OpenXml engine registered for extension '{0}'. Ensure the format engine's Register() was called.", ext));

            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format("File not found: {0}", path), path);

            if (editable)
                EnsureNotLocked(path);

            return factory(path, editable);
        }

        /// <summary>
        /// 按路径写盘前确认文件未被其他进程（通常是 Office 本身）独占打开：
        /// 否则写入会失败，或被宿主下次保存覆盖。应改用当前文档工具。
        /// </summary>
        private static void EnsureNotLocked(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                throw new IOException(string.Format(
                    "File is open in another program (e.g. Office): {0}. Close it first, or use the active-document tools instead of the file-path tools.",
                    path), ex);
            }
        }

        private static bool IsSharingViolation(IOException ex)
        {
            var code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33; // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
        }

        /// <summary>Create a blank file of the format implied by the extension.</summary>
        public static void Create(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required.", nameof(path));

            var ext = NormalizeExtension(Path.GetExtension(path));
            if (!CreateFactories.TryGetValue(ext, out var factory))
                throw new NotSupportedException(string.Format(
                    "No OpenXml engine registered for extension '{0}'.", ext));

            if (File.Exists(path))
                throw new IOException(string.Format("File already exists: {0}", path));

            factory(path);
        }

        /// <summary>Merge a template with JSON data, writing the result to <paramref name="outputPath"/>.</summary>
        public static MergeResult Merge(string templatePath, string outputPath, MergeData data)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                throw new ArgumentException("templatePath is required.", nameof(templatePath));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var ext = NormalizeExtension(Path.GetExtension(templatePath));
            if (!MergeFactories.TryGetValue(ext, out var factory))
                throw new NotSupportedException(string.Format(
                    "No OpenXml engine registered for extension '{0}'.", ext));

            if (!File.Exists(templatePath))
                throw new FileNotFoundException(string.Format("Template not found: {0}", templatePath), templatePath);
            if (File.Exists(outputPath))
                throw new IOException(string.Format("Output file already exists: {0}", outputPath));

            return factory(templatePath, outputPath, data);
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;
            return extension.StartsWith(".") ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        }
    }
}
