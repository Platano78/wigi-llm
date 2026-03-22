using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace LLMModelSelectorWidget
{
    /// <summary>
    /// Widget instance implementation - polling, rendering, and lifecycle.
    /// This is a partial class - see WidgetInstanceBase.cs for properties.
    /// </summary>
    public partial class LLMModelSelectorWidgetInstance
    {
        // Threading
        private Thread _taskThread;
        private volatile bool _runTask = false;
        private volatile bool _pauseTask = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private RouterApiClient _apiClient;

        // Fonts and colors
        private readonly Font _titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font _normalFont = new Font("Segoe UI", 8f);
        private readonly Font _smallFont = new Font("Segoe UI", 7f);

        private readonly Color _bgColor = Color.FromArgb(30, 30, 30);
        private readonly Color _headerColor = Color.FromArgb(45, 45, 45);
        private readonly Color _buttonColor = Color.FromArgb(0, 122, 204);
        private readonly Color _dangerColor = Color.FromArgb(204, 50, 50);
        private readonly Color _successColor = Color.FromArgb(76, 175, 80);
        private readonly Color _textColor = Color.White;
        private readonly Color _dimTextColor = Color.FromArgb(180, 180, 180);
        private readonly Color _dropdownColor = Color.FromArgb(40, 40, 40);
        private readonly Color _selectedItemColor = Color.FromArgb(60, 60, 60);

        public LLMModelSelectorWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            Initialize(parent, widget_size, instance_guid, resourcePath);
            LoadSettings();

            // Initialize API client
            _apiClient = new RouterApiClient(RouterUrl);

            // Initialize with empty state
            CurrentStatus = new RouterStatus
            {
                Models = new System.Collections.Generic.List<ModelInfo>(),
                LoadedModels = new System.Collections.Generic.List<string>(),
                TotalVram = "Detecting...",
                LoadedCount = 0
            };

            DrawFrame();
            StartTask();
        }

        public void Initialize(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);
        }

        public void StartTask()
        {
            _pauseTask = false;
            _runTask = true;

            _taskThread = new Thread(UpdateTask);
            _taskThread.IsBackground = true;
            _taskThread.Start();
        }

        private void UpdateTask()
        {
            while (_runTask)
            {
                if (!_pauseTask && !IsLoading)
                {
                    try
                    {
                        UpdateStatusAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch { /* Ignore update errors */ }

                    if (_drawingMutex.WaitOne(MutexTimeout))
                    {
                        try
                        {
                            DrawFrame();
                            UpdateWidget();
                        }
                        finally
                        {
                            _drawingMutex.ReleaseMutex();
                        }
                    }
                }

                Thread.Sleep(PollingInterval);
            }
        }

        private async Task UpdateStatusAsync()
        {
            try
            {
                CurrentStatus = await _apiClient.GetStatusAsync();

                // Set default selection to first loaded model or first model
                if (string.IsNullOrEmpty(SelectedModel))
                {
                    if (CurrentStatus.LoadedModels.Count > 0)
                    {
                        SelectedModel = CurrentStatus.LoadedModels[0];
                    }
                    else if (CurrentStatus.Models.Count > 0)
                    {
                        SelectedModel = CurrentStatus.Models[0].Name;
                    }
                }
            }
            catch (Exception ex)
            {
                LoadingMessage = $"Error: {ex.Message}";
            }
        }

        private void DrawFrame()
        {
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(_bgColor);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int padding = 10;
                int y = padding;

                // Header
                using (var headerBrush = new SolidBrush(_headerColor))
                using (var textBrush = new SolidBrush(_textColor))
                {
                    var headerRect = new Rectangle(0, 0, BitmapCurrent.Width, 30);
                    g.FillRectangle(headerBrush, headerRect);
                    g.DrawString("LLM Model Selector", _titleFont, textBrush, padding, 8);
                }
                y += 35;

                // Dropdown box
                var dropdownRect = new Rectangle(padding, y, BitmapCurrent.Width - (padding * 2), 30);
                RenderDropdown(g, dropdownRect);
                y += 40;

                // Action buttons
                var buttonWidth = (BitmapCurrent.Width - (padding * 5)) / 3;
                var loadRect = new Rectangle(padding, y, buttonWidth, 30);
                var unloadRect = new Rectangle(loadRect.Right + padding, y, buttonWidth, 30);
                var statusRect = new Rectangle(unloadRect.Right + padding, y, buttonWidth, 30);

                RenderButton(g, loadRect, "▶ LOAD", _buttonColor, IsLoading);
                RenderButton(g, unloadRect, "■ UNLOAD", _dangerColor, IsLoading);
                RenderButton(g, statusRect, "⟳ STATUS", _successColor, false);
                y += 40;

                // Status section
                using (var textBrush = new SolidBrush(_textColor))
                using (var dimBrush = new SolidBrush(_dimTextColor))
                {
                    if (IsLoading)
                    {
                        g.DrawString(LoadingMessage, _normalFont, textBrush, padding, y);
                        y += 20;
                    }
                    else
                    {
                        // Active model
                        var activeModel = CurrentStatus.LoadedModels.Count > 0
                            ? CurrentStatus.LoadedModels[0]
                            : "None";
                        g.DrawString($"Active: {activeModel}", _normalFont, textBrush, padding, y);
                        y += 20;

                        // VRAM usage
                        g.DrawString($"VRAM: {CurrentStatus.TotalVram}", _normalFont, dimBrush, padding, y);
                        y += 20;

                        // Status indicator
                        var statusText = CurrentStatus.LoadedCount > 0
                            ? $"● Running ({CurrentStatus.LoadedCount} loaded)"
                            : "○ Idle";
                        var statusColor = CurrentStatus.LoadedCount > 0 ? _successColor : _dimTextColor;
                        using (var statusBrush = new SolidBrush(statusColor))
                        {
                            g.DrawString(statusText, _normalFont, statusBrush, padding, y);
                        }
                        y += 25;

                        // Loaded models list
                        if (CurrentStatus.LoadedModels.Count > 0)
                        {
                            g.DrawString("Loaded Models:", _normalFont, textBrush, padding, y);
                            y += 18;

                            foreach (var model in CurrentStatus.LoadedModels)
                            {
                                var isPrimary = model == CurrentStatus.LoadedModels[0];
                                var modelText = isPrimary ? $"• {model} (primary)" : $"• {model} (standby)";
                                g.DrawString(modelText, _smallFont, dimBrush, padding + 10, y);
                                y += 16;
                            }
                        }
                    }
                }

                // Render dropdown menu if shown (drawn on top)
                if (ShowDropdown)
                {
                    RenderDropdownMenu(g, dropdownRect);
                }
            }
        }

        private void RenderDropdown(Graphics g, Rectangle rect)
        {
            using (var dropdownBrush = new SolidBrush(_dropdownColor))
            using (var borderPen = new Pen(_buttonColor, 1))
            using (var textBrush = new SolidBrush(_textColor))
            {
                g.FillRectangle(dropdownBrush, rect);
                g.DrawRectangle(borderPen, rect);

                var displayText = string.IsNullOrEmpty(SelectedModel) ? "Select Model ▼" : $"{SelectedModel} ▼";
                var textFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(displayText, _normalFont, textBrush, new Rectangle(rect.X + 8, rect.Y, rect.Width - 16, rect.Height), textFormat);
            }
        }

        private void RenderDropdownMenu(Graphics g, Rectangle dropdownRect)
        {
            // Use smaller item height (18px) and larger max height to show all 8 models
            int itemHeight = 18;
            var menuHeight = Math.Min(CurrentStatus.Models.Count * itemHeight, 160);
            var menuRect = new Rectangle(dropdownRect.X, dropdownRect.Bottom, dropdownRect.Width, menuHeight);

            using (var menuBrush = new SolidBrush(_dropdownColor))
            using (var borderPen = new Pen(_buttonColor, 1))
            using (var textBrush = new SolidBrush(_textColor))
            using (var dimBrush = new SolidBrush(_dimTextColor))
            {
                g.FillRectangle(menuBrush, menuRect);
                g.DrawRectangle(borderPen, menuRect);

                var itemY = menuRect.Y;
                for (int i = 0; i < CurrentStatus.Models.Count; i++)
                {
                    var model = CurrentStatus.Models[i];
                    var itemRect = new Rectangle(menuRect.X, itemY, menuRect.Width, itemHeight);

                    // Highlight selected
                    if (i == SelectedIndex || model.Name == SelectedModel)
                    {
                        using (var selectedBrush = new SolidBrush(_selectedItemColor))
                        {
                            g.FillRectangle(selectedBrush, itemRect);
                        }
                    }

                    // Model name and status
                    var loadedIndicator = model.IsLoaded ? "● " : "○ ";
                    var displayText = $"{loadedIndicator}{model.Name} ({model.VramEstimate})";
                    var brush = model.IsLoaded ? textBrush : dimBrush;
                    g.DrawString(displayText, _smallFont, brush, itemRect.X + 4, itemRect.Y + 2);

                    itemY += itemHeight;
                }
            }
        }

        private void RenderButton(Graphics g, Rectangle rect, string text, Color color, bool disabled)
        {
            var buttonColor = disabled ? Color.FromArgb(60, 60, 60) : color;

            using (var buttonBrush = new SolidBrush(buttonColor))
            using (var textBrush = new SolidBrush(_textColor))
            {
                g.FillRectangle(buttonBrush, rect);

                var textFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, _normalFont, textBrush, rect, textFormat);
            }
        }

        private void UpdateWidget()
        {
            if (BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;

                WidgetUpdated?.Invoke(this, e);
            }
        }

        private async Task LoadModelAsync(string modelName)
        {
            IsLoading = true;
            LoadingMessage = $"Loading {modelName}...";
            DrawFrame();
            UpdateWidget();

            try
            {
                var success = await _apiClient.LoadModelAsync(modelName);
                LoadingMessage = success
                    ? $"✓ Loaded {modelName}"
                    : $"✗ Failed to load {modelName}";

                DrawFrame();
                UpdateWidget();
                await Task.Delay(2000);
                await UpdateStatusAsync();
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = "";
            }
        }

        private async Task UnloadModelAsync(string modelName)
        {
            IsLoading = true;
            LoadingMessage = $"Unloading {modelName}...";
            DrawFrame();
            UpdateWidget();

            try
            {
                var success = await _apiClient.UnloadModelAsync(modelName);
                LoadingMessage = success
                    ? $"✓ Unloaded {modelName}"
                    : $"✗ Failed to unload {modelName}";

                DrawFrame();
                UpdateWidget();
                await Task.Delay(2000);
                await UpdateStatusAsync();
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = "";
            }
        }

        // IWidgetInstance interface
        public void RequestUpdate()
        {
            if (_drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    UpdateStatusAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    DrawFrame();
                    UpdateWidget();
                }
                catch { /* Ignore update errors */ }
                finally
                {
                    _drawingMutex.ReleaseMutex();
                }
            }
        }

        public virtual void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                HandleClick(new Point(x, y));
            }
        }

        private void HandleClick(Point location)
        {
            int padding = 10;
            int y = 35; // After header

            // Dropdown rect
            var dropdownRect = new Rectangle(padding, y, BitmapCurrent.Width - (padding * 2), 30);

            if (ShowDropdown)
            {
                // Check dropdown menu clicks
                int itemHeight = 18;
                var menuHeight = Math.Min(CurrentStatus.Models.Count * itemHeight, 160);
                var menuRect = new Rectangle(dropdownRect.X, dropdownRect.Bottom, dropdownRect.Width, menuHeight);

                if (menuRect.Contains(location))
                {
                    var itemIndex = (location.Y - menuRect.Y) / itemHeight;
                    if (itemIndex >= 0 && itemIndex < CurrentStatus.Models.Count)
                    {
                        SelectedModel = CurrentStatus.Models[itemIndex].Name;
                        SelectedIndex = itemIndex;
                    }
                }
                ShowDropdown = false;
                DrawFrame();
                UpdateWidget();
                return;
            }

            // Toggle dropdown
            if (dropdownRect.Contains(location))
            {
                ShowDropdown = !ShowDropdown;
                DrawFrame();
                UpdateWidget();
                return;
            }

            y += 40; // After dropdown

            // Button rects
            var buttonWidth = (BitmapCurrent.Width - (padding * 5)) / 3;
            var loadRect = new Rectangle(padding, y, buttonWidth, 30);
            var unloadRect = new Rectangle(loadRect.Right + padding, y, buttonWidth, 30);
            var statusRect = new Rectangle(unloadRect.Right + padding, y, buttonWidth, 30);

            if (!IsLoading)
            {
                if (loadRect.Contains(location) && !string.IsNullOrEmpty(SelectedModel))
                {
                    Task.Run(async () => await LoadModelAsync(SelectedModel));
                }
                else if (unloadRect.Contains(location) && !string.IsNullOrEmpty(SelectedModel))
                {
                    Task.Run(async () => await UnloadModelAsync(SelectedModel));
                }
                else if (statusRect.Contains(location))
                {
                    Task.Run(async () => await UpdateStatusAsync());
                }
            }
        }

        public void Dispose()
        {
            _pauseTask = true;
            _runTask = false;

            // Wait for thread to finish
            if (_taskThread != null && _taskThread.IsAlive)
            {
                _taskThread.Join(1000);
            }

            // Dispose mutex
            _drawingMutex?.Dispose();

            _apiClient?.Dispose();
            BitmapCurrent?.Dispose();
            _titleFont?.Dispose();
            _normalFont?.Dispose();
            _smallFont?.Dispose();
        }

        public void EnterSleep()
        {
            _pauseTask = true;
        }

        public void ExitSleep()
        {
            _pauseTask = false;
        }

        // Settings persistence
        public virtual void UpdateSettings()
        {
            // Update API client with new URL
            _apiClient = new RouterApiClient(RouterUrl);
            DrawFrame();
            UpdateWidget();
        }

        public virtual void SaveSettings()
        {
            WidgetObject.WidgetManager.StoreSetting(this, "RouterUrl", RouterUrl);
            WidgetObject.WidgetManager.StoreSetting(this, "PollingInterval", PollingInterval.ToString());
        }

        public virtual void LoadSettings()
        {
            if (WidgetObject.WidgetManager.LoadSetting(this, "RouterUrl", out string url))
            {
                RouterUrl = url;
            }

            if (WidgetObject.WidgetManager.LoadSetting(this, "PollingInterval", out string intervalStr))
            {
                if (int.TryParse(intervalStr, out int interval))
                {
                    PollingInterval = interval;
                }
            }
        }
    }
}
