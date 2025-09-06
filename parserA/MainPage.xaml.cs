using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Controls;

namespace parserA
{
    public partial class MainPage : ContentPage
    {
        private const string KEY = "58e310878dcae97b7fd2ed9b73f6d716";
        private HttpClient _client = new HttpClient();
        private int count = 0;

        public MainPage()
        {
            InitializeComponent();
        }

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

        private async void UpdateButtonClicked(object sender, EventArgs e)
        {
            messageLabel.Text = string.Empty;
            try
            {
                (double lat, double lon) = await GetLocationByName(cityEntry.Text);
                temperatureLabel.Text = (await GetTemperature(lat, lon)).ToString();
            }
            catch (Exception ex)
            {
                messageLabel.Text += ex.Message;
            }

            try
            {
                rateLabel.Text = $"1$ = {Convert.ToInt32(await GetDollarRate())}p.";
            }
            catch (Exception ex)
            {
                messageLabel.Text += ex.Message;
            }
        }

        private void OnCounterClicked(object sender, EventArgs e)
        {
            count++;
            CounterBtn.Text = $"Нажато {count} раз";
            SemanticScreenReader.Announce(CounterBtn.Text);
        }

        private void OnResetClicked(object sender, EventArgs e)
        {
            count = 0;
            CounterBtn.Text = "Нажать";
        }
    }
}