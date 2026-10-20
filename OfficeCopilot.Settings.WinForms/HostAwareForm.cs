using System;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// 所有设置窗体的基类，负责套用当前宿主（Excel / Word / PowerPoint）的图标。
    /// </summary>
    public class HostAwareForm : Form
    {
        protected override void OnLoad(EventArgs e)
        {
            HostFormIcon.Apply(this);
            base.OnLoad(e);
        }
    }
}
