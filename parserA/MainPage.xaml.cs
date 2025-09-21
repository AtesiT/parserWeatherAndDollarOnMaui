using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using System.Threading;
using System.Globalization;
using System.Collections.ObjectModel;

namespace parserA
{
    // --- МОДЕЛИ ДЛЯ ДЕСЕРИАЛИЗАЦИИ ОТВЕТА ОТ ЦБ РФ ---
    public class CbrCurrency
    {
        [JsonPropertyName("Value")]
        public double Value { get; set; }

        [JsonPropertyName("Previous")]
        public double Previous { get; set; }
    }

    public class CbrRates
    {
        [JsonPropertyName("Valute")]
        public Dictionary<string, CbrCurrency> Valute { get; set; }
    }

    // --- МОДЕЛИ ДЛЯ ДЕСЕРИАЛИЗАЦИИ ОТВЕТА ОТ UNSPLASH API ---
    public class UnsplashSearchResult
    {
        [JsonPropertyName("results")]
        public List<UnsplashPhoto> Results { get; set; }
    }

    public class UnsplashPhoto
    {
        [JsonPropertyName("urls")]
        public UnsplashPhotoUrls Urls { get; set; }
    }

    public class UnsplashPhotoUrls
    {
        [JsonPropertyName("regular")]
        public string Regular { get; set; }
    }


    // --- ОСНОВНОЙ КЛАСС СТРАНИЦЫ ---

    public partial class MainPage : ContentPage
    {
        private const string KEY = "58e310878dcae97b7fd2ed9b73f6d716";
        private const string UNSPLASH_KEY = "qFhqPBt0AzKHb8Ct_xibWdQLm9Cv4gjcWZJ8Xfk3ZC8";
        private HttpClient _client;
        private int count = 0;
        private bool _isFlashing = false; // Флаг для управления состоянием фонарика
        private CancellationTokenSource _flashlightCts;
        private ObservableCollection<string> _imageSources;

        public MainPage()
        {
            InitializeComponent();
            _flashlightCts = new CancellationTokenSource();
            _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
            _imageSources = new ObservableCollection<string>();
            CityCarousel.ItemsSource = _imageSources;

            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
        }

        // --- МЕТОДЫ ДЛЯ РАБОТЫ С API ---

        private async Task<(double today, double yesterday)> GetCbrDollarRates()
        {
            var rates = await _client.GetFromJsonAsync<CbrRates>(
                "https://www.cbr-xml-daily.ru/daily_json.js"
            ) ?? throw new Exception("Не удалось загрузить курсы валют от ЦБ РФ.");

            if (rates.Valute.TryGetValue("USD", out var usd))
            {
                return (usd.Value, usd.Previous);
            }

            throw new Exception("Не удалось найти курс доллара в ответе ЦБ РФ.");
        }


        private async Task<(double lat, double lon)> GetLocationByName(string name)
        {
            var data = await _client.GetFromJsonAsync<JsonDocument>(
                $"http://api.openweathermap.org/geo/1.0/direct?q={name}&lang=ru&limit=1&appid={KEY}"
            ) ?? throw new Exception("Не удалось загрузить местоположение.");
            if (data.RootElement.GetArrayLength() > 0
                && data.RootElement[0].TryGetProperty("lat", out JsonElement latElement)
                && data.RootElement[0].TryGetProperty("lon", out JsonElement lonElement)
                && latElement.TryGetDouble(out double lat)
                && lonElement.TryGetDouble(out double lon))
                return (lat, lon);
            throw new Exception("Не удалось получить местоположение.");
        }

        private async Task<double> GetTemperature(double lat, double lon)
        {
            var data = await _client.GetFromJsonAsync<JsonDocument>(
                $"https://api.openweathermap.org/data/2.5/weather?units=metric&lat={lat}&lon={lon}&appid={KEY}"
            ) ?? throw new Exception("Не удалось загрузить данные о температуре.");
            if (data.RootElement.TryGetProperty("main", out JsonElement main)
                && main.TryGetProperty("temp", out JsonElement temp)
                && temp.TryGetDouble(out double value))
                return value;
            throw new Exception("Не удалось получить температуру.");
        }

        private async Task<List<string>> GetCityImages(string city)
        {
            if (string.IsNullOrWhiteSpace(UNSPLASH_KEY) || UNSPLASH_KEY == "YOUR_UNSPLASH_ACCESS_KEY")
            {
                throw new Exception("Необходимо указать ключ Unsplash API.");
            }
            var url = $"https://api.unsplash.com/search/photos?query={city}+cityscape&client_id={UNSPLASH_KEY}&per_page=3";
            var response = await _client.GetFromJsonAsync<UnsplashSearchResult>(url);

            if (response != null && response.Results != null && response.Results.Count > 0)
            {
                return response.Results.Select(p => p.Urls.Regular).ToList();
            }

            return new List<string>();
        }


        // --- ОБРАБОТЧИКИ СОБЫТИЙ ---

        private async void UpdateButtonClicked(object sender, EventArgs e)
        {
            messageLabel.Text = string.Empty;

            try
            {
                (double lat, double lon) = await GetLocationByName(cityEntry.Text);

                double currentTemperature = await GetTemperature(lat, lon);
                temperatureLabel.Text = currentTemperature.ToString("F1");
                weatherIconLabel.Text = currentTemperature > 0 ? "🔥" : currentTemperature < 0 ? "❄️" : "";

                var imageUrls = await GetCityImages(cityEntry.Text);
                _imageSources.Clear();
                if (imageUrls.Count > 0)
                {
                    foreach (var url in imageUrls)
                    {
                        _imageSources.Add(url);
                    }
                }
                else
                {
                    messageLabel.Text += "Не удалось найти картинки для этого города.";
                }
            }
            catch (Exception ex)
            {
                temperatureLabel.Text = "не понятно";
                weatherIconLabel.Text = "";
                messageLabel.Text += ex.Message;
                _imageSources.Clear();
            }

            try
            {
                var (todayRate, yesterdayRate) = await GetCbrDollarRates();
                TodayRateLabel.Text = $"Сегодня: {todayRate:F2} ₽";
                YesterdayRateLabel.Text = $"Вчера: {yesterdayRate:F2} ₽";

                if (todayRate > yesterdayRate)
                {
                    RateChangeIndicatorLabel.Text = "▲";
                    RateChangeIndicatorLabel.TextColor = Colors.Green;
                }
                else if (todayRate < yesterdayRate)
                {
                    RateChangeIndicatorLabel.Text = "▼";
                    RateChangeIndicatorLabel.TextColor = Colors.Red;
                }
                else
                {
                    RateChangeIndicatorLabel.Text = "▬";
                    RateChangeIndicatorLabel.TextColor = Colors.Gray;
                }
            }
            catch (Exception ex)
            {
                messageLabel.Text += ex.Message;
            }
        }


        // --- ОБНОВЛЁННЫЙ КОД ДЛЯ ФОНАРИКА ---
        private async void OnCounterClicked(object sender, EventArgs e)
        {
            count++;
            CounterBtn.Text = $"Нажато {count} раз";
            SemanticScreenReader.Announce(CounterBtn.Text);

            var status = await Permissions.RequestAsync<Permissions.Flashlight>();
            if (status != PermissionStatus.Granted)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    messageLabel.Text = "Нет разрешения на использование фонарика.";
                });
                return;
            }

            // Запускаем задачу только если она еще не запущена
            if (!_isFlashing)
            {
                _isFlashing = true;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Цикл будет работать, пока i меньше текущего значения count
                        for (int i = 0; i < count && !_flashlightCts.Token.IsCancellationRequested; i++)
                        {
                            await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOnAsync());
                            await Task.Delay(200, _flashlightCts.Token);
                            await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOffAsync());
                            await Task.Delay(200, _flashlightCts.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            try
                            {
                                await Flashlight.TurnOffAsync();
                            }
                            catch { }
                            _isFlashing = false;
                        });
                    }
                }, _flashlightCts.Token);
            }
        }

        private async void OnResetClicked(object sender, EventArgs e)
        {
            count = 0;
            CounterBtn.Text = "Нажать";

            // Отменяем текущую задачу, если она есть
            if (!_flashlightCts.IsCancellationRequested)
            {
                _flashlightCts.Cancel();
                _flashlightCts.Dispose();
                _flashlightCts = new CancellationTokenSource();
            }

            // Выключаем фонарик, если он был включен
            await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOffAsync());
            _isFlashing = false;

            SemanticScreenReader.Announce(CounterBtn.Text);
        }
    }
}