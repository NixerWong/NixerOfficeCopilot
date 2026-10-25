using System;
using System.Collections.Generic;
using System.ComponentModel;
using OfficeCopilot.Localization;

namespace OfficeCopilot.Settings.WinForms
{
    internal sealed class LocalizedCategoryAttribute : CategoryAttribute
    {
        private readonly string _resourceKey;

        public LocalizedCategoryAttribute(string resourceKey)
            : base(resourceKey)
        {
            _resourceKey = resourceKey;
        }

        protected override string GetLocalizedString(string value)
        {
            return LocalizationService.Get(_resourceKey);
        }
    }

    internal sealed class LocalizedDisplayNameAttribute : DisplayNameAttribute
    {
        private readonly string _resourceKey;

        public LocalizedDisplayNameAttribute(string resourceKey)
        {
            _resourceKey = resourceKey;
        }

        public override string DisplayName
        {
            get { return LocalizationService.Get(_resourceKey); }
        }
    }

    internal sealed class LocalizedDescriptionAttribute : DescriptionAttribute
    {
        private readonly string _resourceKey;

        public LocalizedDescriptionAttribute(string resourceKey)
        {
            _resourceKey = resourceKey;
        }

        public override string Description
        {
            get { return LocalizationService.Get(_resourceKey); }
        }
    }

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

        [LocalizedCategory("Settings.Config.SkillEditor.Category.BasicInfo")]
        [LocalizedDisplayName("Settings.Config.Common.Enabled")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.Enabled")]
        public bool Enabled { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.BasicInfo")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.DisplayName")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.DisplayName")]
        public string DisplayName { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.BasicInfo")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.Category")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.Category")]
        public string Category { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.TriggerConditions")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.UseWhen")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.UseWhen")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> UseWhen { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.TriggerConditions")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.AvoidWhen")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.AvoidWhen")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> AvoidWhen { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.ToolPermissions")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.AllowedTools")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.AllowedTools")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> AllowedTools { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.ToolPermissions")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.PreferredTools")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.PreferredTools")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> PreferredTools { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.ToolPermissions")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.VerificationTools")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.VerificationTools")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> VerificationTools { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.RelatedSkills")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.CompanionSkills")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.CompanionSkills")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> CompanionSkills { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.RelatedSkills")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.FallbackSkills")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.FallbackSkills")]
        [TypeConverter(typeof(StringListConverter))]
        public List<string> FallbackSkills { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.SafetyPolicy")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.RequiresConfirmation")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.RequiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.SafetyPolicy")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.RequiresBackup")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.RequiresBackup")]
        public bool RequiresBackup { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.SafetyPolicy")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.RequireVerification")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.RequireVerification")]
        public bool RequireVerification { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.SafetyPolicy")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.AllowHighRiskTools")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.AllowHighRiskTools")]
        public bool AllowHighRiskTools { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.SafetyPolicy")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.AllowCodeExecution")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.AllowCodeExecution")]
        public bool AllowCodeExecution { get; set; }

        [LocalizedCategory("Settings.Config.SkillEditor.Category.Advanced")]
        [LocalizedDisplayName("Settings.Config.SkillEditor.Property.MaxToolRoundCount")]
        [LocalizedDescription("Settings.Config.SkillEditor.Description.MaxToolRoundCount")]
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
