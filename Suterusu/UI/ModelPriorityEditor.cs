using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.UI
{
    internal class ModelPriorityEditor
    {
        private readonly ListBox _list;
        private readonly Border _editPanel;
        private readonly TextBlock _editTitle;
        private readonly TextBox _nameBox;
        private readonly TextBox _urlBox;
        private readonly PasswordBox _keyBox;
        private readonly ComboBox _modelCombo;
        private readonly Button _fetchButton;
        private readonly ComboBox _presetCombo;
        private readonly Action<string> _showValidation;
        private readonly Action _hideValidation;
        private readonly ILogger _logger;

        private int _editingIndex = -2; // -2 = not editing, -1 = adding, >= 0 = editing
        private bool _isApplyingPreset;
        private bool _isSyncingPreset;

        private static readonly EndpointPreset CustomPreset =
            new EndpointPreset { Name = "Custom", BaseUrl = string.Empty, DefaultModel = string.Empty, RequiresApiKey = false };

        public ModelPriorityEditor(
            ListBox list,
            Border editPanel,
            TextBlock editTitle,
            TextBox nameBox,
            TextBox urlBox,
            PasswordBox keyBox,
            ComboBox modelCombo,
            Button fetchButton,
            ComboBox presetCombo,
            Action<string> showValidation,
            Action hideValidation,
            ILogger logger)
        {
            _list = list;
            _editPanel = editPanel;
            _editTitle = editTitle;
            _nameBox = nameBox;
            _urlBox = urlBox;
            _keyBox = keyBox;
            _modelCombo = modelCombo;
            _fetchButton = fetchButton;
            _presetCombo = presetCombo;
            _showValidation = showValidation;
            _hideValidation = hideValidation;
            _logger = logger;

            _presetCombo.ItemsSource = EndpointPreset.GetPresets();
            _presetCombo.SelectionChanged += OnPresetChanged;
            _urlBox.TextChanged += OnUrlChanged;
        }

        public bool IsEditing => _editingIndex >= -1;

        public void LoadListFrom(IReadOnlyList<ModelEntry> entries)
        {
            _list.Items.Clear();
            if (entries != null)
            {
                foreach (var entry in entries)
                    _list.Items.Add(entry.Clone());
            }
        }

        public IReadOnlyList<ModelEntry> GetEntries()
        {
            return _list.Items.Cast<ModelEntry>().ToList();
        }

        public void OnAdd()
        {
            _editingIndex = -1;
            _editTitle.Text = "Add Entry";
            ClearForm();
            ShowPanel();
        }

        public void OnEdit()
        {
            int index = _list.SelectedIndex;
            if (index < 0)
                return;

            _editingIndex = index;
            _editTitle.Text = "Edit Entry";
            PopulateForm((ModelEntry)_list.Items[index]);
            ShowPanel();
        }

        public void OnDelete()
        {
            int index = _list.SelectedIndex;
            if (index < 0)
                return;

            _list.Items.RemoveAt(index);
            if (_list.Items.Count > 0)
                _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
        }

        public void OnMoveUp()
        {
            int index = _list.SelectedIndex;
            if (index <= 0)
                return;

            var item = _list.Items[index];
            _list.Items.RemoveAt(index);
            _list.Items.Insert(index - 1, item);
            _list.SelectedIndex = index - 1;
        }

        public void OnMoveDown()
        {
            int index = _list.SelectedIndex;
            if (index < 0 || index >= _list.Items.Count - 1)
                return;

            var item = _list.Items[index];
            _list.Items.RemoveAt(index);
            _list.Items.Insert(index + 1, item);
            _list.SelectedIndex = index + 1;
        }

        public void OnConfirm()
        {
            string name = _nameBox.Text.Trim();
            string url = _urlBox.Text.Trim();
            string apiKey = _keyBox.Password;
            string model = _modelCombo.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                _showValidation("Base URL is required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                _showValidation("Model is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
                name = "Custom";

            var entry = new ModelEntry
            {
                Name = name,
                BaseUrl = url.TrimEnd('/'),
                ApiKey = apiKey,
                Model = model
            };

            if (_editingIndex == -1)
            {
                int insertAt = _list.SelectedIndex >= 0
                    ? _list.SelectedIndex + 1
                    : _list.Items.Count;
                _list.Items.Insert(insertAt, entry);
                _list.SelectedIndex = insertAt;
            }
            else
            {
                _list.Items[_editingIndex] = entry;
                _list.SelectedIndex = _editingIndex;
            }

            HidePanel();
            _hideValidation();
        }

        public void OnDiscard()
        {
            HidePanel();
            _hideValidation();
        }

        public async Task OnFetchModelsAsync()
        {
            string rawUrl = _urlBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                _showValidation("Enter a Base URL first.");
                return;
            }

            string modelsUrl = rawUrl.TrimEnd('/');
            if (modelsUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                modelsUrl = modelsUrl.Substring(0, modelsUrl.Length - "/chat/completions".Length);
            modelsUrl += "/models";

            string apiKey = _keyBox.Password;

            _fetchButton.IsEnabled = false;
            _fetchButton.Content = "Fetching...";
            _hideValidation();

            try
            {
                using (var client = new HttpClient())
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", apiKey);

                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var response = await client.GetAsync(modelsUrl, cts.Token);
                        response.EnsureSuccessStatusCode();

                        string json = await response.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;

                        _modelCombo.Items.Clear();
                        if (data != null)
                        {
                            foreach (var item in data)
                            {
                                string id = (string)item["id"];
                                if (!string.IsNullOrWhiteSpace(id))
                                    _modelCombo.Items.Add(id);
                            }
                        }

                        if (_modelCombo.Items.Count > 0)
                            _modelCombo.SelectedIndex = 0;
                        else
                            _showValidation("No models returned. Enter model name manually.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _showValidation("Model fetch timed out.");
            }
            catch (Exception ex)
            {
                _showValidation($"Failed to fetch models: {ex.Message}");
            }
            finally
            {
                _fetchButton.IsEnabled = true;
                _fetchButton.Content = "Fetch Models";
            }
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingPreset)
                return;

            var preset = _presetCombo.SelectedItem as EndpointPreset;
            if (preset == null || string.IsNullOrWhiteSpace(preset.BaseUrl))
                return;

            _isApplyingPreset = true;
            _urlBox.Text = preset.BaseUrl;
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
                _nameBox.Text = preset.Name;
            if (string.IsNullOrWhiteSpace(_modelCombo.Text) && !string.IsNullOrWhiteSpace(preset.DefaultModel))
                _modelCombo.Text = preset.DefaultModel;
            _isApplyingPreset = false;
        }

        private void OnUrlChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingPreset)
                return;

            var customPreset = _presetCombo.Items
                .OfType<EndpointPreset>()
                .FirstOrDefault(p => p.Name == "Custom");

            if (customPreset != null && !ReferenceEquals(_presetCombo.SelectedItem, customPreset))
            {
                _isSyncingPreset = true;
                _presetCombo.SelectedItem = customPreset;
                _isSyncingPreset = false;
            }
        }

        private void ClearForm()
        {
            _nameBox.Text = string.Empty;
            _urlBox.Text = string.Empty;
            _keyBox.Clear();
            _modelCombo.Text = string.Empty;
            _modelCombo.Items.Clear();

            _isSyncingPreset = true;
            _presetCombo.SelectedIndex = -1;
            _isSyncingPreset = false;
        }

        private void PopulateForm(ModelEntry entry)
        {
            _nameBox.Text = entry.Name ?? string.Empty;
            _urlBox.Text = entry.BaseUrl ?? string.Empty;
            _keyBox.Password = entry.ApiKey ?? string.Empty;
            _modelCombo.Items.Clear();
            _modelCombo.Text = entry.Model ?? string.Empty;

            var matchedPreset = _presetCombo.Items
                .OfType<EndpointPreset>()
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl)
                    && p.BaseUrl.TrimEnd('/').Equals(
                        (entry.BaseUrl ?? string.Empty).TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase));

            _isSyncingPreset = true;
            _presetCombo.SelectedItem = matchedPreset
                ?? _presetCombo.Items.OfType<EndpointPreset>().FirstOrDefault(p => p.Name == "Custom");
            _isSyncingPreset = false;
        }

        private void ShowPanel()
        {
            _editPanel.Visibility = Visibility.Visible;
            _nameBox.Focus();
        }

        private void HidePanel()
        {
            _editPanel.Visibility = Visibility.Collapsed;
            _editingIndex = -2;
        }
    }
}
