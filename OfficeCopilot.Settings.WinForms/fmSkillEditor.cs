using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DevExpress.XtraBars;
using DevExpress.XtraRichEdit;
using DevExpress.XtraTab;
using Markdig;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// Skill 编辑器窗体。
    /// 左侧：Name/Description 文本框 + PropertyGrid（YAML frontmatter 其余字段）
    /// 右侧：双标签 — "Skill预览"（WebBrowser 实时渲染）+ "MarkDown编辑"（RichEditControl 源码）
    /// </summary>
    public partial class fmSkillEditor : HostAwareForm
    {
        private readonly string _skillsRootDir;
        private readonly string _skillFilePath;
        private string _originalFrontmatter = string.Empty;

        // 由编辑器自身写入的字段；其余顶层字段（如 user-invocable、disable-model-invocation）保存时原样保留
        private static readonly HashSet<string> ManagedFrontmatterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "description", "display-name", "category", "enabled",
            "use-when", "avoid-when", "allowed-tools", "preferred-tools", "verification-tools",
            "companion-skills", "fallback-skills", "requires-confirmation", "requires-backup",
            "require-verification", "allow-high-risk-tools", "allow-code-execution", "allow-vba-fallback",
            "max-tool-round-count"
        };
        private readonly bool _isNew;
        private readonly Timer _previewTimer;
        private SkillYamlModel _yamlModel;
        private bool _suppressPropertyChanged;

        public string SkillName { get; private set; }
        public string SkillDescription { get; private set; }
        public string SavedFilePath { get; private set; }

        public fmSkillEditor(string skillsRootDirectory)
        {
            InitializeComponent();
            _isNew = true;
            _skillsRootDir = skillsRootDirectory;
            _skillFilePath = null;
            _previewTimer = new Timer { Interval = 500 };
            _previewTimer.Tick += PreviewTimer_Tick;
            WireEvents();
            SetupEditor();
        }

        public fmSkillEditor(string skillFilePath, bool isNew = false)
        {
            InitializeComponent();
            _isNew = isNew;
            _skillFilePath = skillFilePath;
            _skillsRootDir = null;
            _previewTimer = new Timer { Interval = 500 };
            _previewTimer.Tick += PreviewTimer_Tick;
            WireEvents();
            SetupEditor();
            if (!_isNew && File.Exists(skillFilePath))
                LoadSkillFile(skillFilePath);
        }

        private void WireEvents()
        {
            this.Load += fmSkillEditor_Load;
            txtMarkDown.TextChanged += MarkDown_TextChanged;
            pgcSkillBasicParams.CellValueChanged += PgcSkillBasicParams_CellValueChanged;
            xtcSkillMarkDown.SelectedPageChanged += XtcSkillMarkDown_SelectedPageChanged;
        }

        private void SetupEditor()
        {
            //txtMarkDown.Options.Behavior.Open = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.Save = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.SaveAs = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.CreateNew = DocumentCapability.Hidden;
            //txtMarkDown.Options.HorizontalRuler.Visibility = RichEditRulerVisibility.Hidden;
            webPreview.Navigate("about:blank");
            _yamlModel = new SkillYamlModel();
            pgcSkillBasicParams.SelectedObject = _yamlModel;
            xtcSkillMarkDown.SelectedTabPage = xtpWebPreview;
        }

        private void fmSkillEditor_Load(object sender, EventArgs e)
        {
            if (_isNew)
            {
                txtMarkDown.Text = "# Skill 名称\n\n在此编写 Skill 指令和逻辑...\n";
                txtName.Text = string.Empty;
                txtDescription.Text = string.Empty;
                txtName.Focus();
                UpdatePreview();
            }
            else
            {
                txtName.Focus();
                UpdatePreview();
            }
        }

        private void LoadSkillFile(string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var (frontmatterYaml, body) = SplitFrontmatter(content);

                _originalFrontmatter = frontmatterYaml ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(frontmatterYaml))
                    ParseYamlToModel(frontmatterYaml);

                txtMarkDown.Text = body ?? string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "加载 Skill 文件失败：" + ex.Message, "Skill 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static (string frontmatterYaml, string body) SplitFrontmatter(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return (null, string.Empty);

            // 去除 UTF-8 BOM（某些编辑器会在文件开头写入 \uFEFF）
            if (content.Length > 0 && content[0] == '\uFEFF')
                content = content.Substring(1);

            var match = Regex.Match(content, @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n?", RegexOptions.Singleline);
            if (!match.Success) return (null, content);
            return (match.Groups[1].Value, content.Substring(match.Length));
        }

        private void ParseYamlToModel(string yaml)
        {
            try
            {
                _suppressPropertyChanged = true;

                var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var ctx = new YamlParseContext(lines);

                txtName.Text = ctx.GetScalar("name") ?? string.Empty;
                txtDescription.Text = ctx.GetScalar("description") ?? string.Empty;
                _yamlModel.Enabled = SkillFrontmatterText.ToEnabled(ctx.GetScalar("enabled"));
                _yamlModel.DisplayName = ctx.GetScalar("display-name") ?? string.Empty;
                _yamlModel.Category = ctx.GetScalar("category") ?? string.Empty;
                _yamlModel.UseWhen = ctx.GetList("use-when");
                _yamlModel.AvoidWhen = ctx.GetList("avoid-when");
                _yamlModel.AllowedTools = ctx.GetList("allowed-tools");
                _yamlModel.PreferredTools = ctx.GetList("preferred-tools");
                _yamlModel.VerificationTools = ctx.GetList("verification-tools");
                _yamlModel.CompanionSkills = ctx.GetList("companion-skills");
                _yamlModel.FallbackSkills = ctx.GetList("fallback-skills");
                _yamlModel.RequiresConfirmation = ctx.GetBool("requires-confirmation");
                _yamlModel.RequiresBackup = ctx.GetBool("requires-backup");
                _yamlModel.RequireVerification = ctx.GetBool("require-verification");
                _yamlModel.AllowHighRiskTools = ctx.GetBool("allow-high-risk-tools");
                _yamlModel.AllowCodeExecution = ctx.GetBool("allow-code-execution") || ctx.GetBool("allow-vba-fallback");
                _yamlModel.MaxToolRoundCount = ctx.GetInt("max-tool-round-count", 3);

                pgcSkillBasicParams.Refresh();
            }
            catch { }
            finally { _suppressPropertyChanged = false; }
        }

        private void MarkDown_TextChanged(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            UpdatePreview();
        }

        private void XtcSkillMarkDown_SelectedPageChanged(object sender, TabPageChangedEventArgs e)
        {
            if (e.Page == xtpWebPreview) UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                var markdown = txtMarkDown.Text ?? string.Empty;
                var html = Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
                var fullHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><style>
body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;padding:16px;line-height:1.6;color:#333;}
h1,h2,h3{color:#1a5276;}h1{border-bottom:2px solid #1a5276;padding-bottom:8px;}
h2{border-bottom:1px solid #ddd;padding-bottom:4px;}
code{background:#f4f4f4;padding:2px 6px;border-radius:3px;font-family:Consolas,'Courier New',monospace;}
pre{background:#2d2d2d;color:#f8f8f2;padding:16px;border-radius:6px;overflow-x:auto;}
pre code{background:none;padding:0;color:inherit;}
blockquote{border-left:4px solid #1a5276;padding-left:16px;margin-left:0;color:#666;}
table{border-collapse:collapse;width:100%;}
th,td{border:1px solid #ddd;padding:8px;text-align:left;}th{background:#f0f0f0;}
a{color:#2980b9;}
</style></head><body>" + html + "</body></html>";
                webPreview.DocumentText = fullHtml;
            }
            catch { }
        }

        private void PgcSkillBasicParams_CellValueChanged(object sender, DevExpress.XtraVerticalGrid.Events.CellValueChangedEventArgs e)
        {
            if (_suppressPropertyChanged) return;
            UpdatePreview();
        }

        private void btnSave_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show(this, "请输入 Skill 名称。", "Skill 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtName.Focus();
                return;
            }
            SkillName = txtName.Text.Trim();
            SkillDescription = (txtDescription.Text ?? string.Empty).Trim();
            try
            {
                SavedFilePath = SaveSkillFile();
                MessageBox.Show(this, "已保存。Skill 变更将在重启应用后生效。", "Skill 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "Skill 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string SaveSkillFile()
        {
            var yaml = BuildYaml();
            var body = txtMarkDown.Text ?? string.Empty;
            var fullContent = "---\n" + yaml + "---\n\n" + body;
            string filePath;
            if (_isNew)
            {
                var dirPath = Path.Combine(_skillsRootDir, SanitizeDirectoryName(SkillName));
                Directory.CreateDirectory(dirPath);
                filePath = Path.Combine(dirPath, "SKILL.md");
            }
            else
            {
                filePath = _skillFilePath;
                var dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            File.WriteAllText(filePath, fullContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return filePath;
        }

        private string BuildYaml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("name: " + EscapeYamlValue(txtName.Text));
            if (!string.IsNullOrWhiteSpace(txtDescription.Text))
                sb.AppendLine("description: " + EscapeYamlValue(txtDescription.Text));
            if (!_yamlModel.Enabled) sb.AppendLine("enabled: false");
            if (!string.IsNullOrWhiteSpace(_yamlModel.DisplayName))
                sb.AppendLine("display-name: " + EscapeYamlValue(_yamlModel.DisplayName));
            if (!string.IsNullOrWhiteSpace(_yamlModel.Category))
                sb.AppendLine("category: " + EscapeYamlValue(_yamlModel.Category));
            AppendList(sb, "use-when", _yamlModel.UseWhen);
            AppendList(sb, "avoid-when", _yamlModel.AvoidWhen);
            AppendList(sb, "allowed-tools", _yamlModel.AllowedTools);
            AppendList(sb, "preferred-tools", _yamlModel.PreferredTools);
            AppendList(sb, "verification-tools", _yamlModel.VerificationTools);
            AppendList(sb, "companion-skills", _yamlModel.CompanionSkills);
            AppendList(sb, "fallback-skills", _yamlModel.FallbackSkills);
            if (_yamlModel.RequiresConfirmation) sb.AppendLine("requires-confirmation: true");
            if (_yamlModel.RequiresBackup) sb.AppendLine("requires-backup: true");
            if (_yamlModel.RequireVerification) sb.AppendLine("require-verification: true");
            if (_yamlModel.AllowHighRiskTools) sb.AppendLine("allow-high-risk-tools: true");
            if (_yamlModel.AllowCodeExecution) sb.AppendLine("allow-code-execution: true");
            if (_yamlModel.MaxToolRoundCount != 3)
                sb.AppendLine("max-tool-round-count: " + _yamlModel.MaxToolRoundCount);
            foreach (var line in SkillFrontmatterText.ExtractUnmanagedBlocks(_originalFrontmatter, ManagedFrontmatterKeys))
                sb.AppendLine(line);
            return sb.ToString();
        }

        private static string EscapeYamlValue(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "\"\"";
            if (v.Contains(":")||v.Contains("#")||v.Contains("{")||v.Contains("}")||v.Contains("[")||v.Contains("]")||v.Contains(",")||v.Contains("&")||v.Contains("*")||v.Contains("?")||v.Contains("|")||v.Contains("-")||v.Contains("<")||v.Contains(">")||v.Contains("=")||v.Contains("!")||v.Contains("%")||v.Contains("@")||v.Contains("`")||v.StartsWith(" ")||v.EndsWith(" "))
                return "\"" + v.Replace("\\","\\\\").Replace("\"","\\\"") + "\"";
            return v;
        }

        private static void AppendList(StringBuilder sb, string key, List<string> list)
        {
            if (list == null || list.Count == 0) return;
            if (list.Count == 1) { sb.AppendLine(key + ": " + EscapeYamlValue(list[0])); return; }
            sb.AppendLine(key + ":");
            foreach (var item in list) sb.AppendLine("  - " + EscapeYamlValue(item));
        }

        private static string SanitizeDirectoryName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return name.Trim();
        }

        private void btnCancel_ItemClick(object sender, ItemClickEventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// 轻量手动 YAML 解析器，提取标量和列表值。
        /// 避免依赖 YamlDotNet（net8.0 → net472 不可引用）。
        /// </summary>
        private sealed class YamlParseContext
        {
            private readonly string[] _lines;

            public YamlParseContext(string[] lines)
            {
                _lines = lines;
            }

            public string GetScalar(string key)
            {
                for (int i = 0; i < _lines.Length; i++)
                {
                    var line = _lines[i];
                    var match = Regex.Match(line, @"^" + Regex.Escape(key) + @"\s*:\s*(.+)$");
                    if (match.Success)
                    {
                        var val = match.Groups[1].Value.Trim();
                        // 去掉引号
                        if (val.Length >= 2 &&
                            ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                             (val.StartsWith("'") && val.EndsWith("'"))))
                        {
                            val = val.Substring(1, val.Length - 2);
                        }
                        return val;
                    }
                }
                return null;
            }

            public List<string> GetList(string key)
            {
                var result = new List<string>();
                bool inList = false;

                for (int i = 0; i < _lines.Length; i++)
                {
                    var line = _lines[i];

                    if (!inList)
                    {
                        // 检查是否为列表头: `key:` 后面跟 `- item`
                        if (Regex.IsMatch(line, @"^" + Regex.Escape(key) + @"\s*:\s*$"))
                        {
                            inList = true;
                            continue;
                        }
                        // 单行列表: `key: value`
                        var scalarMatch = Regex.Match(line, @"^" + Regex.Escape(key) + @"\s*:\s*(.+)$");
                        if (scalarMatch.Success)
                        {
                            result.Add(StripQuotes(scalarMatch.Groups[1].Value.Trim()));
                            return result;
                        }
                    }
                    else
                    {
                        // 列表项: `  - value`
                        var itemMatch = Regex.Match(line, @"^\s*-\s+(.+)$");
                        if (itemMatch.Success)
                        {
                            result.Add(StripQuotes(itemMatch.Groups[1].Value.Trim()));
                            continue;
                        }

                        // 空行或下一级键值对 = 列表结束
                        if (string.IsNullOrWhiteSpace(line) ||
                            Regex.IsMatch(line, @"^[a-zA-Z]"))
                        {
                            break;
                        }
                    }
                }

                return result;
            }

            public bool GetBool(string key)
            {
                var val = GetScalar(key);
                return val != null &&
                       (val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                        val == "1");
            }

            public int GetInt(string key, int defaultValue)
            {
                var val = GetScalar(key);
                return int.TryParse(val, out var result) ? result : defaultValue;
            }

            private static string StripQuotes(string val)
            {
                if (val.Length >= 2 &&
                    ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                     (val.StartsWith("'") && val.EndsWith("'"))))
                {
                    return val.Substring(1, val.Length - 2);
                }
                return val;
            }
        }
    }
}