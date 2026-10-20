using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Word.Formatting;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>
    /// Formats many paragraphs in one call: each paragraph is mapped to a role (style + font + paragraph format).
    /// Replaces the select_paragraph → apply_style → format_selection → set_paragraph_format loop per paragraph.
    /// </summary>
    public sealed class WordFormatParagraphsTool : WordComToolBase
    {
        public const string ToolName = "word_format_paragraphs";

        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = ToolName,
            DisplayName = "Format Paragraphs (Batch)",
            Description = "Formats many paragraphs of the active document in ONE call, without changing any text. "
                + "Read paragraph indexes with word_read_paragraphs first, define roles once (style + font + paragraph format), "
                + "then map paragraphs to roles. Give every listed paragraph an 'expect' (its first few characters): a paragraph whose text "
                + "does not start with it is skipped and reported, never formatted by mistake. Unlisted non-empty paragraphs outside tables "
                + "get defaultRole. builtinStyle (Title, Subtitle, Heading1-Heading9, Normal, BodyText, Caption, ListParagraph, Quote) works "
                + "in every Word language; Chinese names such as 标题 1 are also accepted. The whole batch is a single undo step. "
                + "Prefer this tool over per-paragraph word_select_paragraph + word_apply_style / word_format_selection / word_set_paragraph_format.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = BuildSchema()
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var formatRequest = ParseRequest(ParseArguments(request));
                var validationError = WordParagraphFormatPlanner.Validate(formatRequest);
                if (validationError != null)
                {
                    return Task.FromResult(Fail(request, validationError, "word.format_paragraphs_invalid"));
                }

                var result = host.FormatParagraphs(formatRequest);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to format paragraphs."
                            : result.ErrorMessage,
                        "word.format_paragraphs_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    WordParagraphFormatPlanner.BuildSummary(result),
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.format_paragraphs_failed"));
            }
        }

        public static WordFormatParagraphsRequest ParseRequest(JObject args)
        {
            var request = new WordFormatParagraphsRequest
            {
                DefaultRole = NullIfEmpty(ReadString(args, "defaultRole", null)),
                UpdateStyleDefinitions = ReadBool(args, "updateStyleDefinitions", false),
                ClearDirectFormatting = ReadBool(args, "clearDirectFormatting", false)
            };

            var roles = args == null ? null : args["roles"] as JObject;
            if (roles != null)
            {
                foreach (var property in roles.Properties())
                {
                    var role = property.Value as JObject;
                    if (role == null)
                    {
                        continue;
                    }

                    request.Roles[property.Name] = new WordParagraphRoleFormat
                    {
                        BuiltinStyle = NullIfEmpty(ReadString(role, "builtinStyle", null)),
                        StyleName = NullIfEmpty(ReadString(role, "styleName", null)),
                        FontName = NullIfEmpty(ReadString(role, "fontName", null)),
                        LatinFontName = NullIfEmpty(ReadString(role, "latinFontName", null)),
                        Size = ReadNullableDouble(role, "size"),
                        Bold = ReadNullableBool(role, "bold"),
                        Italic = ReadNullableBool(role, "italic"),
                        Color = NullIfEmpty(ReadString(role, "color", null)),
                        Alignment = NullIfEmpty(ReadString(role, "alignment", null)),
                        LineSpacingRule = NullIfEmpty(ReadString(role, "lineSpacingRule", null)),
                        LineSpacing = ReadNullableDouble(role, "lineSpacing"),
                        SpaceBefore = ReadNullableDouble(role, "spaceBefore"),
                        SpaceAfter = ReadNullableDouble(role, "spaceAfter"),
                        FirstLineIndent = ReadNullableDouble(role, "firstLineIndent"),
                        FirstLineIndentChars = ReadNullableDouble(role, "firstLineIndentChars"),
                        LeftIndent = ReadNullableDouble(role, "leftIndent"),
                        RightIndent = ReadNullableDouble(role, "rightIndent")
                    };
                }
            }

            var paragraphs = args == null ? null : args["paragraphs"] as JArray;
            if (paragraphs != null)
            {
                foreach (var item in paragraphs.OfType<JObject>())
                {
                    request.Paragraphs.Add(new WordParagraphRoleAssignment
                    {
                        Index = ReadInt(item, "index", 0),
                        Role = NullIfEmpty(ReadString(item, "role", null)),
                        Expect = NullIfEmpty(ReadString(item, "expect", null))
                    });
                }
            }

            return request;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string BuildSchema()
        {
            var number = new Func<string, JObject>(description => new JObject { ["type"] = "number", ["description"] = description });
            var text = new Func<string, JObject>(description => new JObject { ["type"] = "string", ["description"] = description });

            var role = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["builtinStyle"] = text("Title | Subtitle | Heading1..Heading9 | Normal | BodyText | Caption | ListParagraph | Quote. Language-independent; preferred."),
                    ["styleName"] = text("Custom or localized style name from word_list_styles; used only when builtinStyle is empty."),
                    ["fontName"] = text("Font for all text, e.g. 宋体 / 黑体 / 楷体 / 仿宋."),
                    ["latinFontName"] = text("Optional separate font for Latin letters and digits, e.g. Times New Roman."),
                    ["size"] = number("Font size in points (三号 16, 四号 14, 小四 12, 五号 10.5)."),
                    ["bold"] = new JObject { ["type"] = "boolean" },
                    ["italic"] = new JObject { ["type"] = "boolean" },
                    ["color"] = text("Hex RGB such as #000000."),
                    ["alignment"] = text("left | center | right | justify."),
                    ["lineSpacingRule"] = text("single | 1.5 | double | atLeast | exactly | multiple."),
                    ["lineSpacing"] = number("Points; required with exactly / atLeast (e.g. 20 or 28)."),
                    ["spaceBefore"] = number("Points."),
                    ["spaceAfter"] = number("Points."),
                    ["firstLineIndentChars"] = number("First-line indent in characters, e.g. 2 for Chinese body text; 0 for headings."),
                    ["firstLineIndent"] = number("First-line indent in points; ignored when firstLineIndentChars is set."),
                    ["leftIndent"] = number("Points."),
                    ["rightIndent"] = number("Points.")
                },
                ["additionalProperties"] = false
            };

            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["roles"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Role name → formatting, e.g. {\"title\":{...},\"h1\":{...},\"body\":{...}}.",
                        ["additionalProperties"] = role
                    },
                    ["paragraphs"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Paragraphs that need a non-default role (titles, headings, abstract, keywords ...). 1-based indexes from word_read_paragraphs.",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["index"] = new JObject { ["type"] = "integer" },
                                ["role"] = new JObject { ["type"] = "string" },
                                ["expect"] = text("First few characters of the paragraph (whitespace ignored). Mismatch → skipped.")
                            },
                            ["required"] = new JArray("index", "role"),
                            ["additionalProperties"] = false
                        }
                    },
                    ["defaultRole"] = text("Role for every other non-empty paragraph outside tables, usually the body role."),
                    ["updateStyleDefinitions"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "true: write role formatting into the styles themselves (whole document follows the style); false (default): apply as direct formatting."
                    },
                    ["clearDirectFormatting"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "true: reset existing direct font/paragraph formatting of the targeted paragraphs first (also removes inline bold/colour)."
                    }
                },
                ["required"] = new JArray("roles"),
                ["additionalProperties"] = false
            };

            return schema.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
