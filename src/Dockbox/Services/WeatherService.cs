using Dockbox.Core;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Dockbox.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

public sealed record WeatherInfo(string City, double Temperature, WeatherKind Kind, bool IsDay, string Description);

/// <summary>Clima atual via Open-Meteo (gratuito, sem chave).</summary>
public static class WeatherService
{
    // Respostas do clima têm poucos KB: limita a 1 MB e 10 s para uma resposta anormal não pesar.
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1024 * 1024,
    };

    static WeatherService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Dockbox/1.0");
    }

    public static async Task<WeatherInfo?> GetAsync(string city)
    {
        try
        {
            var loc = string.IsNullOrWhiteSpace(city) ? await LocateByIpAsync() : await GeocodeAsync(city.Trim());
            if (loc == null) return null;
            var (lat, lon, name) = loc.Value;

            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,is_day&timezone=auto",
                lat, lon);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            double temp = cur.GetProperty("temperature_2m").GetDouble();
            int code = cur.GetProperty("weather_code").GetInt32();
            bool day = cur.GetProperty("is_day").GetInt32() == 1;
            var (kind, desc) = Describe(code);
            return new WeatherInfo(name, temp, kind, day, desc == "" ? "—" : Loc.T(desc));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(double, double, string)?> GeocodeAsync(string city)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=" + Loc.Culture.TwoLetterISOLanguageName + "&name=" + Uri.EscapeDataString(city);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? city);
    }

    private static async Task<(double, double, string)?> LocateByIpAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/"));
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var ok) && !ok.GetBoolean()) return null;
        return (root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
    }

    private static (WeatherKind, string) Describe(int code) => code switch
    {
        0 => (WeatherKind.Clear, "Wx_Clear"),
        1 => (WeatherKind.Clear, "Wx_MainlyClear"),
        2 => (WeatherKind.PartlyCloudy, "Wx_PartlyCloudy"),
        3 => (WeatherKind.Cloudy, "Wx_Cloudy"),
        45 or 48 => (WeatherKind.Fog, "Wx_Fog"),
        >= 51 and <= 57 => (WeatherKind.Drizzle, "Wx_Drizzle"),
        >= 61 and <= 67 => (WeatherKind.Rain, "Wx_Rain"),
        >= 71 and <= 77 => (WeatherKind.Snow, "Wx_Snow"),
        >= 80 and <= 82 => (WeatherKind.Rain, "Wx_RainShowers"),
        85 or 86 => (WeatherKind.Snow, "Wx_SnowShowers"),
        >= 95 => (WeatherKind.Thunder, "Wx_Thunder"),
        _ => (WeatherKind.Cloudy, ""),
    };
}
