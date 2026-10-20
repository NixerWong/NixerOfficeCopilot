using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Word.Formatting;
using OfficeCopilot.Host.Word.Tools;

namespace OfficeCopilot.Agent.Tests.Word
{
    [TestClass]
    public class WordParagraphFormatPlannerTests
    {
        [DataTestMethod]
        [DataRow("Title", -63)]
        [DataRow("Heading1", -2)]
        [DataRow("Heading 1", -2)]
        [DataRow("heading_2", -3)]
        [DataRow("标题 1", -2)]
        [DataRow("标题", -63)]
        [DataRow("正文", -1)]
        [DataRow("Normal", -1)]
        public void TryResolveBuiltinStyle_AcceptsEnglishAndChineseAliases(string name, int expected)
        {
            int styleId;
            Assert.IsTrue(WordParagraphFormatPlanner.TryResolveBuiltinStyle(name, out styleId), name);
            Assert.AreEqual(expected, styleId, name);
        }

        [TestMethod]
        public void TryResolveBuiltinStyle_RejectsUnknownName()
        {
            int styleId;
            Assert.IsFalse(WordParagraphFormatPlanner.TryResolveBuiltinStyle("MyCustomStyle", out styleId));
        }

        [TestMethod]
        public void Validate_RejectsMissingRolesUnknownRoleAndDuplicates()
        {
            StringAssert.Contains(WordParagraphFormatPlanner.Validate(new WordFormatParagraphsRequest()), "roles");

            var unknownStyle = CreateRequest();
            unknownStyle.Roles["h1"].BuiltinStyle = "Heading42";
            StringAssert.Contains(WordParagraphFormatPlanner.Validate(unknownStyle), "Heading42");

            var undefinedRole = CreateRequest();
            undefinedRole.Paragraphs.Add(new WordParagraphRoleAssignment { Index = 3, Role = "h9" });
            StringAssert.Contains(WordParagraphFormatPlanner.Validate(undefinedRole), "h9");

            var duplicated = CreateRequest();
            duplicated.Paragraphs.Add(new WordParagraphRoleAssignment { Index = 1, Role = "h1" });
            StringAssert.Contains(WordParagraphFormatPlanner.Validate(duplicated), "more than once");

            var badDefault = CreateRequest();
            badDefault.DefaultRole = "missing";
            StringAssert.Contains(WordParagraphFormatPlanner.Validate(badDefault), "defaultRole");

            Assert.IsNull(WordParagraphFormatPlanner.Validate(CreateRequest()));
        }

        [TestMethod]
        public void BuildPlan_SkipsExpectMismatchAndOutOfRange_DefaultRoleSkipsEmptyAndTables()
        {
            var request = CreateRequest();
            request.Paragraphs.Add(new WordParagraphRoleAssignment { Index = 2, Role = "h1", Expect = "二、方法" });
            request.Paragraphs.Add(new WordParagraphRoleAssignment { Index = 9, Role = "h1" });

            var snapshots = new List<WordParagraphSnapshot>
            {
                new WordParagraphSnapshot { Index = 1, Text = "一、 引言\r" },
                new WordParagraphSnapshot { Index = 2, Text = "正文第一段" },
                new WordParagraphSnapshot { Index = 3, Text = "  \r" },
                new WordParagraphSnapshot { Index = 4, Text = "单元格", InTable = true },
                new WordParagraphSnapshot { Index = 5, Text = "正文第二段" }
            };

            var plan = WordParagraphFormatPlanner.BuildPlan(request, snapshots);

            CollectionAssert.AreEqual(new[] { 1, 5 }, plan.Items.Select(i => i.Index).ToArray());
            Assert.AreEqual("h1", plan.Items[0].Role, "expect 忽略空白，应匹配。");
            Assert.AreEqual("body", plan.Items[1].Role);
            CollectionAssert.AreEqual(new[] { 2, 9 }, plan.Skipped.Select(s => s.Index).ToArray());
            StringAssert.Contains(plan.Skipped[0].Reason, "expect mismatch");
            StringAssert.Contains(plan.Skipped[1].Reason, "out of range");
        }

        [TestMethod]
        public void BuildSummary_ListsSkippedAndAsksToFixOnlyThose()
        {
            var summary = WordParagraphFormatPlanner.BuildSummary(new WordFormatParagraphsResult
            {
                Success = true,
                TotalParagraphs = 10,
                AppliedCount = 9,
                RoleCounts = new Dictionary<string, int> { ["h1"] = 2, ["body"] = 7 },
                Skipped = new List<WordParagraphFormatSkip> { new WordParagraphFormatSkip { Index = 4, Reason = "expect mismatch" } },
                UndoGrouped = true
            });

            StringAssert.Contains(summary, "Formatted 9 of 10");
            StringAssert.Contains(summary, "#4 expect mismatch");
            StringAssert.Contains(summary, "do not redo the whole batch");
            StringAssert.Contains(summary, "single Ctrl+Z");
        }

        [DataTestMethod]
        [DataRow("一、引言\r", "一、引言", true)]
        [DataRow("一、引言\r", "", true)]
        [DataRow("一、引言\r", "一、引言\r", false)]
        [DataRow("一、引言\r", "一、引言\n", false)]
        [DataRow("引言", "前言", false)]
        public void ShouldKeepTrailingParagraphMark_PreventsMergingWithNextParagraph(string selected, string replacement, bool expected)
        {
            Assert.AreEqual(expected, WordParagraphFormatPlanner.ShouldKeepTrailingParagraphMark(selected, replacement));
        }

        [TestMethod]
        public void BuildSummary_NoSkipped_TellsModelFormattingIsComplete()
        {
            var summary = WordParagraphFormatPlanner.BuildSummary(new WordFormatParagraphsResult
            {
                Success = true,
                TotalParagraphs = 45,
                AppliedCount = 40,
                Skipped = new List<WordParagraphFormatSkip>()
            });

            StringAssert.Contains(summary, "formatting is complete");
            StringAssert.Contains(summary, "Do not re-apply");
        }

        [TestMethod]
        public void ParseRequest_ReadsRolesParagraphsAndFlags()
        {
            var args = JObject.Parse(@"{
                ""roles"": {
                    ""title"": { ""builtinStyle"": ""Title"", ""fontName"": ""黑体"", ""size"": 22, ""bold"": true, ""alignment"": ""center"" },
                    ""body"": { ""builtinStyle"": ""Normal"", ""fontName"": ""宋体"", ""latinFontName"": ""Times New Roman"", ""size"": 12, ""firstLineIndentChars"": 2, ""lineSpacingRule"": ""1.5"" }
                },
                ""paragraphs"": [ { ""index"": 1, ""role"": ""title"", ""expect"": ""基于"" } ],
                ""defaultRole"": ""body"",
                ""clearDirectFormatting"": true
            }");

            var request = WordFormatParagraphsTool.ParseRequest(args);

            Assert.AreEqual(2, request.Roles.Count);
            Assert.IsTrue(request.Roles.ContainsKey("TITLE"), "角色名不区分大小写。");
            Assert.AreEqual(22d, request.Roles["title"].Size);
            Assert.AreEqual(true, request.Roles["title"].Bold);
            Assert.AreEqual("Times New Roman", request.Roles["body"].LatinFontName);
            Assert.AreEqual(2d, request.Roles["body"].FirstLineIndentChars);
            Assert.AreEqual(1, request.Paragraphs.Count);
            Assert.AreEqual("基于", request.Paragraphs[0].Expect);
            Assert.AreEqual("body", request.DefaultRole);
            Assert.IsTrue(request.ClearDirectFormatting);
            Assert.IsFalse(request.UpdateStyleDefinitions);
            Assert.IsNull(WordParagraphFormatPlanner.Validate(request));
        }

        private static WordFormatParagraphsRequest CreateRequest()
        {
            var request = new WordFormatParagraphsRequest { DefaultRole = "body" };
            request.Roles["h1"] = new WordParagraphRoleFormat { BuiltinStyle = "Heading1", Size = 16 };
            request.Roles["body"] = new WordParagraphRoleFormat { BuiltinStyle = "Normal", Size = 12 };
            request.Paragraphs.Add(new WordParagraphRoleAssignment { Index = 1, Role = "h1", Expect = "一、引言" });
            return request;
        }
    }
}
