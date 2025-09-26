using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using System.Threading;
using System.Globalization;
using System.Collections.ObjectModel;
using HtmlAgilityPack;

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

    // --- ОСНОВНОЙ КЛАСС СТРАНИЦЫ ---

    public partial class MainPage : ContentPage
    {
        private const string KEY = "58e310878dcae97b7fd2ed9b73f6d716";
        private HttpClient _client;
        private int count = 0;
        private bool _isFlashing = false;
        private CancellationTokenSource _flashlightCts;
        private ObservableCollection<string> _imageSources;

        private IDispatcherTimer _slideshowTimer; // Таймер для слайд-шоу

        public MainPage()
        {
            InitializeComponent();
            _flashlightCts = new CancellationTokenSource();
            _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
            _imageSources = new ObservableCollection<string>();
            CityCarousel.ItemsSource = _imageSources;

            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

            _slideshowTimer = Dispatcher.CreateTimer();
            _slideshowTimer.Interval = TimeSpan.FromSeconds(5);
            _slideshowTimer.Tick += OnSlideshowTimerTick;
        }

        // --- МЕТОДЫ ДЛЯ РАБОТЫ С API (без изменений) ---

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
            try
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var query = $"{city} cityscape";
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&tbm=isch";
                var html = await httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                return doc.DocumentNode
                    .SelectNodes("//img[@src]")?
                    .Select(img => img.GetAttributeValue("src", ""))
                    .Where(src => src.StartsWith("https://encrypted-tbn0.gstatic.com/"))
                    .Take(3)
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scraping images: {ex.Message}");
                return new List<string>();
            }
        }


        // --- ОБРАБОТЧИКИ СОБЫТИЙ (без изменений, кроме фонарика) ---

        private async void UpdateButtonClicked(object sender, EventArgs e)
        {
            messageLabel.Text = string.Empty;
            _slideshowTimer.Stop();

            try
            {
                (double lat, double lon) = await GetLocationByName(cityEntry.Text);

                double currentTemperature = await GetTemperature(lat, lon);
                temperatureLabel.Text = currentTemperature.ToString("F1");

                if (currentTemperature >= 13) weatherIconLabel.Text = "☀️";
                else if (currentTemperature >= 0 && currentTemperature < 13) weatherIconLabel.Text = "☁️";
                else weatherIconLabel.Text = "❄️";

                var imageUrls = await GetCityImages(cityEntry.Text);
                _imageSources.Clear();
                if (imageUrls != null && imageUrls.Count > 0)
                {
                    foreach (var url in imageUrls)
                    {
                        _imageSources.Add(url);
                    }
                    CityCarousel.Position = 0;
                    _slideshowTimer.Start();
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

        private void OnSlideshowTimerTick(object sender, EventArgs e)
        {
            if (_imageSources == null || _imageSources.Count == 0)
                return;

            int nextPosition = (CityCarousel.Position + 1) % _imageSources.Count;
            CityCarousel.Position = nextPosition;
        }


        // --- ИЗМЕНЕННАЯ ЛОГИКА ФОНАРИКА ---

        private async void OnCounterClicked(object sender, EventArgs e)
        {
            // 1. Сначала останавливаем любой предыдущий цикл мигания
            if (!_flashlightCts.IsCancellationRequested)
            {
                _flashlightCts.Cancel();
                _flashlightCts.Dispose();
            }
            // Создаем новый токен отмены для нового цикла
            _flashlightCts = new CancellationTokenSource();
            _isFlashing = false;
            // Убедимся, что фонарик выключен перед стартом новой логики
            try { await Flashlight.TurnOffAsync(); } catch { }

            // 2. Обновляем счетчик
            count++;

            // 3. Проверяем, не достигли ли мы 10 нажатий
            if (count >= 10)
            {
                count = 0; // Сбрасываем счетчик
            }

            // 4. Обновляем текст на кнопке
            if (count == 0)
            {
                CounterBtn.Text = "Нажать";
            }
            else
            {
                // Показываем частоту в Герцах (1/интервал) для наглядности
                CounterBtn.Text = $"Частота: {count} Гц ({count} нажатий)";
            }
            SemanticScreenReader.Announce(CounterBtn.Text);

            // Если счетчик сброшен на 0, выходим - фонарик уже выключен
            if (count == 0)
            {
                return;
            }

            // 5. Запрашиваем разрешение, если еще не сделано
            var status = await Permissions.RequestAsync<Permissions.Flashlight>();
            if (status != PermissionStatus.Granted)
            {
                messageLabel.Text = "Нет разрешения на использование фонарика.";
                return;
            }

            // 6. Вычисляем интервал и запускаем новый цикл мигания
            // Общий интервал (вкл+выкл) = 1.0 / count секунд.
            // Значит, половина интервала (на включение или выключение) = (1.0 / count / 2.0) секунд.
            // Переводим в миллисекунды: (1000.0 / count / 2.0) или 500.0 / count
            int delay = (int)(500.0 / count);
            if (delay < 1) delay = 1; // Задержка не может быть меньше 1 мс

            _isFlashing = true;
            // Запускаем бесконечный цикл в фоновом потоке
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_flashlightCts.Token.IsCancellationRequested)
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOnAsync());
                        await Task.Delay(delay, _flashlightCts.Token);
                        await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOffAsync());
                        await Task.Delay(delay, _flashlightCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Это ожидаемое исключение, когда мы нажимаем кнопку снова и отменяем задачу
                }
                finally
                {
                    // Гарантированно выключаем фонарик при выходе из цикла
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try { await Flashlight.TurnOffAsync(); } catch { }
                        _isFlashing = false;
                    });
                }
            }, _flashlightCts.Token);
        }

        private async void OnResetClicked(object sender, EventArgs e)
        {
            // Останавливаем любой запущенный цикл
            if (!_flashlightCts.IsCancellationRequested)
            {
                _flashlightCts.Cancel();
                _flashlightCts.Dispose();
                _flashlightCts = new CancellationTokenSource();
            }

            count = 0;
            CounterBtn.Text = "Нажать";
            _isFlashing = false;

            // Гарантированно выключаем фонарик
            try
            {
                await Flashlight.TurnOffAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при выключении фонарика: {ex.Message}");
            }

            SemanticScreenReader.Announce(CounterBtn.Text);
        }
    }
}