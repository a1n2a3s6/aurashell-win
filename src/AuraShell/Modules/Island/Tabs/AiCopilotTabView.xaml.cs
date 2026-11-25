using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuraShell.Localization;
using AuraShell.Services;
using WinForms = System.Windows.Forms;

namespace AuraShell.Modules.Island.Tabs
{
    public partial class AiCopilotTabView : UserControl
    {
        private AiCopilotService? _aiService;
        private Action<bool>? _setIslandVisible;
        private Action<string>? _showToast;
        private bool _isBusy;

        public bool IsInputFocused => PromptInput.IsKeyboardFocused;

        public AiCopilotTabView()
        {
            InitializeComponent();
        }

        public void Initialize(AiCopilotService aiService, Action<bool> setIslandVisible, Action<string> showToast)
        {
            _aiService = aiService;
            _setIslandVisible = setIslandVisible;
            _showToast = showToast;
            UpdateProviderLabel();
        }

        public void UpdateProviderLabel()
        {
            if (_aiService == null) return;
            bool isGemini = string.Equals(_aiService.ActiveProvider, "Gemini", StringComparison.OrdinalIgnoreCase);
            ProviderSwitchBtn.Content = isGemini ? "✨ Gemini 2.5" : "⚡ Groq";
        }

        private async void AskBtn_Click(object sender, RoutedEventArgs e)
        {
            await SubmitPromptAsync();
        }

        private async void PromptInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SubmitPromptAsync();
            }
        }

        private async Task SubmitPromptAsync()
        {
            string prompt = PromptInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt) || _aiService == null) return;
            await RunAiAsync(() => _aiService.AskTextAsync(prompt));
        }

        private async void AskScreenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null || _isBusy) return;
            string question = PromptInput.Text.Trim();

            await RunAiAsync(async () =>
            {
                _setIslandVisible?.Invoke(false);
                await Task.Delay(180);
                byte[] png = CapturePrimaryScreenPng();
                _setIslandVisible?.Invoke(true);
                return await _aiService.AskAboutScreenAsync(png, question);
            });
        }

        private async void TranslateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null) return;
            string text = GetInputOrClipboard();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResponseText.Text = LocaleManager.Get("AiClipboardEmpty");
                return;
            }
            await RunAiAsync(() => _aiService.TranslateArEnAsync(text));
        }

        private async void SummarizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null) return;
            string text = GetClipboardTextSafe();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResponseText.Text = LocaleManager.Get("AiClipboardEmpty");
                return;
            }
            await RunAiAsync(() => _aiService.SummarizeAsync(text));
        }

        private async void FixGrammarBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null) return;
            string text = GetInputOrClipboard();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResponseText.Text = LocaleManager.Get("AiClipboardEmpty");
                return;
            }
            await RunAiAsync(() => _aiService.FixGrammarAsync(text));
        }

        private async void ExplainCodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null) return;
            string text = GetClipboardTextSafe();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResponseText.Text = LocaleManager.Get("AiClipboardEmpty");
                return;
            }
            await RunAiAsync(() => _aiService.ExplainCodeAsync(text));
        }

        private void CopyAnswerBtn_Click(object sender, RoutedEventArgs e)
        {
            string ans = ResponseText.Text;
            if (string.IsNullOrWhiteSpace(ans)) return;
            try
            {
                Clipboard.SetText(ans);
                _showToast?.Invoke(LocaleManager.Get("AiCopied", "Copied!"));
            }
            catch { }
        }

        private void ProviderSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null) return;
            string next = _aiService.ToggleProvider();
            UpdateProviderLabel();
            _showToast?.Invoke($"AI → {next}");
        }

        private async Task RunAiAsync(Func<Task<string>> action)
        {
            if (_isBusy) return;
            _isBusy = true;
            SetButtonsEnabled(false);
            ResponseText.Text = LocaleManager.Get("AiThinking", "Thinking...");
            try
            {
                string result = await action();
                ResponseText.Text = result;
            }
            catch (Exception ex)
            {
                ResponseText.Text = ex.Message;
            }
            finally
            {
                _isBusy = false;
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            AskBtn.IsEnabled = enabled;
            AskScreenBtn.IsEnabled = enabled;
            TranslateBtn.IsEnabled = enabled;
            SummarizeBtn.IsEnabled = enabled;
            FixGrammarBtn.IsEnabled = enabled;
            ExplainCodeBtn.IsEnabled = enabled;
        }

        private string GetInputOrClipboard()
        {
            string typed = PromptInput.Text.Trim();
            return !string.IsNullOrEmpty(typed) ? typed : GetClipboardTextSafe();
        }

        private static string GetClipboardTextSafe()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] CapturePrimaryScreenPng()
        {
            var bounds = WinForms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
