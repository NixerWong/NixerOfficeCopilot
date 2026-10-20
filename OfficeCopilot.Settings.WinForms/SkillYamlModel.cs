using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// SKILL.md YAML frontmatter 的可编辑模型，绑定到 PropertyGridControl。
    /// Name 和 Description 在独立文本框编辑，其余字段在此模型中。
    /// </summary>
    public sealed class SkillYamlModel
    {
        public SkillYamlModel()
        {
            DisplayName = string.Empty;
            Category = string.Empty;
            UseWhen = new List<string>();
            AvoidWhen = new List<string>();
            AllowedTools = new List<string>();
            PreferredTools = new List<string>();
            VerificationTools = new List<string>();
            CompanionSkills = new List<string>();
            FallbackSkills = new List<string>();
            MaxToolRoundCount = 3;
            Enabled = true;
        }

        [Category("基本信息")]
        [DisplayName("启用")]
        [Description("关闭后此 Skill 不会被加载或触发")]
        public bool Enabled { get; set; }

        [Category("基本信息")]
        [DisplayName("显示名称")]
        [Description("Skill 的显示名称，不填则使用 Name")]
        public string DisplayName { get; set; }

        [Category("基本信息")]
        [DisplayName("分类")]
        [Description("Skill 分类，如 network, file, office, utility")]
        public string Category { get; set; }

        [Category("触发条件")]
        [DisplayName("触发关键词")]
        [Description("匹配这些关键词时自动触发 Skill（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> UseWhen { get; set; }

        [Category("触发条件")]
        [DisplayName("抑制关键词")]
        [Description("包含这些关键词时不触发此 Skill（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> AvoidWhen { get; set; }

        [Category("工具权限")]
        [DisplayName("允许的工具")]
        [Description("此 Skill 可使用的工具白名单（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> AllowedTools { get; set; }

        [Category("工具权限")]
        [DisplayName("优先工具")]
        [Description("此 Skill 优先使用的工具（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> PreferredTools { get; set; }

        [Category("工具权限")]
        [DisplayName("验证工具")]
        [Description("用于验证结果的工具（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> VerificationTools { get; set; }

        [Category("关联 Skill")]
        [DisplayName("配套 Skill")]
        [Description("协同使用的 Skill 名称列表（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> CompanionSkills { get; set; }

        [Category("关联 Skill")]
        [DisplayName("降级 Skill")]
        [Description("当前 Skill 失败时降级使用的 Skill（逗号分隔）")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> FallbackSkills { get; set; }

        [Category("安全策略")]
        [DisplayName("需要用户确认")]
        [Description("执行前是否需要用户确认")]
        public bool RequiresConfirmation { get; set; }

        [Category("安全策略")]
        [DisplayName("需要备份")]
        [Description("执行前是否需要创建备份")]
        public bool RequiresBackup { get; set; }

        [Category("安全策略")]
        [DisplayName("需要验证")]
        [Description("执行后是否需要验证结果")]
        public bool RequireVerification { get; set; }

        [Category("安全策略")]
        [DisplayName("允许高风险工具")]
        [Description("是否允许使用高风险工具")]
        public bool AllowHighRiskTools { get; set; }

        [Category("安全策略")]
        [DisplayName("允许 VBA 降级")]
        [Description("是否允许 VBA 降级执行")]
        public bool AllowCodeExecution { get; set; }

        [Category("高级")]
        [DisplayName("最大工具调用轮数")]
        [Description("Skill 执行期间最多调用工具的轮数")]
        public int MaxToolRoundCount { get; set; }
    }

    /// <summary>
    /// 将 List&lt;string&gt; 显示为逗号分隔字符串，方便在 PropertyGrid 中编辑。
    /// </summary>
    internal sealed class StringListConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
        {
            if (value is string s)
            {
                var result = new List<string>();
                foreach (var part in s.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        result.Add(trimmed);
                    }
                }
                return result;
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is List<string> list)
            {
                return string.Join(", ", list);
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
