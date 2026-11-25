using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraShell.Localization;

namespace AuraShell.Services
{
    /// <summary>
    /// Native HTTP AI Copilot supporting Gemini 2.5 Flash (text + multimodal screen Q&A)
    /// and Groq (ultra-fast chat & vision), with zero external NuGet packages.
    /// </summary>
    public sealed class AiCopilotService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly ConfigStore _configStore;

        public AiCopilotService(ConfigStore configStore)
        {
            _configStore = configStore;
        }

        public string ActiveProvider => _configStore.Current.AiProvider;

        public string ToggleProvider()
        {
            string next = string.Equals(_configStore.Current.AiProvider, "Gemini", StringComparison.OrdinalIgnoreCase)
                ? "Groq"
                : "Gemini";
            _configStore.Update(c => c.AiProvider = next);
            return next;
        }

        public Task<string> AskTextAsync(string prompt, CancellationToken ct = default)
        {
            string sys = "You are Aura, a concise, sharp Windows desktop copilot inside AuraIsland. Reply clearly in the same language as the user (Arabic or English). Keep answers compact and easy to scan.";
            return SendAsync(sys, prompt, null, ct);
        }

        public Task<string> AskAboutScreenAsync(byte[] pngBytes, string userQuestion, CancellationToken ct = default)
        {
            string prompt = string.IsNullOrWhiteSpace(userQuestion)
                ? (LocaleManager.IsArabic
                    ? "اشرح بإيجاز ما يظهر في هذه الشاشة وما أهم إجراء أو معلومة فيها."
                    : "Briefly explain what is on this screen and highlight the most important info or next action.")
                : userQuestion;

            string sys = "You are Aura, a visual Windows desktop copilot. Analyze the screenshot accurately and answer concisely in the user's language.";
            return SendAsync(sys, prompt, pngBytes, ct);
        }

        public Task<string> TranslateArEnAsync(string text, CancellationToken ct = default)
        {
            string sys = "You are an expert Arabic-English translator. If the input is primarily Arabic, translate it into natural English. If the input is English or another language, translate it into natural, fluent Arabic. Output ONLY the translation.";
            return SendAsync(sys, text, null, ct);
        }

        public Task<string> SummarizeAsync(string text, CancellationToken ct = default)
        {
            string sys = "Summarize the following text into 3-5 crisp bullet points in the same language as the input. No filler.";
            return SendAsync(sys, text, null, ct);
        }

        public Task<string> FixGrammarAsync(string text, CancellationToken ct = default)
        {
            string sys = "Fix spelling, grammar, and clarity of the following text while preserving its original language and tone. Output ONLY the corrected text.";
            return SendAsync(sys, text, null, ct);
        }

        public Task<string> ExplainCodeAsync(string code, CancellationToken ct = default)
        {
            string sys = LocaleManager.IsArabic
                ? "اشرح هذا الكود البرمجي بإيجاز ووضوح بالعربية: ماذا يفعل، أهم الأجزاء، وأي ملاحظات أو أخطاء محتملة."
                : "Explain this code concisely: what it does, key logic, and any potential bugs or improvements.";
            return SendAsync(sys, code, null, ct);
        }

        private async Task<string> SendAsync(string systemInstruction, string userPrompt, byte[]? pngImage, CancellationToken ct)
        {
            var cfg = _configStore.Current;
            bool useGemini = string.Equals(cfg.AiProvider, "Gemini", StringComparison.OrdinalIgnoreCase);

            // Auto-fallback if active provider has no key but the other does
            if (useGemini && string.IsNullOrWhiteSpace(cfg.GeminiApiKey) && !string.IsNullOrWhiteSpace(cfg.GroqApiKey))
                useGemini = false;
            else if (!useGemini && string.IsNullOrWhiteSpace(cfg.GroqApiKey) && !string.IsNullOrWhiteSpace(cfg.GeminiApiKey))
                useGemini = true;

            if (useGemini)
            {
                if (string.IsNullOrWhiteSpace(cfg.GeminiApiKey))
                    return LocaleManager.Get("AiNoKey", "Set your Gemini or Groq API key in AuraShell Studio.");
                return await CallGeminiAsync(cfg.GeminiApiKey.Trim(), cfg.GeminiModel, systemInstruction, userPrompt, pngImage, ct);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(cfg.GroqApiKey))
                    return LocaleManager.Get("AiNoKey", "Set your Gemini or Groq API key in AuraShell Studio.");
                return await CallGroqAsync(cfg.GroqApiKey.Trim(), cfg.GroqModel, systemInstruction, userPrompt, pngImage, ct);
            }
        }

        private static async Task<string> CallGeminiAsync(
            string apiKey,
            string model,
            string systemInstruction,
            string prompt,
            byte[]? pngImage,
            CancellationToken ct)
        {
            string effectiveModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(effectiveModel)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            object[] parts = pngImage != null
                ? new object[]
                {
                    new { text = prompt },
                    new
                    {
                        inline_data = new
                        {
                            mime_type = "image/png",
                            data = Convert.ToBase64String(pngImage)
                        }
                    }
                }
                : new object[] { new { text = prompt } };

            var payload = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                contents = new[]
                {
                    new { role = "user", parts }
                },
                generationConfig = new
                {
                    temperature = 0.4,
                    maxOutputTokens = 700
                }
            };

            string body = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            try
            {
                using var resp = await Http.SendAsync(req, ct);
                string respText = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                    return $"Gemini API Error ({(int)resp.StatusCode}): {ExtractErrorMessage(respText)}";

                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
                {
                    var content = cands[0].GetProperty("content");
                    if (content.TryGetProperty("parts", out var respParts) && respParts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var p in respParts.EnumerateArray())
                        {
                            if (p.TryGetProperty("text", out var t))
                                sb.Append(t.GetString());
                        }
                        string answer = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(answer))
                            return answer;
                    }
                }
                return "No response text returned by Gemini.";
            }
            catch (Exception ex)
            {
                return $"Gemini request failed: {ex.Message}";
            }
        }

        private static async Task<string> CallGroqAsync(
            string apiKey,
            string model,
            string systemInstruction,
            string prompt,
            byte[]? pngImage,
            CancellationToken ct)
        {
            const string url = "https://api.groq.com/openai/v1/chat/completions";
            string effectiveModel = pngImage != null
                ? "meta-llama/llama-4-scout-17b-16e-instruct"
                : (string.IsNullOrWhiteSpace(model) ? "llama-3.3-70b-versatile" : model.Trim());

            object userContent = pngImage != null
                ? new object[]
                {
                    new { type = "text", text = prompt },
                    new
                    {
                        type = "image_url",
                        image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(pngImage) }
                    }
                }
                : prompt;

            var payload = new
            {
                model = effectiveModel,
                temperature = 0.4,
                max_tokens = 700,
                messages = new object[]
                {
                    new { role = "system", content = systemInstruction },
                    new { role = "user", content = userContent }
                }
            };

            string body = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                using var resp = await Http.SendAsync(req, ct);
                string respText = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                    return $"Groq API Error ({(int)resp.StatusCode}): {ExtractErrorMessage(respText)}";

                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    string? answer = choices[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrWhiteSpace(answer))
                        return answer.Trim();
                }
                return "No response text returned by Groq.";
            }
            catch (Exception ex)
            {
                return $"Groq request failed: {ex.Message}";
            }
        }

        private static string ExtractErrorMessage(string rawJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? rawJson;
            }
            catch { }
            return rawJson.Length > 180 ? rawJson.Substring(0, 180) + "..." : rawJson;
        }
    }
}
