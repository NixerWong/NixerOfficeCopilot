using DevExpress.LookAndFeel;
using DevExpress.UserSkins;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// Office 加载项的宿主是 Excel / Word / PowerPoint 进程，DevExpress 不会读取宿主的 app.config，
    /// 因此 DefaultAppSkin 不生效。由各加载项的 ThisAddIn_Startup 在创建任何 DevExpress 控件之前调用。
    /// </summary>
    public static class DevExpressSkinBootstrap
    {
        // Whiteprint 属于 Bonus Skin，必须先 Register 才能按名称找到。
        private const string SkinName = "Whiteprint";

        public static void Apply()
        {
            try
            {
                BonusSkins.Register();
                UserLookAndFeel.Default.SetSkinStyle(SkinName);
            }
            catch (System.Exception)
            {
                // 皮肤只是外观，失败时保持 DevExpress 默认皮肤，不阻断加载项启动。
            }
        }
    }
}
