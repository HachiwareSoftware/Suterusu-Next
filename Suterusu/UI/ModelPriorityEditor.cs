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
        private readonly TextBox _keyTextBox;
        private readonly ComboBox _modelCombo;
        private readonly ComboBox _capabilityCombo;
        private readonly ComboBox _reasoningCombo;
        private readonly TextBox _reasoningCustomBox;
        private readonly Button _fetchButton;
        private readonly ComboBox _presetCombo;
        private readonly Action<string> _showValidation;
        private readonly Action _hideValidation;
        private readonly Func<string> _getCliProxyApiKey;
        private readonly ILogger _logger;

        private int _editingIndex = -2; // -2 = not editing, -1 = adding, >= 0 = editing
        private bool _isApplyingPreset;
        private bool _isSyncingPreset;
        private readonly Dictionary<string, List<string>> _reasoningEffortsByModel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private const string DefaultReasoningEffort = "default";
        private const string CustomReasoningEffort = "custom...";

        private static readonly EndpointPreset CustomPreset =
            new EndpointPreset { Name = "Custom", BaseUrl = string.Empty, DefaultModel = string.Empty, RequiresApiKey = false };

        public ModelPriorityEditor(
            ListBox list,
            Border editPanel,
            TextBlock editTitle,
            TextBox nameBox,
            TextBox urlBox,
            PasswordBox keyBox,
            TextBox keyTextBox,
            ComboBox modelCombo,
            ComboBox capabilityCombo,
            ComboBox reasoningCombo,
            TextBox reasoningCustomBox,
            Button fetchButton,
            ComboBox presetCombo,
            Action<string> showValidation,
            Action hideValidation,
            Func<string> getCliProxyApiKey,
            ILogger logger)
        {
            _list = list;
            _editPanel = editPanel;
            _editTitle = editTitle;
            _nameBox = nameBox;
            _urlBox = urlBox;
            _keyBox = keyBox;
            _keyTextBox = keyTextBox;
            _modelCombo = modelCombo;
            _capabilityCombo = capabilityCombo;
            _reasoningCombo = reasoningCombo;
            _reasoningCustomBox = reasoningCustomBox;
            _fetchButton = fetchButton;
            _presetCombo = presetCombo;
            _showValidation = showValidation;
            _hideValidation = hideValidation;
            _getCliProxyApiKey = getCliProxyApiKey;
            _logger = logger;

            _presetCombo.ItemsSource = EndpointPreset.GetPresets();
            _presetCombo.SelectionChanged += OnPresetChanged;
            _urlBox.TextChanged += OnUrlChanged;
            _modelCombo.SelectionChanged += OnModelSelectionChanged;
            _modelCombo.LostFocus += OnModelLostFocus;
            _reasoningCombo.SelectionChanged += OnReasoningChanged;
            ResetReasoningOptions(DefaultReasoningEffort);
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
            string apiKey = GetApiKey();
            string model = _modelCombo.Text.Trim();
            ModelCapability capability = GetCapability();
            string reasoningEffort = GetReasoningEffort();

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

            if (string.IsNullOrWhiteSpace(reasoningEffort))
            {
                _showValidation("Custom reasoning level is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
                name = "Custom";

            var entry = new ModelEntry
            {
                Name = name,
                BaseUrl = url.TrimEnd('/'),
                ApiKey = apiKey,
                Model = model,
                Capability = capability,
                ReasoningEffort = reasoningEffort
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

            string apiKey = GetApiKey();
            _logger.Debug($"Fetching models from {modelsUrl}. Auth header present: {!string.IsNullOrWhiteSpace(apiKey)}.");

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
                        _logger.Debug($"Model fetch response: {(int)response.StatusCode} {response.ReasonPhrase}.");
                        response.EnsureSuccessStatusCode();

                        string json = await response.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        _logger.Debug($"Model fetch parsed. data_count={data?.Count ?? 0}.");

                        _modelCombo.Items.Clear();
                        _reasoningEffortsByModel.Clear();
                        if (data != null)
                        {
                            foreach (var item in data)
                            {
                                string id = (string)item["id"];
                                if (!string.IsNullOrWhiteSpace(id))
                                {
                                    _modelCombo.Items.Add(id);
                                    var efforts = ExtractReasoningEfforts(item).ToList();
                                    _logger.Debug($"Model '{id}' direct reasoning levels: {FormatReasoningEfforts(efforts)}.");
                                    if (efforts.Count == 0)
                                    {
                                        string detailsUrl = GetModelDetailsUrl(item, modelsUrl);
                                        if (!string.IsNullOrWhiteSpace(detailsUrl))
                                        {
                                            _logger.Debug($"Model '{id}' has details URL for reasoning metadata: {detailsUrl}.");
                                            var detailEfforts = await FetchReasoningEffortsFromDetailsAsync(client, detailsUrl, _logger, cts.Token);
                                            efforts.AddRange(detailEfforts.Where(e => !efforts.Any(existing => string.Equals(existing, e, StringComparison.OrdinalIgnoreCase))));
                                            _logger.Debug($"Model '{id}' details reasoning levels: {FormatReasoningEfforts(detailEfforts)}.");
                                        }
                                        else
                                        {
                                            _logger.Debug($"Model '{id}' has no direct reasoning levels and no details URL.");
                                        }
                                    }

                                    if (efforts.Count > 0)
                                    {
                                        _reasoningEffortsByModel[id] = efforts;
                                        _logger.Debug($"Model '{id}' final reasoning levels: {FormatReasoningEfforts(efforts)}.");
                                    }
                                }
                            }
                        }

                        if (_modelCombo.Items.Count > 0)
                        {
                            _modelCombo.SelectedIndex = 0;
                            ResetReasoningOptions(DefaultReasoningEffort);
                        }
                        else
                        {
                            ResetReasoningOptions(DefaultReasoningEffort);
                            _showValidation("No models returned. Enter model name manually.");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Model fetch timed out from {modelsUrl}.");
                _showValidation("Model fetch timed out.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to fetch models from {modelsUrl}.", ex);
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
            if (string.IsNullOrWhiteSpace(GetApiKey()))
            {
                string key = !string.IsNullOrWhiteSpace(preset.DefaultApiKey)
                    ? preset.DefaultApiKey
                    : string.Equals(preset.Name, "CLIProxyAPI", StringComparison.OrdinalIgnoreCase)
                        ? _getCliProxyApiKey?.Invoke() ?? string.Empty
                        : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                    _keyBox.Password = key;
            }
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
            _keyTextBox.Text = string.Empty;
            _modelCombo.Text = string.Empty;
            _modelCombo.Items.Clear();
            _reasoningEffortsByModel.Clear();
            SetCapability(ModelCapability.Auto);
            ResetReasoningOptions(DefaultReasoningEffort);

            _isSyncingPreset = true;
            _presetCombo.SelectedIndex = -1;
            _isSyncingPreset = false;
        }

        private void PopulateForm(ModelEntry entry)
        {
            _nameBox.Text = entry.Name ?? string.Empty;
            _urlBox.Text = entry.BaseUrl ?? string.Empty;
            _keyBox.Password = entry.ApiKey ?? string.Empty;
            _keyTextBox.Text = entry.ApiKey ?? string.Empty;
            _modelCombo.Items.Clear();
            _modelCombo.Text = entry.Model ?? string.Empty;
            SetCapability(entry.Capability);
            ResetReasoningOptions(string.IsNullOrWhiteSpace(entry.ReasoningEffort) ? DefaultReasoningEffort : entry.ReasoningEffort.Trim());

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

        public string GetApiKey()
        {
            return _keyTextBox.Visibility == Visibility.Visible
                ? _keyTextBox.Text ?? string.Empty
                : _keyBox.Password ?? string.Empty;
        }

        public void ToggleApiKeyVisibility()
        {
            if (_keyBox.Visibility == Visibility.Visible)
            {
                _keyTextBox.Text = _keyBox.Password;
                _keyBox.Visibility = Visibility.Collapsed;
                _keyTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                _keyBox.Password = _keyTextBox.Text ?? string.Empty;
                _keyTextBox.Visibility = Visibility.Collapsed;
                _keyBox.Visibility = Visibility.Visible;
            }
        }

        private ModelCapability GetCapability()
        {
            if (_capabilityCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string raw
                && Enum.TryParse(raw, out ModelCapability capability))
            {
                return capability;
            }

            return ModelCapability.Auto;
        }

        private void SetCapability(ModelCapability capability)
        {
            foreach (ComboBoxItem item in _capabilityCombo.Items)
            {
                if (item.Tag is string raw && Enum.TryParse(raw, out ModelCapability itemCapability) && itemCapability == capability)
                {
                    _capabilityCombo.SelectedItem = item;
                    return;
                }
            }

            _capabilityCombo.SelectedIndex = 0;
        }

        private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReasoningOptionsForCurrentModel(preserveCurrent: false);
        }

        private void OnModelLostFocus(object sender, RoutedEventArgs e)
        {
            UpdateReasoningOptionsForCurrentModel(preserveCurrent: true);
        }

        private void OnReasoningChanged(object sender, SelectionChangedEventArgs e)
        {
            _reasoningCustomBox.Visibility = IsCustomReasoningSelected()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateReasoningOptionsForCurrentModel(bool preserveCurrent)
        {
            string current = preserveCurrent ? GetReasoningEffort() : DefaultReasoningEffort;
            ResetReasoningOptions(current);
        }

        private void ResetReasoningOptions(string selected)
        {
            selected = string.IsNullOrWhiteSpace(selected) ? DefaultReasoningEffort : selected.Trim();
            string model = (_modelCombo.SelectedItem as string)?.Trim();
            if (string.IsNullOrWhiteSpace(model))
                model = _modelCombo.Text?.Trim() ?? string.Empty;
            var options = new List<string> { DefaultReasoningEffort };

            if (!string.IsNullOrWhiteSpace(model) && _reasoningEffortsByModel.TryGetValue(model, out var fetched))
            {
                foreach (string effort in fetched)
                {
                    if (!options.Any(o => string.Equals(o, effort, StringComparison.OrdinalIgnoreCase)))
                        options.Add(effort);
                }
            }

            options.Add(CustomReasoningEffort);
            _logger.Debug($"Reasoning dropdown reset for model '{model}'. selected='{selected}', options={FormatReasoningEfforts(options)}.");

            _reasoningCombo.Items.Clear();
            foreach (string option in options)
                _reasoningCombo.Items.Add(CreateReasoningOption(option));

            string matched = options.FirstOrDefault(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                SelectReasoningOption(matched);
                _reasoningCustomBox.Text = string.Empty;
            }
            else
            {
                SelectReasoningOption(CustomReasoningEffort);
                _reasoningCustomBox.Text = selected;
            }

            _reasoningCustomBox.Visibility = IsCustomReasoningSelected()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private string GetReasoningEffort()
        {
            if (IsCustomReasoningSelected())
                return _reasoningCustomBox.Text?.Trim() ?? string.Empty;

            return GetReasoningOptionValue(_reasoningCombo.SelectedItem) ?? DefaultReasoningEffort;
        }

        private bool IsCustomReasoningSelected()
        {
            return string.Equals(GetReasoningOptionValue(_reasoningCombo.SelectedItem), CustomReasoningEffort, StringComparison.OrdinalIgnoreCase);
        }

        private static ComboBoxItem CreateReasoningOption(string value)
        {
            return new ComboBoxItem
            {
                Content = ToReasoningDisplayName(value),
                Tag = value
            };
        }

        private void SelectReasoningOption(string value)
        {
            foreach (var item in _reasoningCombo.Items)
            {
                if (string.Equals(GetReasoningOptionValue(item), value, StringComparison.OrdinalIgnoreCase))
                {
                    _reasoningCombo.SelectedItem = item;
                    return;
                }
            }
        }

        private static string GetReasoningOptionValue(object item)
        {
            if (item is ComboBoxItem comboItem)
                return comboItem.Tag as string ?? comboItem.Content as string;

            return item as string;
        }

        private static string ToReasoningDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Default";

            if (string.Equals(value, CustomReasoningEffort, StringComparison.OrdinalIgnoreCase))
                return "Custom...";

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        public static IReadOnlyList<string> ExtractReasoningEfforts(JToken modelMetadata)
        {
            var values = new List<string>();
            AddReasoningValues(values, modelMetadata?["reasoning_efforts"]);
            AddReasoningValues(values, modelMetadata?["reasoning_effort"]);
            AddReasoningValues(values, modelMetadata?["supported_reasoning_efforts"]);
            AddReasoningValues(values, modelMetadata?["supported_reasoning_levels"]);
            AddReasoningValues(values, modelMetadata?["reasoning"]?["levels"]);
            AddReasoningValues(values, modelMetadata?["reasoning"]?["efforts"]);
            AddReasoningValues(values, modelMetadata?["reasoning"]?["values"]);
            AddReasoningValues(values, modelMetadata?["reasoning"]?["supported_efforts"]);
            AddReasoningValues(values, modelMetadata?["capabilities"]?["reasoning_efforts"]);
            AddReasoningValues(values, modelMetadata?["capabilities"]?["reasoning"]?["levels"]);
            AddReasoningValues(values, modelMetadata?["capabilities"]?["reasoning"]?["efforts"]);

            return values;
        }

        private static async Task<IReadOnlyList<string>> FetchReasoningEffortsFromDetailsAsync(HttpClient client, string detailsUrl, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                var response = await client.GetAsync(detailsUrl, cancellationToken);
                logger.Debug($"Reasoning details fetch response from {detailsUrl}: {(int)response.StatusCode} {response.ReasonPhrase}.");
                if (!response.IsSuccessStatusCode)
                {
                    return new List<string>();
                }

                string json = await response.Content.ReadAsStringAsync();
                var efforts = ExtractReasoningEffortsFromDetails(JObject.Parse(json));
                logger.Debug($"Reasoning details fetch parsed from {detailsUrl}: {FormatReasoningEfforts(efforts)}.");
                return efforts;
            }
            catch (Exception ex)
            {
                logger.Warn($"Reasoning details fetch failed from {detailsUrl}: {ex.Message}");
                return new List<string>();
            }
        }

        public static IReadOnlyList<string> ExtractReasoningEffortsFromDetails(JToken details)
        {
            var values = new List<string>();
            AddReasoningEffortsFromToken(values, details);
            AddReasoningEffortsFromToken(values, details?["data"]);
            AddReasoningEffortsFromToken(values, details?["result"]);

            var endpoints = details?["data"]?["endpoints"] as JArray
                ?? details?["endpoints"] as JArray;
            if (endpoints != null)
            {
                foreach (var endpoint in endpoints)
                    AddReasoningEffortsFromToken(values, endpoint);
            }

            return values;
        }

        private static void AddReasoningEffortsFromToken(List<string> values, JToken token)
        {
            foreach (string effort in ExtractReasoningEfforts(token))
                AddReasoningValue(values, effort);
        }

        private static string GetModelDetailsUrl(JToken modelMetadata, string modelsUrl)
        {
            string rawDetailsUrl = (string)modelMetadata?["links"]?["details"]
                ?? (string)modelMetadata?["details_url"]
                ?? (string)modelMetadata?["detailsUrl"];

            if (string.IsNullOrWhiteSpace(rawDetailsUrl))
                return null;

            rawDetailsUrl = rawDetailsUrl.Trim();
            if (Uri.TryCreate(rawDetailsUrl, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            if (!Uri.TryCreate(modelsUrl, UriKind.Absolute, out var modelsUri))
                return null;

            var root = new Uri(modelsUri.GetLeftPart(UriPartial.Authority));
            if (Uri.TryCreate(root, rawDetailsUrl, out var relativeUri))
                return relativeUri.ToString();

            return null;
        }

        private static void AddReasoningValues(List<string> values, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return;

            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                    AddReasoningValues(values, item);
                return;
            }

            if (token.Type == JTokenType.Object)
            {
                AddReasoningValues(values, token["levels"]);
                AddReasoningValues(values, token["values"]);
                AddReasoningValues(values, token["supported"]);
                return;
            }

            string value = token.ToString().Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            AddReasoningValue(values, value);
        }

        private static void AddReasoningValue(List<string> values, string value)
        {
            if (!values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)))
                values.Add(value);
        }

        private static string FormatReasoningEfforts(IEnumerable<string> efforts)
        {
            if (efforts == null)
                return "[]";

            var values = efforts.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            return values.Count == 0
                ? "[]"
                : "[" + string.Join(", ", values) + "]";
        }
    }
}
