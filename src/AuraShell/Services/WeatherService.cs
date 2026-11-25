using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraShell.Localization;

namespace AuraShell.Services
{
    public sealed class WeatherSnapshot
    {
        public string City { get; init; } = string.Empty;
        public double TemperatureC { get; init; }
        public double HighC { get; init; }
        public double LowC { get; init; }
        public int WeatherCode { get; init; }
        public bool IsDay { get; init; } = true;
        public string Icon { get; init; } = "☀️";
        public string ConditionKey { get; init; } = "WeatherClear";
        public string LocalizedCondition => LocaleManager.Get(ConditionKey, "Clear");
        public string DisplayShort => $"{Icon} {Math.Round(TemperatureC)}°C";
    }

    /// <summary>
    /// Free Open-Meteo weather & geocoding client (zero API key, zero NuGet dependencies).
    /// Fetches current temperature, WMO weather code, is_day, and daily High/Low temperatures.
    /// </summary>
    public sealed class WeatherService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public WeatherSnapshot? Latest { get; private set; }
        public event EventHandler<WeatherSnapshot>? WeatherUpdated;

        public async Task<(string City, double Lat, double Lon)?> GeocodeCityAsync(string cityName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cityName)) return null;
            try
            {
                string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName.Trim())}&count=1&language=en&format=json";
                using var resp = await Http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                string json = await resp.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var first = results[0];
                    string name = first.TryGetProperty("name", out var n) ? n.GetString() ?? cityName : cityName;
                    double lat = first.GetProperty("latitude").GetDouble();
                    double lon = first.GetProperty("longitude").GetDouble();
                    return (name, lat, lon);
                }
            }
            catch
            {
                // Ignore network failure
            }
            return null;
        }

        public async Task<WeatherSnapshot?> FetchCurrentAsync(string city, double latitude, double longitude, CancellationToken ct = default)
        {
            try
            {
                string latStr = latitude.ToString(CultureInfo.InvariantCulture);
                string lonStr = longitude.ToString(CultureInfo.InvariantCulture);
                string url = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto";

                using var resp = await Http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                string json = await resp.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(json);
                var current = doc.RootElement.GetProperty("current");
                double temp = current.GetProperty("temperature_2m").GetDouble();
                int code = current.GetProperty("weather_code").GetInt32();
                bool isDay = !current.TryGetProperty("is_day", out var d) || d.GetInt32() == 1;

                double high = temp;
                double low = temp;
                if (doc.RootElement.TryGetProperty("daily", out var daily))
                {
                    if (daily.TryGetProperty("temperature_2m_max", out var maxArr) && maxArr.GetArrayLength() > 0)
                        high = maxArr[0].GetDouble();
                    if (daily.TryGetProperty("temperature_2m_min", out var minArr) && minArr.GetArrayLength() > 0)
                        low = minArr[0].GetDouble();
                }

                var (icon, key) = MapWmoCode(code, isDay);
                Latest = new WeatherSnapshot
                {
                    City = city,
                    TemperatureC = temp,
                    HighC = high,
                    LowC = low,
                    WeatherCode = code,
                    IsDay = isDay,
                    Icon = icon,
                    ConditionKey = key
                };
                WeatherUpdated?.Invoke(this, Latest);
                return Latest;
            }
            catch
            {
                return Latest;
            }
        }

        private static (string Icon, string ResourceKey) MapWmoCode(int code, bool isDay)
        {
            return code switch
            {
                0 => (isDay ? "☀️" : "🌙", "WeatherClear"),
                1 or 2 or 3 => (isDay ? "⛅" : "☁️", "WeatherPartlyCloudy"),
                45 or 48 => ("🌫️", "WeatherFog"),
                51 or 53 or 55 or 56 or 57 => ("🌦️", "WeatherDrizzle"),
                61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("🌧️", "WeatherRain"),
                71 or 73 or 75 or 77 or 85 or 86 => ("❄️", "WeatherSnow"),
                95 or 96 or 99 => ("⛈️", "WeatherStorm"),
                _ => (isDay ? "☀️" : "🌙", "WeatherClear")
            };
        }
    }
}
