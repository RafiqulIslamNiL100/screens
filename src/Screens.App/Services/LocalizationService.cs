using System;
using System.Collections.Generic;

namespace Screens.App.Services;

public enum AppLanguage { English, Chinese }

/// <summary>
/// Minimal in-app localization: a flat key/value table per language, no
/// .resx/satellite-assembly pipeline (keeps the self-contained single-file
/// publish simple). ViewModels expose computed string properties that call
/// <see cref="T"/> and subscribe to <see cref="LanguageChanged"/> to refresh
/// themselves — same pattern the codebase already uses for AuthViewModel's
/// sign-up/sign-in text toggle, just driven by language instead of mode.
/// </summary>
public sealed class LocalizationService
{
    private AppLanguage _language = AppLanguage.English;

    public event Action? LanguageChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;
            _language = value;
            LanguageChanged?.Invoke();
        }
    }

    public string T(string key)
    {
        var table = _language == AppLanguage.Chinese ? Zh : En;
        return table.TryGetValue(key, out var value) ? value : (En.TryGetValue(key, out var fallback) ? fallback : key);
    }

    private static readonly Dictionary<string, string> En = new()
    {
        ["App.Title"] = "Screens",
        ["Auth.SignInHeading"] = "Sign in",
        ["Auth.SignUpHeading"] = "Create your account",
        ["Auth.Email"] = "Email",
        ["Auth.Password"] = "Password",
        ["Auth.SignIn"] = "Sign in",
        ["Auth.SignUp"] = "Sign up",
        ["Auth.ToggleToSignIn"] = "Already have an account? Sign in",
        ["Auth.ToggleToSignUp"] = "New here? Create an account",
        ["Activation.Title"] = "Activate Screens",
        ["Activation.Subtitle"] = "Enter the activation key you received after purchase.",
        ["Activation.Activate"] = "Activate",
        ["Main.Fields"] = "Fields",
        ["Main.Reset"] = "Reset",
        ["Main.ClearAll"] = "Clear all",
        ["Main.Export"] = "Export (Ctrl+E)",
        ["Main.ZoomToggle"] = "Zoom to fit / 100%",
        ["Main.Settings"] = "Settings",
        ["Main.Duplicate"] = "Duplicate",
        ["Settings.Title"] = "Settings & About",
        ["Settings.Theme"] = "Theme",
        ["Settings.Language"] = "Language",
        ["Settings.CheckForUpdates"] = "Check for updates",
        ["Settings.SignOut"] = "Sign out",
        ["Settings.Renew"] = "Renew license",
        ["Settings.ExportPresets"] = "Export presets",
        ["Settings.Watermark"] = "Add watermark to exports",
        ["Settings.WatermarkTextPlaceholder"] = "Made with Screens",
        ["Settings.PublishPremiumTemplate"] = "Publish as Premium Template",
        ["Toast.OpenFolder"] = "Open folder",
        ["CommandPalette.Placeholder"] = "Type a command…",
        ["License.DaysRemaining"] = "{0} days remaining",
        ["License.ExpiresToday"] = "Expires today",
        ["License.Lifetime"] = "Lifetime license",
        ["PremiumAccess.DaysRemaining"] = "{0} days remaining",
        ["PremiumAccess.ExpiresToday"] = "Expires today",
        ["PremiumAccess.Lifetime"] = "Lifetime access",
    };

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["App.Title"] = "Screens",
        ["Auth.SignInHeading"] = "登录",
        ["Auth.SignUpHeading"] = "创建账户",
        ["Auth.Email"] = "邮箱",
        ["Auth.Password"] = "密码",
        ["Auth.SignIn"] = "登录",
        ["Auth.SignUp"] = "注册",
        ["Auth.ToggleToSignIn"] = "已有账户？点击登录",
        ["Auth.ToggleToSignUp"] = "还没有账户？点击注册",
        ["Activation.Title"] = "激活 Screens",
        ["Activation.Subtitle"] = "请输入购买后收到的激活密钥。",
        ["Activation.Activate"] = "激活",
        ["Main.Fields"] = "字段",
        ["Main.Reset"] = "重置",
        ["Main.ClearAll"] = "清空全部",
        ["Main.Export"] = "导出 (Ctrl+E)",
        ["Main.ZoomToggle"] = "适应窗口 / 100%",
        ["Main.Settings"] = "设置",
        ["Main.Duplicate"] = "复制",
        ["Settings.Title"] = "设置与关于",
        ["Settings.Theme"] = "主题",
        ["Settings.Language"] = "语言",
        ["Settings.CheckForUpdates"] = "检查更新",
        ["Settings.SignOut"] = "退出登录",
        ["Settings.Renew"] = "续订许可证",
        ["Settings.ExportPresets"] = "导出预设",
        ["Settings.Watermark"] = "导出时添加水印",
        ["Settings.WatermarkTextPlaceholder"] = "Made with Screens",
        ["Settings.PublishPremiumTemplate"] = "发布为高级模板",
        ["Toast.OpenFolder"] = "打开文件夹",
        ["CommandPalette.Placeholder"] = "输入命令…",
        ["License.DaysRemaining"] = "剩余 {0} 天",
        ["License.ExpiresToday"] = "今天到期",
        ["License.Lifetime"] = "永久许可证",
        ["PremiumAccess.DaysRemaining"] = "剩余 {0} 天",
        ["PremiumAccess.ExpiresToday"] = "今天到期",
        ["PremiumAccess.Lifetime"] = "永久访问权限",
    };
}
