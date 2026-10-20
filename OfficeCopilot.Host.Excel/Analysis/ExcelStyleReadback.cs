using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace OfficeCopilot.Host.Excel.Analysis
{
    /// <summary>
    /// 区域格式回读的纯计算部分：把 Excel COM 属性的原始返回值翻译成模型可判定的字符串。
    /// Excel 对多单元格区域中取值不一致的属性一律返回 null，本类把该语义显式化为 "mixed"，
    /// 避免调用方把"读不到"当成"没有设置"。
    /// </summary>
    public static class ExcelStyleReadback
    {
        public const string Mixed = "mixed";
        public const string Unknown = "unknown";
        public const string NoFill = "none";
        public const string NoBorder = "none";

        private const int XlNone = -4142;
        private const int XlLineStyleNone = -4142;

        public static bool IsInconclusive(string value)
        {
            return string.Equals(value, Mixed, StringComparison.Ordinal)
                || string.Equals(value, Unknown, StringComparison.Ordinal);
        }

        /// <summary>OLE 整数色值转 #RRGGBB。<paramref name="colorIndex"/> 为 xlNone 时返回 "none"。</summary>
        public static string FromOleColor(object colorValue, object colorIndex)
        {
            int index;
            if (TryToInt32(colorIndex, out index) && index == XlNone)
            {
                return NoFill;
            }

            if (colorValue == null || colorValue is DBNull)
            {
                return Mixed;
            }

            int ole;
            if (!TryToInt32(colorValue, out ole))
            {
                return Unknown;
            }

            var color = ColorTranslator.FromOle(ole);
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static string FromBoolean(object value)
        {
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false";
            }
            catch (Exception)
            {
                return Unknown;
            }
        }

        /// <summary>下划线：xlUnderlineStyleNone 记为 false，其余非空值记为 true。</summary>
        public static string FromUnderline(object value)
        {
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            int style;
            if (!TryToInt32(value, out style))
            {
                return FromBoolean(value);
            }

            return style == XlNone ? "false" : "true";
        }

        public static string FromNumber(object value)
        {
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("0.##", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return Unknown;
            }
        }

        public static string FromText(object value)
        {
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(text) ? Mixed : text;
        }

        public static string DescribeHorizontalAlignment(object value)
        {
            int code;
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            if (!TryToInt32(value, out code))
            {
                return Unknown;
            }

            switch (code)
            {
                case 1: return "general";
                case -4131: return "left";
                case -4108: return "center";
                case -4152: return "right";
                case 5: return "fill";
                case -4130: return "justify";
                case 7: return "centerAcrossSelection";
                case -4117: return "distributed";
                default: return code.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string DescribeVerticalAlignment(object value)
        {
            int code;
            if (value == null || value is DBNull)
            {
                return Mixed;
            }

            if (!TryToInt32(value, out code))
            {
                return Unknown;
            }

            switch (code)
            {
                case -4160: return "top";
                case -4108: return "center";
                case -4107: return "bottom";
                case -4130: return "justify";
                case -4117: return "distributed";
                default: return code.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 汇总四条外边框。<paramref name="edgeLineStyles"/> 顺序为 left/top/bottom/right，
        /// 元素为 <c>Border.LineStyle</c> 原值（null 表示该边在区域内不一致）。
        /// </summary>
        public static string SummarizeBorders(IList<object> edgeLineStyles)
        {
            if (edgeLineStyles == null || edgeLineStyles.Count != 4)
            {
                return Unknown;
            }

            var names = new[] { "left", "top", "bottom", "right" };
            var present = new List<string>();
            var mixedEdges = new List<string>();

            for (var i = 0; i < 4; i++)
            {
                var raw = edgeLineStyles[i];
                if (raw == null || raw is DBNull)
                {
                    mixedEdges.Add(names[i]);
                    continue;
                }

                int lineStyle;
                if (!TryToInt32(raw, out lineStyle))
                {
                    mixedEdges.Add(names[i]);
                    continue;
                }

                if (lineStyle != XlLineStyleNone)
                {
                    present.Add(names[i]);
                }
            }

            if (mixedEdges.Count == 4)
            {
                return Mixed;
            }

            var summary = present.Count == 0 ? NoBorder : string.Join(",", present.ToArray());
            if (mixedEdges.Count > 0)
            {
                summary += "（" + string.Join(",", mixedEdges.ToArray()) + " 不一致）";
            }

            return summary;
        }

        /// <summary>收集取值为 mixed/unknown 的字段名，让模型一眼看出哪些结论不能下。</summary>
        public static string CollectInconclusiveProperties(IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var names = new List<string>();
            foreach (var pair in values)
            {
                if (IsInconclusive(pair.Value))
                {
                    names.Add(pair.Key);
                }
            }

            return string.Join(",", names.ToArray());
        }

        private static bool TryToInt32(object value, out int result)
        {
            result = 0;
            if (value == null || value is DBNull)
            {
                return false;
            }

            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
