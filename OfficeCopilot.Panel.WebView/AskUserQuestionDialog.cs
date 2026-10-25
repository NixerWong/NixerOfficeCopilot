using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OfficeCopilot.Localization;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Panel.WebView
{
    /// <summary>
    /// Phase 1: AskUserQuestion 对话框。
    /// 在 Broker 发送 tool.user_interaction.request 事件后显示，
    /// 允许用户选择/输入答案，然后将答案通过 BrokerClient 发送回 Broker。
    /// </summary>
    internal sealed class AskUserQuestionDialog : Form
    {
        private const uint EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        private readonly AgentBrokerToolUserInteractionRequest _request;
        private readonly List<QuestionPanel> _questionPanels = new List<QuestionPanel>();
        private Button _submitButton;
        private Button _cancelButton;

        public AskUserQuestionDialog(AgentBrokerToolUserInteractionRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));

            InitializeForm();
            ParseAndRenderQuestions();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += (sender, args) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ApplyLocalization();
        }

        private void InitializeForm()
        {
            Text = LocalizationService.Get("Web.panel.askTitle");
            Size = new Size(600, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Padding = new Padding(16);
        }

        private void ParseAndRenderQuestions()
        {
            AskUserQuestionInput input;
            try
            {
                input = JsonConvert.DeserializeObject<AskUserQuestionInput>(_request.InteractionDataJson ?? "{}")
                    ?? new AskUserQuestionInput();
            }
            catch
            {
                input = new AskUserQuestionInput();
            }

            if (input.Questions == null || input.Questions.Count == 0)
            {
                var lbl = new Label
                {
                    Text = LocalizationService.Get("Web.panel.noQuestions"),
                    Tag = "Web.panel.noQuestions",
                    AutoSize = true,
                    Location = new Point(16, 16)
                };
                Controls.Add(lbl);
                return;
            }

            var yOffset = 16;
            var maxWidth = ClientSize.Width - 32;

            foreach (var question in input.Questions)
            {
                var panel = new QuestionPanel(question, maxWidth, yOffset);
                _questionPanels.Add(panel);
                panel.AddToForm(this);
                yOffset = panel.BottomY + 16;
            }

            // Buttons
            _submitButton = new Button
            {
                Text = LocalizationService.Get("Web.panel.submit"),
                Tag = "Web.panel.submit",
                Location = new Point(ClientSize.Width - 200, yOffset + 8),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            _cancelButton = new Button
            {
                Text = LocalizationService.Get("Web.panel.cancel"),
                Tag = "Web.panel.cancel",
                Location = new Point(ClientSize.Width - 110, yOffset + 8),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(_submitButton);
            Controls.Add(_cancelButton);

            ClientSize = new Size(600, yOffset + 60);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ApplyLocalization));
                return;
            }

            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Web.panel.askTitle");
            ApplyLocalizationToControls(Controls);
        }

        private static void ApplyLocalizationToControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var key = control.Tag as string;
                if (!string.IsNullOrEmpty(key) && key.StartsWith("Web.", StringComparison.Ordinal))
                {
                    var value = LocalizationService.Get(key);
                    if (control is TextBox && control.IsHandleCreated)
                    {
                        SendMessage(control.Handle, EM_SETCUEBANNER, IntPtr.Zero, value);
                    }
                    else
                    {
                        control.Text = value;
                    }
                }

                if (control.HasChildren)
                {
                    ApplyLocalizationToControls(control.Controls);
                }
            }
        }

        /// <summary>
        /// 获取用户的回答，序列化为 JSON 字符串。
        /// </summary>
        public AgentBrokerToolUserInteractionResponse GetResponse()
        {
            var answers = new Dictionary<string, string>();
            var annotations = new Dictionary<string, object>();

            foreach (var panel in _questionPanels)
            {
                var answer = panel.GetAnswer();
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    answers[panel.QuestionText] = answer;
                }

                var notes = panel.GetNotes();
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    annotations[panel.QuestionText] = new { notes };
                }
            }

            return new AgentBrokerToolUserInteractionResponse
            {
                ToolCallId = _request.ToolCallId ?? string.Empty,
                AnswersJson = JsonConvert.SerializeObject(answers),
                AnnotationsJson = annotations.Count > 0 ? JsonConvert.SerializeObject(annotations) : "{}",
                Cancelled = false
            };
        }

        /// <summary>
        /// 单个问题的面板：标签 + 选项（radio/checkbox）+ 自定义输入框。
        /// </summary>
        private sealed class QuestionPanel
        {
            private readonly AskUserQuestionInput.QuestionData _question;
            private readonly int _startY;
            private readonly int _maxWidth;
            private readonly List<RadioButton> _radioButtons = new List<RadioButton>();
            private readonly List<CheckBox> _checkBoxes = new List<CheckBox>();
            private TextBox _otherTextBox;
            private TextBox _notesTextBox;

            public QuestionPanel(AskUserQuestionInput.QuestionData question, int maxWidth, int startY)
            {
                _question = question;
                _maxWidth = maxWidth;
                _startY = startY;
                QuestionText = question?.Question ?? string.Empty;
            }

            public string QuestionText { get; }

            public int BottomY { get; private set; }

            public void AddToForm(Form form)
            {
                var y = _startY;

                // Header chip
                if (!string.IsNullOrWhiteSpace(_question.Header))
                {
                    var headerLabel = new Label
                    {
                        Text = "▎" + _question.Header,
                        Font = new Font(form.Font, FontStyle.Bold),
                        ForeColor = Color.DodgerBlue,
                        AutoSize = true,
                        Location = new Point(0, y)
                    };
                    form.Controls.Add(headerLabel);
                    y += headerLabel.Height + 4;
                }

                // Question text
                var questionLabel = new Label
                {
                    Text = _question.Question ?? string.Empty,
                    AutoSize = false,
                    Size = new Size(_maxWidth, 36),
                    Location = new Point(0, y),
                    Font = new Font(form.Font.FontFamily, 10f)
                };
                form.Controls.Add(questionLabel);
                y += questionLabel.Height + 8;

                // Description for each option
                if (_question.Options != null)
                {
                    var isMultiSelect = _question.MultiSelect;

                    foreach (var option in _question.Options)
                    {
                        if (isMultiSelect)
                        {
                            var cb = new CheckBox
                            {
                                Text = option.Label ?? string.Empty,
                                AutoSize = true,
                                Location = new Point(16, y),
                                Tag = option
                            };
                            form.Controls.Add(cb);
                            _checkBoxes.Add(cb);
                            y += cb.Height + 4;

                            if (!string.IsNullOrWhiteSpace(option.Description))
                            {
                                var descLabel = new Label
                                {
                                    Text = "    " + option.Description,
                                    ForeColor = Color.Gray,
                                    AutoSize = true,
                                    Location = new Point(32, y),
                                    Font = new Font(form.Font.FontFamily, 8f)
                                };
                                form.Controls.Add(descLabel);
                                y += descLabel.Height + 8;
                            }
                        }
                        else
                        {
                            var rb = new RadioButton
                            {
                                Text = option.Label ?? string.Empty,
                                AutoSize = true,
                                Location = new Point(16, y),
                                Tag = option
                            };
                            form.Controls.Add(rb);
                            _radioButtons.Add(rb);
                            y += rb.Height + 4;

                            if (!string.IsNullOrWhiteSpace(option.Description))
                            {
                                var descLabel = new Label
                                {
                                    Text = "    " + option.Description,
                                    ForeColor = Color.Gray,
                                    AutoSize = true,
                                    Location = new Point(32, y),
                                    Font = new Font(form.Font.FontFamily, 8f)
                                };
                                form.Controls.Add(descLabel);
                                y += descLabel.Height + 8;
                            }
                        }
                    }
                }

                // "Other" freeform input
                var otherLabel = new Label
                {
                    Text = LocalizationService.Get("Web.panel.customAnswer"),
                    Tag = "Web.panel.customAnswer",
                    AutoSize = true,
                    Location = new Point(16, y),
                    Font = new Font(form.Font.FontFamily, 8f, FontStyle.Italic)
                };
                form.Controls.Add(otherLabel);
                y += otherLabel.Height + 2;

                _otherTextBox = new TextBox
                {
                    Location = new Point(16, y),
                    Size = new Size(_maxWidth - 16, 24)
                };
                form.Controls.Add(_otherTextBox);
                _otherTextBox.Tag = "Web.panel.customAnswerPlaceholder";
                SendMessage(_otherTextBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, LocalizationService.Get("Web.panel.customAnswerPlaceholder"));
                y += _otherTextBox.Height + 8;

                // Notes field
                var notesLabel = new Label
                {
                    Text = LocalizationService.Get("Web.panel.notes"),
                    Tag = "Web.panel.notes",
                    AutoSize = true,
                    Location = new Point(16, y),
                    Font = new Font(form.Font.FontFamily, 8f, FontStyle.Italic)
                };
                form.Controls.Add(notesLabel);
                y += notesLabel.Height + 2;

                _notesTextBox = new TextBox
                {
                    Location = new Point(16, y),
                    Size = new Size(_maxWidth - 16, 24)
                };
                form.Controls.Add(_notesTextBox);
                _notesTextBox.Tag = "Web.panel.notesPlaceholder";
                SendMessage(_notesTextBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, LocalizationService.Get("Web.panel.notesPlaceholder"));
                y += _notesTextBox.Height + 4;

                BottomY = y;
            }

            public string GetAnswer()
            {
                var sb = new StringBuilder();

                // Radio buttons
                var selected = _radioButtons.FirstOrDefault(r => r.Checked);
                if (selected != null)
                {
                    sb.Append(selected.Text);
                }

                // Checkboxes
                var checkedBoxes = _checkBoxes.Where(c => c.Checked).ToList();
                if (checkedBoxes.Count > 0)
                {
                    if (sb.Length > 0) sb.Clear();
                    sb.Append(string.Join(", ", checkedBoxes.Select(c => c.Text)));
                }

                // Other
                if (!string.IsNullOrWhiteSpace(_otherTextBox?.Text))
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(_otherTextBox.Text.Trim());
                }

                return sb.ToString();
            }

            public string GetNotes()
            {
                return _notesTextBox?.Text?.Trim() ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// AskUserQuestion 工具的输入 JSON 反序列化模型。
    /// </summary>
    internal sealed class AskUserQuestionInput
    {
        [JsonProperty("questions")]
        public List<QuestionData> Questions { get; set; }

        public sealed class QuestionData
        {
            [JsonProperty("question")]
            public string Question { get; set; }

            [JsonProperty("header")]
            public string Header { get; set; }

            [JsonProperty("options")]
            public List<OptionData> Options { get; set; }

            [JsonProperty("multiSelect")]
            public bool MultiSelect { get; set; }
        }

        public sealed class OptionData
        {
            [JsonProperty("label")]
            public string Label { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("preview")]
            public string Preview { get; set; }
        }
    }
}
