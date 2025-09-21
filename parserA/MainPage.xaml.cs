using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using System.Threading;
using System.Globalization;
using System.Collections.ObjectModel; // Добавлено для ObservableCollection

namespace parserA
{
    // --- МОДЕЛИ ДЛЯ ДЕСЕРИАЛИЗАЦИИ ОТВЕТА ОТ ЦБ РФ ---

    // Модель для одной валюты
    public class CbrCurrency
    {
        [JsonPropertyName("Value")]
        public double Value { get; set; }

        [JsonPropertyName("Previous")]
        public double Previous { get; set; }
    }

    // Корневая модель ответа API
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
        private bool _flashlightActive = false;
        private CancellationTokenSource _flashlightCts;
        private ObservableCollection<string> _imageSources; // Коллекция для слайд-шоу

        public MainPage()
        {
            InitializeComponent();
            _flashlightCts = new CancellationTokenSource();
            _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) }; // Установка таймаута
            _imageSources = new ObservableCollection<string>();
            CityCarousel.ItemsSource = _imageSources; // Привязка коллекции к CarouselView

            // Установим культуру, чтобы точка была разделителем для double
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
        }

        // --- МЕТОДЫ ДЛЯ РАБОТЫ С API ---

        // Старый метод, больше не используется. Можно удалить.
        private async Task<double> GetDollarRate()
        {
            var data = await _client.GetFromJsonAsync<JsonDocument>(
                "https://www.floatrates.com/daily/usd.json"
            ) ?? throw new Exception("Не удалось загрузить данные по доллару.");
            if (data.RootElement.TryGetProperty("rub", out JsonElement currency)
                && currency.TryGetProperty("rate", out JsonElement rate)
                && rate.TryGetDouble(out double value))
                return value;
            throw new Exception("Не удалось получить стоимость доллара.");
        }

        // НОВЫЙ МЕТОД для получения курсов с сайта ЦБ РФ
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

        // --- НОВЫЙ МЕТОД ДЛЯ ПОИСКА КАРТИНОК НА UNSPLASH ---
        private async Task<List<string>> GetCityImages(string city)
        {
            if (string.IsNullOrWhiteSpace(UNSPLASH_KEY) || UNSPLASH_KEY == "YOUR_UNSPLASH_ACCESS_KEY")
            {
                throw new Exception("Необходимо указать ключ Unsplash API.");
            }
            //var url = $"https://api.unsplash.com/search/photos?query={city}&client_id={UNSPLASH_KEY}&per_page=3";
            var url = $"https://api.unsplash.com/search/photos?query={city}+cityscape&client_id={UNSPLASH_KEY}&per_page=3";
            var response = await _client.GetFromJsonAsync<UnsplashSearchResult>(url);

            if (response != null && response.Results != null && response.Results.Count > 0)
            {
                return response.Results.Select(p => p.Urls.Regular).ToList();
            }

            return new List<string>(); // Возвращаем пустой список, если картинок нет
        }


        // --- ОБРАБОТЧИКИ СОБЫТИЙ ---

        private async void UpdateButtonClicked(object sender, EventArgs e)
        {
            messageLabel.Text = string.Empty;

            // --- Блок для погоды ---
            try
            {
                (double lat, double lon) = await GetLocationByName(cityEntry.Text);

                // 1. Получаем температуру и сохраняем в переменную
                double currentTemperature = await GetTemperature(lat, lon);

                // 2. Обновляем текст с температурой
                temperatureLabel.Text = currentTemperature.ToString("F1"); // Форматируем до 1 знака после запятой

                // 3. Добавляем логику для иконки
                if (currentTemperature > 0)
                {
                    weatherIconLabel.Text = "🔥"; // Эмодзи жары
                }
                else if (currentTemperature < 0)
                {
                    weatherIconLabel.Text = "❄️"; // Эмодзи холода
                }
                else
                {
                    weatherIconLabel.Text = ""; // Если ровно 0, ничего не показываем
                }

                // --- НОВЫЙ БЛОК ДЛЯ СЛАЙД-ШОУ ---
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
                // При ошибке сбрасываем значения
                temperatureLabel.Text = "не понятно";
                weatherIconLabel.Text = "";
                messageLabel.Text += ex.Message;
                _imageSources.Clear(); // Очищаем слайд-шоу при ошибке
            }

            // --- Блок для курса валют (остается без изменений) ---
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


        // --- Код для фонарика (остается без изменений) ---
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

            if (_flashlightActive)
            {
                _flashlightCts.Cancel();
                await Task.Delay(100);
                _flashlightCts.Dispose();
                _flashlightCts = new CancellationTokenSource();
            }

            _flashlightActive = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < count && _flashlightActive && !_flashlightCts.Token.IsCancellationRequested; i++)
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOnAsync());
                        await Task.Delay(200, _flashlightCts.Token);
                        await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOffAsync());
                        await Task.Delay(200, _flashlightCts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        messageLabel.Text = $"Ошибка при управлении фонариком: {ex.Message}";
                    });
                }
                finally
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            await Flashlight.TurnOffAsync();
                        }
                        catch { }
                        _flashlightActive = false;
                    });
                }
            }, _flashlightCts.Token);
        }

        private async void OnResetClicked(object sender, EventArgs e)
        {
            count = 0;
            CounterBtn.Text = "Нажать";
            _flashlightActive = false;

            if (!_flashlightCts.IsCancellationRequested)
            {
                _flashlightCts.Cancel();
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () => await Flashlight.TurnOffAsync());
                }
                catch (Exception ex)
                {
                    messageLabel.Text = $"Ошибка при выключении фонарика: {ex.Message}";
                }
                _flashlightCts.Dispose();
                _flashlightCts = new CancellationTokenSource();
            }

            SemanticScreenReader.Announce(CounterBtn.Text);
        }
    }
}