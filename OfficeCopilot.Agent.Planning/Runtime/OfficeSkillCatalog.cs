using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Planning
{
    public static class OfficeSkillCatalog
    {
        public static IReadOnlyList<SkillDefinition> CreateDefaultSkills()
        {
            var skills = new List<SkillDefinition>
            {
                CreateExcelAnalysisSkill(),
                CreateExcelLiveResultSkill(),
                CreateExcelStructuralTransformSkill(),
                CreateExcelBulkFormatSkill(),
                CreatePowerPointGenerateDeckSkill(),
                CreatePowerPointEditSlideSkill(),
                CreatePowerPointBeautifySkill(),
                CreatePowerPointStyleAdvisorSkill(),
                CreateSkillifySkill(),
                CreateRecordToMemorySkill()
            };

            foreach (var skill in skills)
            {
                ApplyHostBaseToolNames(skill);
            }

            return skills;
        }

        private static readonly string[] ExcelBaseToolNames =
        {
            "runtime.get_host_context",
            "runtime.list_tools",
            "runtime.ask_user_question",
            "runtime.calculate",
            "excel_get_active_workbook",
            "excel_list_worksheets",
            "excel_get_selection",
            "excel_read_range",
            "excel_get_range_info",
            "excel_profile_range",
            "excel_aggregate",
            "excel_reconcile",
            "excel_compare_key_sets"
        };

        private static readonly string[] PowerPointBaseToolNames =
        {
            "runtime.get_host_context",
            "runtime.list_tools",
            "ppt_get_active_presentation",
            "ppt_get_selection",
            "ppt_list_slides",
            "ppt_read_slide",
            "ppt_style_get"
        };

        /// <summary>
        /// 每个宿主技能的白名单都并入该宿主的只读/上下文工具。否则技能一旦命中，
        /// 未列举的基础工具会从模型可见列表里消失，模型便会误以为“没有可用工具”而自行降级到代码执行。
        /// </summary>
        private static void ApplyHostBaseToolNames(SkillDefinition skill)
        {
            string hostKind;
            if (skill == null
                || skill.AllowedToolNames == null
                || skill.AllowedToolNames.Count == 0
                || !skill.Metadata.TryGetValue(SkillMetadataKeys.HostKind, out hostKind))
            {
                return;
            }

            var normalized = SkillHostMatcher.Normalize(hostKind);
            var baseToolNames = normalized == "excel" ? ExcelBaseToolNames
                : normalized == "powerpoint" ? PowerPointBaseToolNames
                : new string[0];

            var existing = new HashSet<string>(skill.AllowedToolNames, StringComparer.OrdinalIgnoreCase);
            foreach (var toolName in baseToolNames)
            {
                if (existing.Add(toolName))
                {
                    skill.AllowedToolNames.Add(toolName);
                }
            }
        }

        private static SkillDefinition CreatePowerPointSkill(string name, string displayName, string category, string description)
        {
            var skill = new SkillDefinition
            {
                Name = name,
                DisplayName = displayName,
                Category = category,
                Description = description,
                // 0 = 不设技能级上限，沿用全局 Runtime.MaxToolRoundCount。
                MaxToolRoundCount = 0
            };

            skill.Metadata[SkillMetadataKeys.HostKind] = "powerpoint";
            skill.Metadata[SkillMetadataKeys.Scope] = category;
            return skill;
        }

        private static SkillDefinition CreatePowerPointGenerateDeckSkill()
        {
            var skill = CreatePowerPointSkill(
                "ppt-generate-deck",
                "PowerPoint Generate Deck",
                "write",
                "Create a new deck from a topic or outline. Required: search styles, call ppt_style_get (part=preview) for each candidate style to read its preview card and template structure classes, render 3 visually distinct cover candidates with ppt_style_preview using the user's real title, "
                + "then call runtime.ask_user_question with each previewImageId and wait for the user's choice before creating the deck. Use the exact deckId/candidateName/imageVersion reference returned by ppt_style_preview; do not copy image URLs. Never choose a candidate for the user. "
                + "Template example-content CSS is not injected into the deck: implement every visual you need in your own CSS with cover-*/theme-*/sNN-* class names, never reuse a listed template structure class, and size text in px (no vw/vh/clamp). "
                + "After the user chooses, call ppt_style_get (part=design) for the chosen style, then call ppt_deck_create with that preview result's deckId and fromPreview candidate name so the exact cleaned cover becomes page 1; that page is locked (overwrites via ppt_deck_put_slides are ignored), so never rewrite or 'fix' page 1 even if ppt_deck_validate reports font or layout issues on it, and start ppt_deck_put_slides at page 2. "
                + "The css argument of ppt_deck_put_slides replaces theme.css only; never copy the cover CSS into it."
                + " From the first cover preview onward, apply one font rule: if a text block contains any Chinese, use a Chinese-capable font for the entire block; do not split mixed Chinese/English text and do not put a Latin-only font anywhere in that block's font-family stack. Use the system Chinese-font list returned by ppt_style_search; if it is empty, use a bundled Chinese family. Noto Color Emoji is automatic emoji fallback only. Use weights supported by the chosen font."
                + " Do not use text symbols such as ✿ ✦ ★ as decoration: use <i class=\"nx-icon\" data-icon=\"leaf\"></i> for icons (bare lowercase Lucide name such as leaf, bug, mountain, map-pin; never a lucide- prefix), and mark solid color blocks, pills, cards and rules with data-nx-shape=\"rect|rounded-rect|line\" so they become editable PowerPoint shapes instead of background pixels."
                + " Write the remaining HTML slides into the deck workspace in as few ppt_deck_put_slides calls as possible. After all slides are written, call ppt_deck_validate at most once and fix only its error-level issues; never validate or preview slide by slide. ppt_deck_preview only opens a window for the user and returns no image, so call it once at the end."
                + "Use .nx-content or data-nx-layer=\"content\" for slide content, .nx-chrome or data-nx-layer=\"chrome\" for page numbers/logos, and .nx-bg or data-nx-layer=\"bg\" for bleedable decoration. Keep content within the 128px horizontal and 96px vertical target safe area (minimum 112/80); chrome uses the 64/48 action area. "
                + "The runtime makes .nx-chrome a full-slide overlay (top and bottom both 48px): to use it as a footer bar write bottom with top:auto (header: top with bottom:auto), otherwise its centred content ends up mid-slide. Only explicitly marked absolute layouts may use the 80/80 exception. "
                + "preview the completed deck and ask before building with runtime.ask_user_question; in that confirmation message state the exact deckId, title and slide count from the latest ppt_deck_preview result, and offer the build mode as options: "
                + "editable (text, color blocks and icons become editable PowerPoint objects; fonts/shapes may differ slightly from the preview), fidelity (native text boxes over a background image of all decoration), "
                + "image (each slide is one full screenshot of the preview: looks exactly like the preview with no font/icon/shape differences, but nothing is editable), plus a 'not yet' option; pass the chosen value as ppt_deck_build mode. "
                + "ppt_deck_build may omit deckId/workspaceTitle/slideCount to target the workspace most recently previewed in this conversation (never guess an id). Build target: a blank current presentation is built in place; "
                + "if the current presentation has content, ask the user early via runtime.ask_user_question whether to create a new presentation (target=new) "
                + "or append to the end of the current one (target=append). replace=true replaces the entire workspace page set into fromIndex/toIndex and requires an equal page count. "
                + "Do not rebuild the same deck twice in one turn without a new user decision.");
            skill.UseWhen = new List<string>
            {
                "generate", "create presentation", "new deck", "deck", "slides", "presentation", "pitch", "outline to slides",
                "生成", "制作", "做一份", "做个", "做几页", "新建", "演示文稿", "幻灯片", "汇报", "路演", "大纲", "课件", "PPT"
            };
            skill.AllowedToolNames = new List<string>
            {
                "ppt_style_search", "ppt_style_get", "ppt_style_preview",
                "ppt_deck_create", "ppt_deck_put_slides", "ppt_deck_get_slides", "ppt_deck_remove_slides", "ppt_deck_reorder",
                "ppt_deck_preview", "ppt_deck_validate", "ppt_deck_build",
                "ppt_extract_content", "ppt_export_slide_image", "ppt_list_backups"
            };
            skill.PreferredToolNames = new List<string>
            {
                "ppt_style_search", "ppt_style_get", "ppt_style_preview", "runtime.ask_user_question",
                "ppt_deck_create", "ppt_deck_put_slides", "ppt_deck_preview",
                "ppt_deck_build"
            };
            skill.VerificationToolNames = new List<string>();
            skill.RequireVerification = false;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = false;
            return skill;
        }

        private static SkillDefinition CreatePowerPointEditSlideSkill()
        {
            var skill = CreatePowerPointSkill(
                "ppt-edit-slide",
                "PowerPoint Edit Slides",
                "write",
                "Make targeted edits to existing slides: change text, notes, pictures, add/delete/move slides, delete shapes, check or embed fonts. "
                + "Read the slide first (ppt_read_slide) to get shape names/ids, use one atomic tool per change, then read back to verify.");
            skill.UseWhen = new List<string>
            {
                "edit", "change text", "replace", "rename", "notes", "insert picture", "add slide", "delete slide", "move slide", "font", "embed",
                "修改", "改成", "改为", "替换", "写入", "备注", "插入图片", "新增一页", "加一页", "删除", "移动", "调换", "顺序",
                "标题", "文字", "嵌入字体", "字体缺失", "另存"
            };
            skill.AllowedToolNames = new List<string>
            {
                "ppt_extract_content", "ppt_set_shape_text", "ppt_format_shapes", "ppt_arrange_shapes", "ppt_delete_shapes",
                "ppt_add_text_box", "ppt_insert_picture", "ppt_set_notes", "ppt_add_slides", "ppt_delete_slides", "ppt_move_slide",
                "ppt_check_fonts", "ppt_save_with_fonts", "ppt_export_slide_image", "ppt_list_backups", "ppt_restore_backup"
            };
            skill.PreferredToolNames = new List<string> { "ppt_read_slide", "ppt_set_shape_text", "ppt_add_slides" };
            skill.VerificationToolNames = new List<string> { "ppt_read_slide", "ppt_list_slides", "ppt_export_slide_image" };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = false;
            return skill;
        }

        private static SkillDefinition CreatePowerPointBeautifySkill()
        {
            var skill = CreatePowerPointSkill(
                "ppt-beautify",
                "PowerPoint Beautify",
                "write",
                "Improve the look of existing slides without changing their meaning: unify fonts and sizes, colors, alignment and spacing. "
                + "Inspect (ppt_read_slide, ppt_export_slide_image), apply ppt_format_shapes / ppt_arrange_shapes, use ppt_execute_vba only for bulk changes across many slides, then export images again to compare.");
            skill.UseWhen = new List<string>
            {
                "beautify", "polish", "layout", "align", "consistent", "color", "style", "design", "spacing",
                "美化", "排版", "对齐", "统一", "配色", "字号", "字体", "好看", "专业", "整齐", "间距", "分布", "格式"
            };
            skill.AllowedToolNames = new List<string>
            {
                "ppt_style_search", "ppt_format_shapes", "ppt_arrange_shapes", "ppt_set_shape_text", "ppt_check_fonts",
                "ppt_export_slide_image", "ppt_execute_vba", "ppt_list_backups", "ppt_restore_backup"
            };
            skill.PreferredToolNames = new List<string> { "ppt_export_slide_image", "ppt_format_shapes", "ppt_arrange_shapes" };
            skill.VerificationToolNames = new List<string> { "ppt_read_slide", "ppt_export_slide_image" };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = true;
            return skill;
        }

        private static SkillDefinition CreatePowerPointStyleAdvisorSkill()
        {
            var skill = CreatePowerPointSkill(
                "ppt-style-advisor",
                "PowerPoint Style Advisor",
                "read",
                "Recommend visual styles, palettes and fonts for a deck. Search the style catalog, show up to 3 previews with ppt_style_preview, "
                + "and explain the trade-offs. Does not modify the presentation.");
            skill.UseWhen = new List<string>
            {
                "style", "theme", "template", "palette", "recommend", "suggest", "which style",
                "风格", "主题", "模板", "配色方案", "推荐", "建议", "什么风格", "适合"
            };
            skill.AllowedToolNames = new List<string>
            {
                "ppt_style_search", "ppt_style_preview", "ppt_extract_content", "ppt_export_slide_image", "ppt_check_fonts"
            };
            skill.PreferredToolNames = new List<string> { "ppt_style_search", "ppt_style_get", "ppt_style_preview" };
            skill.VerificationToolNames = new List<string> { "ppt_style_get" };
            skill.RequireVerification = false;
            skill.RequiresBackup = false;
            skill.AllowHighRiskTools = false;
            skill.AllowCodeExecution = false;
            skill.FallbackSkillNames = new List<string>();
            return skill;
        }

        /// <summary>
        /// 受控分析档：可使用衍生工作表完成计算，但不提供直接改写原表的常规工具。
        /// 名称沿用 read-analysis，因为它同时是 Runtime/Settings 的默认技能名。
        /// </summary>
        private static SkillDefinition CreateExcelAnalysisSkill()
        {
            var skill = CreateExcelSkill(
                "read-analysis",
                "Excel Analysis",
                "read",
                "Answer questions from workbook structure and aggregate results. Prefer in-memory derivations; if needed, use staged VBA output only on _wip_ derived worksheets, with a backup and verification. Keep numeric conclusions to the measurements requested and supported by tool results; avoid unrequested totals, percentages, ranges, samples, error counts, or speculative numerical comparisons. Honor requested decimal precision exactly. Reuse the exact source scope, header mode, filters, groups, and measures when verifying. Do not repeat verification after a tool reports a passing result. For a fully scanned read-only union or lookup with no written result range, do not call excel_assert; there is no output target to verify. Report the result and explicitly retain the tool's Tier C status instead of fabricating a single-sheet or per-source assertion.");

            skill.UseWhen = new List<string>
            {
                "top", "inspect", "analyze", "review", "read", "worksheet", "sheet", "range", "selection", "workbook",
                "max", "min", "average", "sum", "count", "highest", "lowest", "rank", "group by", "subtotal", "aggregate",
                "pivot", "distinct", "profile", "reconcile", "how many", "which", "compare",
                "data model", "power query", "table", "tables", "query", "relationship", "measure",
                "查看", "读取", "分析", "统计", "工作表", "工作簿", "区域", "选区", "最大", "最小", "平均", "求和", "计数",
                "排名", "分类汇总", "分组", "汇总", "去重", "占比", "对账", "核对", "多少", "哪个", "对比",
                "数据模型", "查询", "表格", "度量值"
            };
            skill.AllowedToolNames = new List<string>
            {
                "runtime.get_host_context",
                "runtime.list_tools",
                "excel_get_active_workbook",
                "excel_get_selection",
                "excel_list_worksheets",
                "excel_profile_range",
                "excel_aggregate",
                "excel_reconcile",
                "excel_compare_key_sets",
                "excel_assert",
                "excel_analyze_range",
                "excel_read_range",
                "excel_read_worksheet_data",
                "excel_get_range_info",
                "excel_get_range_style",
                "excel_get_range_number_formats",
                "excel_get_range_validation",
                "excel_get_range_merge_info",
                "excel_get_range_lock_info",
                "excel_get_range_hyperlinks",
                "excel_read_range_formulas",
                "excel_get_named_range_value",
                "excel_list_named_ranges",
                "excel_find_in_range",
                "excel_list_tables",
                "excel_view_table",
                "excel_read_table_data",
                "excel_list_power_queries",
                "excel_view_power_query",
                "excel_check_power_query_errors",
                "excel_validate_power_query",
                "excel_list_connections",
                "excel_view_connection",
                "excel_list_data_model_tables",
                "excel_list_data_model_measures",
                "excel_view_data_model_measure",
                "excel_list_data_model_calculated_columns",
                "excel_view_data_model_calculated_column",
                "excel_list_data_model_relationships",
                "excel_list_vba_modules",
                "excel_view_vba_module",
                "excel_list_backups",
                "excel_capture_worksheet_screenshot"
            };
            skill.PreferredToolNames = new List<string>
            {
                "excel_profile_range",
                "excel_aggregate",
                "excel_reconcile",
                "excel_compare_key_sets",
                "excel_get_range_info",
                "excel_list_tables",
                "excel_get_selection",
                "excel_get_active_workbook",
                "excel_list_worksheets",
                "excel_assert"
            };
            skill.VerificationToolNames = new List<string>
            {
                "excel_assert",
                "excel_reconcile",
                "excel_compare_key_sets",
                "excel_profile_range",
                "excel_aggregate",
                "excel_get_range_info",
                "excel_read_range",
                "excel_read_worksheet_data",
                "excel_get_range_style",
                "excel_get_range_number_formats",
                "excel_capture_worksheet_screenshot",
                "runtime.get_host_context"
            };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            // Strategy flags remain descriptive; Excel write permissions are enforced by the turn contract.
            skill.MaxToolRoundCount = 12;
            return skill;
        }

        /// <summary>
        /// 活结果优先使用公式/透视表，让产物随源数据更新；工具列表和标志只表达策略偏好。
        /// </summary>
        private static SkillDefinition CreateExcelLiveResultSkill()
        {
            var skill = CreateExcelSkill(
                "live-result",
                "Excel Live Result",
                "write",
                "Produce results that must stay linked to the source data: write formulas, validate them, or write literal values into a target range. The output has to recalculate when the source changes, so it must be formulas or values, never a code-generated snapshot.");

            skill.UseWhen = new List<string>
            {
                "formula", "formulas", "calculate", "recalculate", "auto update", "linked", "reference",
                "vlookup", "xlookup", "sumifs", "countifs", "sumproduct", "index match", "named range",
                "insert", "update", "set", "fill", "write", "paste", "add column",
                "公式", "函数", "计算", "引用", "查找函数", "自动更新", "联动", "随之变化", "命名区域",
                "写入", "填充", "填入", "设置值", "录入", "新增列"
            };
            skill.AllowedToolNames = new List<string>
            {
                "runtime.get_host_context",
                "runtime.list_tools",
                "excel_get_active_workbook",
                "excel_get_selection",
                "excel_list_worksheets",
                "excel_get_range_info",
                "excel_profile_range",
                "excel_aggregate",
                "excel_reconcile",
                "excel_assert",
                "excel_read_range",
                "excel_read_worksheet_data",
                "excel_read_range_formulas",
                "excel_list_tables",
                "excel_read_table_data",
                "excel_find_in_range",
                "excel_list_named_ranges",
                "excel_get_named_range_value",
                "excel_write_range",
                "excel_create_worksheet",
                "excel_set_range_formulas",
                "excel_validate_range_formulas",
                "excel_refresh_power_query",
                "excel_list_backups",
                "excel_restore_backup"
            };
            skill.PreferredToolNames = new List<string>
            {
                "excel_profile_range",
                "excel_set_range_formulas",
                "excel_validate_range_formulas",
                "excel_write_range",
                "excel_assert"
            };
            skill.VerificationToolNames = new List<string>
            {
                "excel_assert",
                "excel_validate_range_formulas",
                "excel_read_range_formulas",
                "excel_read_range",
                "excel_reconcile",
                "runtime.get_host_context"
            };
            skill.FallbackSkillNames = new List<string> { "read-analysis" };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = false;
            skill.MaxToolRoundCount = 12;
            return skill;
        }

        /// <summary>
        /// 结构改造涉及数据形状（拆分/合并/去重/重排/搬运）；推荐 VBA 处理专用工具无法覆盖的操作。
        /// </summary>
        private static SkillDefinition CreateExcelStructuralTransformSkill()
        {
            var skill = CreateExcelSkill(
                "structural-transform",
                "Excel Structural Transform",
                "write",
                "Change the shape of the data: split, merge, dedupe, reorder, pivot, or move rows and columns in bulk. Stage the work on _wip_ worksheets with conservation expectations, then commit to the target sheet.");

            skill.UseWhen = new List<string>
            {
                "split", "merge", "dedupe", "deduplicate", "remove duplicates", "sort", "reorder", "transpose",
                "restructure", "reshape", "unpivot", "pivot table", "consolidate", "move rows", "move columns",
                "delete rows", "clear", "overwrite", "replace all", "add sheet", "batch",
                "vba", "macro", "script", "code", "restore", "undo",
                "拆分", "合并", "去重", "排序", "重排", "转置", "重构", "改造", "整理", "加工", "批量",
                "透视表", "汇总到新表", "搬运", "删除行", "删除列", "清除", "清空", "覆盖", "全部替换",
                "宏", "代码", "还原", "撤销"
            };
            skill.AllowedToolNames = new List<string>
            {
                "runtime.get_host_context",
                "runtime.list_tools",
                "excel_get_active_workbook",
                "excel_get_selection",
                "excel_list_worksheets",
                "excel_get_range_info",
                "excel_profile_range",
                "excel_aggregate",
                "excel_reconcile",
                "excel_assert",
                "excel_read_range",
                "excel_read_worksheet_data",
                "excel_read_range_formulas",
                "excel_list_tables",
                "excel_read_table_data",
                "excel_find_in_range",
                "excel_write_range",
                "excel_create_worksheet",
                "excel_set_range_formulas",
                "excel_validate_range_formulas",
                "excel_clear_range",
                "excel_execute_vba",
                "excel_cleanup_workspace",
                "excel_refresh_power_query",
                "excel_list_backups",
                "excel_restore_backup"
            };
            skill.PreferredToolNames = new List<string>
            {
                "excel_execute_vba",
                "excel_profile_range",
                "excel_aggregate",
                "excel_reconcile",
                "excel_write_range",
                "excel_assert"
            };
            skill.VerificationToolNames = new List<string>
            {
                "excel_assert",
                "excel_reconcile",
                "excel_cleanup_workspace",
                "excel_list_worksheets",
                "excel_profile_range",
                "excel_read_range",
                "runtime.get_host_context"
            };
            skill.FallbackSkillNames = new List<string> { "read-analysis" };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = true;
            skill.MaxToolRoundCount = 16;
            return skill;
        }

        /// <summary>
        /// 写入档三：批量格式化。只改外观不改取值，守恒式是「取值一个不许变」，
        /// 验证必须回读 excel_get_range_style 而不是看调用是否抛异常（D8）。
        /// </summary>
        private static SkillDefinition CreateExcelBulkFormatSkill()
        {
            var skill = CreateExcelSkill(
                "bulk-format",
                "Excel Bulk Format",
                "write",
                "Change how cells look without changing what they contain: fill colour, font, borders, alignment, number formats. Values must be identical before and after.");

            skill.UseWhen = new List<string>
            {
                "format", "formatting", "style", "font", "background", "color", "colour", "fill",
                "border", "alignment", "highlight", "bold", "italic", "number format", "conditional formatting",
                "column width", "row height", "freeze panes",
                "格式", "样式", "字体", "背景", "颜色", "填充色", "边框", "对齐", "标红", "加粗", "斜体",
                "数字格式", "条件格式", "列宽", "行高", "冻结窗格", "美化", "排版"
            };
            skill.AllowedToolNames = new List<string>
            {
                "runtime.get_host_context",
                "runtime.list_tools",
                "excel_get_active_workbook",
                "excel_get_selection",
                "excel_list_worksheets",
                "excel_get_range_info",
                "excel_profile_range",
                "excel_assert",
                "excel_read_range",
                "excel_find_in_range",
                "excel_get_range_style",
                "excel_get_range_number_formats",
                "excel_get_range_merge_info",
                "excel_set_range_format",
                "excel_execute_vba",
                "excel_capture_worksheet_screenshot",
                "excel_list_backups",
                "excel_restore_backup"
            };
            skill.PreferredToolNames = new List<string>
            {
                "excel_get_range_style",
                "excel_set_range_format",
                "excel_execute_vba",
                "excel_assert"
            };
            skill.VerificationToolNames = new List<string>
            {
                "excel_assert",
                "excel_get_range_style",
                "excel_get_range_number_formats",
                "excel_read_range",
                "runtime.get_host_context"
            };
            skill.FallbackSkillNames = new List<string> { "read-analysis" };
            skill.RequireVerification = true;
            skill.RequiresBackup = true;
            skill.AllowHighRiskTools = true;
            skill.AllowCodeExecution = true;
            skill.MaxToolRoundCount = 12;
            return skill;
        }

        private static SkillDefinition CreateSkillifySkill()
        {
            var skill = new SkillDefinition
            {
                Name = "skillify",
                DisplayName = "Skill Creator (skillify)",
                Category = "meta",
                Description = "Capture this session's repeatable process into a reusable skill file. Use when the user asks to 'create a skill', 'save this as a skill', or wants to turn a workflow into a reusable skill.",
                MaxToolRoundCount = 20,
                IsEnabled = true,
            };

            skill.UseWhen = new List<string>
            {
                "skill", "create skill", "skillify", "做成.*skill", "保存.*流程", "创建.*skill",
                "save.*skill", "capture.*process", "reusable.*skill", "make.*skill"
            };

            skill.AllowedToolNames = new List<string>
            {
                "runtime.ask_user_question",
                "runtime.create_skill",
                "runtime.list_tools",
                "runtime.invoke_skill"
            };

            skill.Metadata["skill.userInvocable"] = "true";
            skill.Metadata["skill.disableModelInvocation"] = "false";
            skill.Metadata[SkillMetadataKeys.RequireKeywordMatch] = "true";
            skill.Metadata["skill.argumentHint"] = "[description of the process you want to capture]";
            skill.Metadata["skill.context"] = "inline";

            skill.Metadata["skill.prompt"] = GetSkillifyPromptBody();

            return skill;
        }

        private static string GetSkillifyPromptBody()
        {
            return @"# Skillify — Capture Session Process as a Reusable Skill

You are capturing this session's repeatable process as a reusable NixerOfficeCopilot skill.

## Your Task

### Step 1: Analyze the Session

Before asking any questions, analyze the session to identify:
- What repeatable process was performed
- What the inputs/parameters were
- The distinct steps (in order)
- The success artifacts/criteria for each step
- Where the user corrected or steered you
- What tools and permissions were needed
- What the goals and success artifacts were

Review the conversation history you have in your context to extract:
- The user's messages and corrections
- The tools that were actually used
- The sequence of steps that led to the final result

### Step 2: Interview the User

Use `runtime.ask_user_question` for ALL questions! Never ask questions via plain text.
For each round, iterate as much as needed until the user is happy.

**Round 1: High level confirmation**
- Suggest a name and description for the skill based on your analysis. Ask the user to confirm or rename.
- Suggest high-level goal(s) and specific success criteria for the skill.

**Round 2: More details**
- Present the high-level steps you identified as a numbered list.
- If you think the skill will require arguments, suggest arguments based on what you observed.
- Ask if this skill should run inline or forked. Forked is better for self-contained tasks; inline is better when the user wants to steer mid-process.
- Ask where the skill should be saved (personal: %APPDATA%/NixerOfficeCopilot/skills/).

**Round 3: Breaking down each step**
For each major step, ask:
- What does this step produce that later steps need?
- What proves that this step succeeded?
- Should the user confirm before proceeding? (especially for destructive operations)
- Are any steps independent and could run in parallel?
- What are the hard constraints or preferences?

IMPORTANT: Pay special attention to where the user corrected you during the session.

**Round 4: Final questions**
- Confirm when this skill should be invoked, and suggest trigger phrases.
- Ask for any other gotchas or things to watch out for.

Stop interviewing once you have enough information. Don't over-ask for simple processes!

### Step 3: Generate and Save the SKILL.md

After the interview, call `runtime.create_skill` with the gathered information:

```json
{
  ""name"": ""<skill-name>"",
  ""description"": ""<one-line description>"",
  ""body"": ""<full markdown body with Goal, Inputs, Steps>"",
  ""category"": ""<category>"",
  ""allowedTools"": [""<tool1>"", ""<tool2>""],
  ""whenToUse"": ""<when to auto-invoke, with trigger phrases>"",
  ""argumentHint"": ""<hint showing argument placeholders>"",
  ""arguments"": [""<arg1>"", ""<arg2>""],
  ""context"": ""inline or fork"",
  ""userInvocable"": true,
  ""disableModelInvocation"": false
}
```

#### SKILL.md Body Format

The `body` should follow this structure:

```markdown
# <Skill Title>
Description of what this skill does

## Inputs
- `$arg_name`: Description of this input

## Goal
Clearly stated goal for this workflow. Best if you have clearly defined artifacts or criteria for completion.

## Steps

### 1. Step Name
What to do in this step. Be specific and actionable.

**Success criteria**: ALWAYS include this!

### 2. Step Name
...
```

**Per-step annotations** (include where relevant):
- **Success criteria**: REQUIRED on every step. This shows that the step is done and we can move on.
- **Execution**: `Direct` (default), `Task agent`, `Teammate`, or `[human]`
- **Artifacts**: Data this step produces that later steps need
- **Human checkpoint**: When to pause and ask the user before proceeding
- **Rules**: Hard rules for the workflow

**Frontmatter rules for create_skill:**
- `allowedTools`: Minimum permissions needed (use specific tool names)
- `context`: Only set `context: fork` for self-contained skills
- `whenToUse` is CRITICAL — tells the model when to auto-invoke. Start with ""Use when..."" and include trigger phrases.
- `arguments` and `argumentHint`: Only include if the skill takes parameters

### Step 4: Confirm

After `runtime.create_skill` succeeds, tell the user:
- Where the skill was saved
- How to invoke it: describe the task naturally, or the model will auto-invoke based on `whenToUse`
- That they can edit the SKILL.md directly at %APPDATA%/NixerOfficeCopilot/skills/<name>/SKILL.md

Follow the instructions above. When finished, summarize the outcome.";
        }

        private static SkillDefinition CreateRecordToMemorySkill()
        {
            var skill = new SkillDefinition
            {
                Name = "record-to-memory",
                DisplayName = "Save to memory",
                Category = "System tools",
                Description = "Persist information in memory when the user asks to remember or record it.",
                MaxToolRoundCount = 5,
                IsEnabled = true,
            };

            skill.UseWhen = new List<string>
            {
                "记住", "记录", "写入记忆", "帮我记", "备忘",
                "remember", "record", "save.*memory", "memorize", "note.*down"
            };

            skill.AllowedToolNames = new List<string>
            {
                "runtime.record_memory"
            };

            skill.Metadata["skill.userInvocable"] = "true";
            skill.Metadata[SkillMetadataKeys.RequireKeywordMatch] = "true";
            skill.Metadata["skill.argumentHint"] = "[content to remember]";

            skill.Metadata["skill.prompt"] = GetRecordToMemoryPromptBody();

            return skill;
        }

        private static string GetRecordToMemoryPromptBody()
        {
            return @"# Save to Memory Skill

## Goal
When the user asks to remember or record information, persist it in the NixerOfficeCopilot memory system.

## Steps

### 1. Understand the request
Extract the following from the user's request:
- **Content**: the information the user explicitly asked to remember
- **Tags**: keywords describing the content, such as ""preference"", ""rule"", ""project"", or ""user information""
- **Scope**: default to `user` (user-wide, across all workspaces)

### 2. Call the memory tool
Use `runtime.record_memory` to save the information:

```json
{
  ""content"": ""<content the user asked to remember>"",
  ""tags"": [""<keyword1>"", ""<keyword2>""],
  ""scope"": ""user""
}
```

### 3. Confirm the result
After the tool runs, tell the user whether the memory was saved successfully.";
        }

        private static SkillDefinition CreateExcelSkill(
            string name,
            string displayName,
            string category,
            string description)
        {
            var skill = new SkillDefinition
            {
                Name = name ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                Category = category ?? string.Empty,
                Description = description ?? string.Empty,
                MaxToolRoundCount = 10
            };

            skill.Metadata[SkillMetadataKeys.HostKind] = "excel";
            skill.Metadata[SkillMetadataKeys.Scope] = category ?? string.Empty;
            return skill;
        }
    }
}
