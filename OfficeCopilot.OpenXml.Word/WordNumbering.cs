using System;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OfficeCopilot.OpenXml.Word
{
    /// <summary>Options for creating a multi-level list (numbering) definition.</summary>
    public sealed class NumberingListOptions
    {
        /// <summary>Level-0 numbering format. Common: decimal, bullet, lowerRoman, upperRoman, lowerLetter, upperLetter.</summary>
        public string Format { get; set; } = "decimal";

        /// <summary>hybridMultilevel | multilevel | singleLevel.</summary>
        public string Type { get; set; } = "hybridMultilevel";

        public string Name { get; set; }
        public string StyleLink { get; set; }
        public string NumStyleLink { get; set; }

        /// <summary>Level-0 start value (default 1).</summary>
        public int? Start { get; set; } = 1;

        /// <summary>Optional explicit numId (auto-assigned when omitted).</summary>
        public int? NumId { get; set; }

        /// <summary>Optional explicit abstractNumId (auto-assigned when omitted).</summary>
        public int? AbstractNumId { get; set; }

        /// <summary>True to continue counting from a sibling list sharing the same abstractNum (Word default).</summary>
        public bool ContinueNumbering { get; set; }
    }

    /// <summary>
    /// Creates numbering definitions (abstractNum + num) and applies them to
    /// paragraphs. Ported from officecli WordHandler.AddNum / AddAbstractNum /
    /// BuildAbstractNumElement (C# 7.3 / netstandard2.0 adaptation).
    /// </summary>
    internal static class WordNumbering
    {
        /// <summary>
        /// Create a numbering list definition: an abstractNum template (9 levels)
        /// plus a num instance. Returns the numId to apply to paragraphs.
        /// </summary>
        public static int AddNumberingList(WordprocessingDocument doc, NumberingListOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var mainPart = doc.MainDocumentPart;
            if (mainPart == null || mainPart.Document == null)
                throw new InvalidOperationException("Main document part is missing.");

            var numberingPart = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
            if (numberingPart.Numbering == null)
                numberingPart.Numbering = new Numbering();
            var numbering = numberingPart.Numbering;

            // abstractNumId assignment.
            int abstractNumId;
            if (options.AbstractNumId.HasValue)
            {
                abstractNumId = options.AbstractNumId.Value;
                if (abstractNumId < 0)
                    throw new ArgumentException("abstractNumId must be >= 0.");
                if (numbering.Elements<AbstractNum>().Any(a => a.AbstractNumberId != null && a.AbstractNumberId.Value == abstractNumId))
                    throw new ArgumentException(string.Format("abstractNumId {0} already exists.", abstractNumId));
            }
            else
            {
                abstractNumId = numbering.Elements<AbstractNum>()
                    .Select(a => a.AbstractNumberId != null ? a.AbstractNumberId.Value : -1)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;
            }

            BuildAbstractNum(numbering, abstractNumId, options);
            int numId = AddNum(numbering, abstractNumId, options.NumId, options.Start, options.ContinueNumbering);

            numberingPart.Numbering.Save();
            return numId;
        }

        /// <summary>Apply a numbering id + level to a body paragraph by 0-based index.</summary>
        public static void ApplyNumberingToBodyParagraph(WordprocessingDocument doc, int paragraphIndex, int numId, int level)
        {
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null || mainPart.Document == null)
                throw new InvalidOperationException("Main document part is missing.");

            var para = mainPart.Document.Body.Elements<Paragraph>().ElementAtOrDefault(paragraphIndex);
            if (para == null)
                throw new ArgumentException(string.Format("Paragraph index {0} not found.", paragraphIndex));
            ApplyToParagraph(para, numId, level);
        }

        /// <summary>Apply a numbering id + level directly to a paragraph element.</summary>
        public static void ApplyToParagraph(Paragraph para, int numId, int level)
        {
            if (para == null)
                throw new ArgumentNullException(nameof(para));
            if (level < 0 || level > 8)
                throw new ArgumentException("level must be in range 0..8.", nameof(level));

            var pPr = para.ParagraphProperties;
            if (pPr == null)
            {
                pPr = new ParagraphProperties();
                para.PrependChild(pPr);
            }

            var numPr = new NumberingProperties();
            numPr.AppendChild(new NumberingLevelReference { Val = level });
            numPr.AppendChild(new NumberingId { Val = numId });

            // Typed setter maintains CT_PPr schema order (numPr after pStyle etc.).
            pPr.NumberingProperties = numPr;
        }

        private static void BuildAbstractNum(Numbering numbering, int abstractNumId, NumberingListOptions options)
        {
            var abstractNum = new AbstractNum { AbstractNumberId = abstractNumId };

            abstractNum.AppendChild(new MultiLevelType { Val = ParseMultiLevelType(options.Type) });

            if (!string.IsNullOrEmpty(options.Name))
                abstractNum.AppendChild(new AbstractNumDefinitionName { Val = options.Name });
            if (!string.IsNullOrEmpty(options.StyleLink))
                abstractNum.AppendChild(new StyleLink { Val = options.StyleLink });
            if (!string.IsNullOrEmpty(options.NumStyleLink))
                abstractNum.AppendChild(new NumberingStyleLink { Val = options.NumStyleLink });

            var topFormat = string.IsNullOrEmpty(options.Format) ? null : options.Format.ToLowerInvariant();
            bool topIsBullet = topFormat == "bullet" || topFormat == "unordered" || topFormat == "ul";
            var bulletChars = new[] { "•", "◦", "▪" };

            for (int lvl = 0; lvl < 9; lvl++)
            {
                var level = new Level { LevelIndex = lvl };

                string levelFormat;
                if (topIsBullet)
                    levelFormat = "bullet";
                else if (!string.IsNullOrEmpty(topFormat))
                    levelFormat = topFormat;   // propagate the explicit top-level format to every level
                else if (lvl % 3 == 0)
                    levelFormat = "decimal";
                else if (lvl % 3 == 1)
                    levelFormat = "lowerLetter";
                else
                    levelFormat = "lowerRoman";

                var numFmt = ParseNumberFormat(levelFormat);
                bool isBullet = numFmt == NumberFormatValues.Bullet;

                int start = (lvl == 0 && options.Start.HasValue) ? options.Start.Value : 1;
                level.AppendChild(new StartNumberingValue { Val = start });
                level.AppendChild(new NumberingFormat { Val = numFmt });

                string lvlText = isBullet ? bulletChars[lvl % bulletChars.Length] : "%" + (lvl + 1) + ".";
                level.AppendChild(new LevelText { Val = lvlText });

                level.AppendChild(new LevelJustification { Val = LevelJustificationValues.Left });

                int leftIndent = (lvl + 1) * 720;
                int hanging = 360;
                level.AppendChild(new PreviousParagraphProperties(
                    new Indentation { Left = leftIndent.ToString(), Hanging = hanging.ToString() }));

                abstractNum.AppendChild(level);
            }

            // Schema requires AbstractNum elements before NumberingInstance elements.
            var firstNum = numbering.GetFirstChild<NumberingInstance>();
            if (firstNum != null)
                numbering.InsertBefore(abstractNum, firstNum);
            else
                numbering.AppendChild(abstractNum);
        }

        private static int AddNum(Numbering numbering, int abstractNumId, int? explicitNumId, int? start, bool continueNumbering)
        {
            int numId;
            if (explicitNumId.HasValue)
            {
                numId = explicitNumId.Value;
                if (numId < 1)
                    throw new ArgumentException("numId must be >= 1 (0 is reserved as 'no numbering').");
                if (numbering.Elements<NumberingInstance>().Any(n => n.NumberID != null && n.NumberID.Value == numId))
                    throw new ArgumentException(string.Format("numId {0} already exists.", numId));
            }
            else
            {
                numId = numbering.Elements<NumberingInstance>()
                    .Select(n => n.NumberID != null ? n.NumberID.Value : 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1;
            }

            var newNum = new NumberingInstance { NumberID = numId };
            newNum.AppendChild(new AbstractNumId { Val = abstractNumId });

            // By default inject a level-0 startOverride so a new num instance is an
            // independent counter (Word's literal behavior would continue counting).
            if (!continueNumbering)
            {
                int defaultStart = start ?? ResolveLevel0Start(numbering, abstractNumId);
                var lvlOverride = new LevelOverride { LevelIndex = 0 };
                lvlOverride.AppendChild(new StartOverrideNumberingValue { Val = defaultStart });
                newNum.AppendChild(lvlOverride);
            }

            // A new <w:num> must precede a trailing <w:numIdMacAtCleanup>.
            var macCleanup = numbering.GetFirstChild<NumberingIdMacAtCleanup>();
            if (macCleanup != null)
                numbering.InsertBefore(newNum, macCleanup);
            else
                numbering.AppendChild(newNum);

            return numId;
        }

        private static int ResolveLevel0Start(Numbering numbering, int abstractNumId)
        {
            var abs = numbering.Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId != null && a.AbstractNumberId.Value == abstractNumId);
            if (abs == null)
                return 1;

            var lvl0 = abs.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == 0);
            if (lvl0 == null)
                return 1;

            var start = lvl0.StartNumberingValue;
            return start != null && start.Val != null ? start.Val.Value : 1;
        }

        private static MultiLevelValues ParseMultiLevelType(string value)
        {
            var v = (value ?? "hybridMultilevel").ToLowerInvariant();
            if (v == "hybridmultilevel" || v == "hybrid")
                return MultiLevelValues.HybridMultilevel;
            if (v == "multilevel" || v == "multi")
                return MultiLevelValues.Multilevel;
            if (v == "singlelevel" || v == "single")
                return MultiLevelValues.SingleLevel;
            throw new ArgumentException(string.Format("Unknown multiLevelType '{0}'. Valid: hybridMultilevel, multilevel, singleLevel.", value));
        }

        private static NumberFormatValues ParseNumberFormat(string value)
        {
            var lower = value.ToLowerInvariant();
            switch (lower)
            {
                case "decimal": return NumberFormatValues.Decimal;
                case "lowerroman": return NumberFormatValues.LowerRoman;
                case "upperroman": return NumberFormatValues.UpperRoman;
                case "lowerletter":
                case "loweralpha": return NumberFormatValues.LowerLetter;
                case "upperletter":
                case "upperalpha": return NumberFormatValues.UpperLetter;
                case "arabicalpha": return NumberFormatValues.ArabicAlpha;
                case "arabicabjad": return NumberFormatValues.ArabicAbjad;
                case "hindivowels": return NumberFormatValues.HindiVowels;
                case "hindiconsonants": return NumberFormatValues.HindiConsonants;
                case "hindinumbers": return NumberFormatValues.HindiNumbers;
                case "hindicounting": return NumberFormatValues.HindiCounting;
                case "thailetters": return NumberFormatValues.ThaiLetters;
                case "thainumbers": return NumberFormatValues.ThaiNumbers;
                case "thaicounting": return NumberFormatValues.ThaiCounting;
                case "chinesecounting": return NumberFormatValues.ChineseCounting;
                case "chinesecountingthousand": return NumberFormatValues.ChineseCountingThousand;
                case "chineselegalsimplified": return NumberFormatValues.ChineseLegalSimplified;
                case "japanesecounting": return NumberFormatValues.JapaneseCounting;
                case "japaneselegal": return NumberFormatValues.JapaneseLegal;
                case "japanesedigitalten":
                case "japanesedigitaltenthousand": return NumberFormatValues.JapaneseDigitalTenThousand;
                case "koreancounting": return NumberFormatValues.KoreanCounting;
                case "koreanlegal": return NumberFormatValues.KoreanLegal;
                case "koreandigital": return NumberFormatValues.KoreanDigital;
                case "ideographdigital": return NumberFormatValues.IdeographDigital;
                case "ideographtraditional": return NumberFormatValues.IdeographTraditional;
                case "ideographzodiac": return NumberFormatValues.IdeographZodiac;
                case "ideographenclosedcircle": return NumberFormatValues.IdeographEnclosedCircle;
                case "hebrew1": return NumberFormatValues.Hebrew1;
                case "hebrew2": return NumberFormatValues.Hebrew2;
                case "bahttext": return NumberFormatValues.BahtText;
                case "dollartext": return NumberFormatValues.DollarText;
                case "iroha": return NumberFormatValues.Iroha;
                case "irohafullwidth": return NumberFormatValues.IrohaFullWidth;
                case "ganada": return NumberFormatValues.Ganada;
                case "ordinal": return NumberFormatValues.Ordinal;
                case "cardinaltext": return NumberFormatValues.CardinalText;
                case "ordinaltext": return NumberFormatValues.OrdinalText;
                case "bullet":
                case "unordered":
                case "ul": return NumberFormatValues.Bullet;
                case "decimalzero": return NumberFormatValues.DecimalZero;
                case "decimalfullwidth": return NumberFormatValues.DecimalFullWidth;
                case "decimalenclosedcircle": return NumberFormatValues.DecimalEnclosedCircle;
                case "decimalenclosedcirclechinese": return NumberFormatValues.DecimalEnclosedCircleChinese;
                case "decimalenclosedfullstop": return NumberFormatValues.DecimalEnclosedFullstop;
                case "decimalenclosedparen": return NumberFormatValues.DecimalEnclosedParen;
                case "numberindash": return NumberFormatValues.NumberInDash;
                case "hex": return NumberFormatValues.Hex;
                case "chicago": return NumberFormatValues.Chicago;
                case "decimalhalfwidth": return NumberFormatValues.DecimalHalfWidth;
                case "decimalfullwidth2": return NumberFormatValues.DecimalFullWidth2;
                case "aiueo": return NumberFormatValues.Aiueo;
                case "aiueofullwidth": return NumberFormatValues.AiueoFullWidth;
                case "chosung": return NumberFormatValues.Chosung;
                case "koreandigital2": return NumberFormatValues.KoreanDigital2;
                case "ideographzodiactraditional": return NumberFormatValues.IdeographZodiacTraditional;
                case "ideographlegaltraditional": return NumberFormatValues.IdeographLegalTraditional;
                case "taiwanesecounting": return NumberFormatValues.TaiwaneseCounting;
                case "taiwanesecountingthousand": return NumberFormatValues.TaiwaneseCountingThousand;
                case "taiwanesedigital": return NumberFormatValues.TaiwaneseDigital;
                case "vietnamesecounting": return NumberFormatValues.VietnameseCounting;
                case "russianlower": return NumberFormatValues.RussianLower;
                case "russianupper": return NumberFormatValues.RussianUpper;
                case "custom": return NumberFormatValues.Custom;
                case "none": return NumberFormatValues.None;
                default:
                    throw new ArgumentException(string.Format(
                        "Unknown numbering format '{0}'. Common values: decimal, lowerRoman, upperRoman, lowerLetter, upperLetter, bullet.", value));
            }
        }
    }
}
