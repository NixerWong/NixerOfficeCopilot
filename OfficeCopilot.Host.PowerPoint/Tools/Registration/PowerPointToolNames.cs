using System;
using System.Collections.Generic;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public static class PowerPointToolGroups
    {
        public const string Context = "ppt.context";
        public const string Read = "ppt.read";
        public const string Backup = "ppt.backup";
        public const string Style = "ppt.style";
        public const string Deck = "ppt.deck";
        public const string File = "ppt.file";
        public const string Write = "ppt.write";
        public const string Vba = "ppt.vba";

        public const string LegacyContext = "powerpoint.context";
        public const string LegacyFile = "powerpoint.file";

        private static readonly Dictionary<string, string[]> LegacyGroupMap =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // 旧 context 组同时覆盖了"读演示文稿"的职责，拆分后映射到两个新组。
                { LegacyContext, new[] { Context, Read } },
                { LegacyFile, new[] { File } }
            };

        public static IReadOnlyList<string> All
        {
            get { return new[] { Context, Read, Write, Backup, Style, Deck, File, Vba }; }
        }

        /// <summary>旧分组名映射到新分组名；非旧名原样返回单元素数组。</summary>
        public static string[] MapLegacy(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return new string[0];
            }

            string[] mapped;
            return LegacyGroupMap.TryGetValue(groupName.Trim(), out mapped) ? mapped : new[] { groupName.Trim() };
        }
    }

    public static class PowerPointToolNames
    {
        public const string GetActivePresentation = "ppt_get_active_presentation";
        public const string GetSelection = "ppt_get_selection";
        public const string ListSlides = "ppt_list_slides";
        public const string ReadSlide = "ppt_read_slide";
        public const string ListBackups = "ppt_list_backups";
        public const string RestoreBackup = "ppt_restore_backup";
        public const string StyleSearch = "ppt_style_search";
        public const string StyleGet = "ppt_style_get";
        public const string StylePreview = "ppt_style_preview";
        public const string DeckCreate = "ppt_deck_create";
        public const string DeckPutSlides = "ppt_deck_put_slides";
        public const string DeckGetSlides = "ppt_deck_get_slides";
        public const string DeckRemoveSlides = "ppt_deck_remove_slides";
        public const string DeckReorder = "ppt_deck_reorder";
        public const string DeckPreview = "ppt_deck_preview";
        public const string DeckValidate = "ppt_deck_validate";
        public const string DeckBuild = "ppt_deck_build";
        public const string ExtractContent = "ppt_extract_content";
        public const string ExportSlideImage = "ppt_export_slide_image";
        public const string CheckFonts = "ppt_check_fonts";
        public const string SetShapeText = "ppt_set_shape_text";
        public const string FormatShapes = "ppt_format_shapes";
        public const string ArrangeShapes = "ppt_arrange_shapes";
        public const string DeleteShapes = "ppt_delete_shapes";
        public const string AddTextBox = "ppt_add_text_box";
        public const string InsertPicture = "ppt_insert_picture";
        public const string SetNotes = "ppt_set_notes";
        public const string AddSlides = "ppt_add_slides";
        public const string DeleteSlides = "ppt_delete_slides";
        public const string MoveSlide = "ppt_move_slide";
        public const string SaveWithFonts = "ppt_save_with_fonts";
        public const string SaveCopy = "ppt_save_copy";
        public const string ExecuteVba = "ppt_execute_vba";

        private static readonly Dictionary<string, string> LegacyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "powerpoint_get_active_presentation", GetActivePresentation },
                { "powerpoint_get_selection", GetSelection }
            };

        /// <summary>旧名 → 新名。别名长期保留（蓝图 Q6）。</summary>
        public static IReadOnlyDictionary<string, string> LegacyAliases
        {
            get { return LegacyNames; }
        }

        public static string MapLegacy(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return string.Empty;
            }

            string mapped;
            var trimmed = toolName.Trim();
            return LegacyNames.TryGetValue(trimmed, out mapped) ? mapped : trimmed;
        }
    }
}
