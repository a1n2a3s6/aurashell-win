namespace AuraShell.Modules.Widgets
{
    public enum AppleWidgetSize
    {
        Small,  // 172 x 172
        Medium, // 360 x 172
        Large   // 360 x 360
    }

    /// <summary>
    /// Contract implemented by all 6 pixel-accurate Apple desktop widget cards.
    /// </summary>
    public interface IAppleWidgetCard
    {
        string WidgetId { get; }
        bool UsesCustomSkyBackdrop { get; }
        void ApplySize(AppleWidgetSize size);
        void ApplyTheme(bool isDark);
    }
}
