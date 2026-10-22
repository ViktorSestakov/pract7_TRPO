using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace pr7_shestakov
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const string Route = "api/v1/Pet";
        private readonly HttpClient hc = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7193/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private Pet? openedPet;
        private bool busy;
        public bool IsNotBusy => !busy;
        public ObservableCollection<Pet> Pets { get; } = new();
        public Pet? SelectedPet { get; set; }
        public string PetName { get; set; } = "";
        public string PetSpecies { get; set; } = "";
        public string AgeText { get; set; } = "0";
        public string WeightText { get; set; } = "";
        public bool PetIsAdopted { get; set; }
        public string FilterSpecies { get; set; } = "";
        public int FilterStatus { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunAsync(GetItems);
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(GetItems);
        private async void Apply_Click(object sender, RoutedEventArgs e) => await RunAsync(GetItems);

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            FilterSpecies = "";
            FilterStatus = 0;
            Notify();
            await RunAsync(GetItems);
        }

        private async Task GetItems()
        {
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(FilterSpecies))
                filters.Add("species=" + Uri.EscapeDataString(FilterSpecies.Trim()));
            if (FilterStatus == 1) filters.Add("adopted=false");
            if (FilterStatus == 2) filters.Add("adopted=true");

            var url = Route + (filters.Count == 0 ? "" : "?" + string.Join("&", filters));
            var pets = await hc.GetFromJsonAsync<List<Pet>>(url) ?? throw new Exception("API вернул пустой ответ.");
            ClearForm();
            Pets.Clear();
            foreach (var pet in pets) Pets.Add(pet);
        }

        private async void Pets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (busy || SelectedPet == null) return;
            int id = SelectedPet.Id;

            FillForm(null);
            await RunAsync(async () =>
            {
                var pet = await hc.GetFromJsonAsync<Pet>($"{Route}/{id}" ?? throw new Exception("API вернул пустую запись."));
                FillForm(pet);
            });
        }

        private void FillForm(Pet? pet)
        {
            openedPet = pet;
            PetName = pet?.Name ?? "";
            PetSpecies = pet?.Species ?? "";
            AgeText = pet?.Age.ToString() ?? "0";
            WeightText = pet?.Weight.ToString() ?? "";
            PetIsAdopted = pet?.IsAdopted ?? false;
            Notify();
        }

        private void ClearForm()
        {
            SelectedPet = null;
            FillForm(null);
        }

        private Pet ReadForm()
        {
            if (string.IsNullOrWhiteSpace(PetName) || string.IsNullOrWhiteSpace(PetSpecies))
                throw new Exception("Заполните имя и вид питомца.");
            if (!int.TryParse(AgeText, out int age) || age < 0)
                throw new Exception("Возраст должен быть целым неотрицательным числом.");
            if (!decimal.TryParse(WeightText.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out decimal weight) || weight <= 0)
                throw new Exception("Вес должен быть числом больше нуля, например 4,5.");

            return new Pet
            {
                Name = PetName.Trim(),
                Species = PetSpecies.Trim(),
                Age = age,
                Weight = weight,
                IsAdopted = PetIsAdopted
            };
        }

        private Pet RequirePet() => openedPet ?? throw new Exception("Сначала выберите питомца в списке.");

        private async void Create_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            using var response = await hc.PostAsJsonAsync(Route, ReadForm());
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void ChangeFull_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            int id = RequirePet().Id;
            var pet = ReadForm();
            pet.Id = id;
            using var response = await hc.PutAsJsonAsync($"{Route}/{id}", pet);
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void Change_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var old = RequirePet();
            var pet = ReadForm();
            var changes = new Dictionary<string, object>();
            if (pet.Name != old.Name) changes["name"] = pet.Name;
            if (pet.Species != old.Species) changes["species"] = pet.Species;
            if (pet.Age != old.Age) changes["age"] = pet.Age;
            if (pet.Weight != old.Weight) changes["weight"] = pet.Weight;
            if (pet.IsAdopted != old.IsAdopted) changes["isAdopted"] = pet.IsAdopted;
            if (changes.Count == 0) { MessageBox.Show("Нет изменений."); return; }

            using var content = JsonContent.Create(changes);
            using var response = await hc.PatchAsync($"{Route}/{old.Id}", content);
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void Del_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            int id = RequirePet().Id;
            if (MessageBox.Show("Удалить выбранного питомца?", "Удаление",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            using var response = await hc.DeleteAsync($"{Route}/{id}");
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async Task RefreshAfterChange()
        {
            ClearForm();
            try { await GetItems(); }
            catch { throw new Exception("Операция выполнена, но список не обновился. Нажмите «Обновить» после восстановления связи."); }
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            Notify();
            try { await action(); }
            catch (HttpRequestException ex)
            {
                MessageBox.Show(ex.StatusCode == null
                    ? "Сервер недоступен. Проверьте, что API запущен и адрес указан верно."
                    : $"Ошибка API: {(int) ex.StatusCode.Value}. {ex.Message}");
            }
            catch (TaskCanceledException) { MessageBox.Show("Сервер не ответил за 15 секунд."); }
            catch (JsonException) { MessageBox.Show("API вернул данные неверного формата."); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { busy = false; Notify(); }
        }
    }

    public class Pet
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Species { get; set; } = "";
        public int Age { get; set; }
        public decimal Weight { get; set; }
        public bool IsAdopted { get; set; }
    }
}
