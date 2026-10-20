using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// 本程序集被 Excel / Word / PowerPoint 三个加载项共用，窗体图标必须由当前宿主在启动时注入。
    /// </summary>
    public static class HostFormIcon
    {
        private const string IconResourceSuffix = ".icon.ico";

        private static Icon _current;

        public static Icon Current
        {
            get { return _current; }
        }

        /// <summary>由各加载项的 ThisAddIn_Startup 调用，从该加载项程序集中读取嵌入的 icon.ico。</summary>
        public static void Initialize(Assembly hostAssembly)
        {
            if (hostAssembly == null)
            {
                return;
            }

            try
            {
                var resourceName = FindIconResourceName(hostAssembly);
                if (resourceName == null)
                {
                    return;
                }

                using (var stream = hostAssembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        _current = new Icon(stream);
                    }
                }
            }
            catch (Exception)
            {
                _current = null;
            }
        }

        public static void Initialize(Icon icon)
        {
            _current = icon;
        }

        public static void Apply(Form form)
        {
            var icon = _current;
            if (form == null || icon == null)
            {
                return;
            }

            try
            {
                form.Icon = icon;
                form.ShowIcon = true;
            }
            catch (Exception)
            {
                // 图标只是外观，失败时保持默认图标即可。
            }
        }

        private static string FindIconResourceName(Assembly assembly)
        {
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name != null && name.EndsWith(IconResourceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
