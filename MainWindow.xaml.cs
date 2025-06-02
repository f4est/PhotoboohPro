using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace UnifiedPhotoBooth
{
    public partial class MainWindow : Window
    {
        private GoogleDriveService _driveService;
        private Dictionary<string, string> _eventFolders;
        private WindowState _previousWindowState;
        private double _windowNormalWidth;
        private double _windowNormalHeight;
        private double _windowNormalLeft;
        private double _windowNormalTop;
        
        public MainWindow()
        {
            InitializeComponent();
            _driveService = new GoogleDriveService();
            _eventFolders = new Dictionary<string, string>();
            _previousWindowState = WindowState;
            
            // Загрузка списка событий
            RefreshEvents();
            
            // Добавляем обработчик изменения выбора события
            cbEvents.SelectionChanged += cbEvents_SelectionChanged;
            
            // По умолчанию открываем режим фотобудки
            MainFrame.Navigate(new PhotoBoothPage(_driveService));
        }
        
        private void RefreshEvents()
        {
            try
            {
                cbEvents.Items.Clear();
                _eventFolders.Clear();
                
                // Получаем список папок-событий
                var events = _driveService.ListEvents();
                
                if (events.Count > 0)
                {
                    cbEvents.Items.Add("Выберите событие");
                    foreach (var eventItem in events)
                    {
                        cbEvents.Items.Add(eventItem.Key);
                        _eventFolders[eventItem.Key] = eventItem.Value;
                    }
                    cbEvents.SelectedIndex = 0;
                }
                else
                {
                    cbEvents.Items.Add("Нет доступных событий");
                    cbEvents.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке списка событий: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void cbEvents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbEvents.SelectedIndex <= 0 || MainFrame.Content == null)
                return;

            var selectedEvent = cbEvents.SelectedItem.ToString();
            string eventFolderId = _eventFolders.ContainsKey(selectedEvent) ? _eventFolders[selectedEvent] : null;

            // Применяем выбранное событие к текущей странице
            if (MainFrame.Content is PhotoBoothPage photoPage)
            {
                MainFrame.Navigate(new PhotoBoothPage(_driveService, eventFolderId));
            }
            else if (MainFrame.Content is VideoBoothPage videoPage)
            {
                MainFrame.Navigate(new VideoBoothPage(_driveService, eventFolderId));
            }
        }
        
        private void BtnPhotoMode_Click(object sender, RoutedEventArgs e)
        {
            var selectedEvent = cbEvents.SelectedIndex > 0 ? cbEvents.SelectedItem.ToString() : null;
            string eventFolderId = null;
            
            if (selectedEvent != null && _eventFolders.ContainsKey(selectedEvent))
            {
                eventFolderId = _eventFolders[selectedEvent];
            }
            
            MainFrame.Navigate(new PhotoBoothPage(_driveService, eventFolderId));
        }
        
        private void BtnVideoMode_Click(object sender, RoutedEventArgs e)
        {
            var selectedEvent = cbEvents.SelectedIndex > 0 ? cbEvents.SelectedItem.ToString() : null;
            string eventFolderId = null;
            
            if (selectedEvent != null && _eventFolders.ContainsKey(selectedEvent))
            {
                eventFolderId = _eventFolders[selectedEvent];
            }
            
            MainFrame.Navigate(new VideoBoothPage(_driveService, eventFolderId));
        }
        
        private void BtnNewEvent_Click(object sender, RoutedEventArgs e)
        {
            // Окно для ввода имени нового события
            var dialog = new InputDialog("Введите название события:", "Новое событие");
            if (dialog.ShowDialog() == true)
            {
                string eventName = dialog.Answer;
                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    try
                    {
                        string newEventId = _driveService.CreateEvent(eventName);
                        if (newEventId != null)
                        {
                            MessageBox.Show($"Событие '{eventName}' успешно создано.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                            RefreshEvents();
                            
                            // Выбираем новое событие в комбобоксе
                            for (int i = 0; i < cbEvents.Items.Count; i++)
                            {
                                if (cbEvents.Items[i].ToString() == eventName)
                                {
                                    cbEvents.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при создании события: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }
        
        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ToggleFullscreen();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при переключении полноэкранного режима: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ToggleFullscreen()
        {
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                // Выход из полноэкранного режима
                ExitFullscreenMode();
            }
            else
            {
                // Вход в полноэкранный режим
                EnterFullscreenMode();
            }
            
            UpdateFullscreenButtonIcon();
        }
        
        private void EnterFullscreenMode()
        {
            _previousWindowState = WindowState;
            
            // Сохраняем текущие размеры и положение окна
            if (WindowState != WindowState.Maximized)
            {
                _windowNormalWidth = Width;
                _windowNormalHeight = Height;
                _windowNormalLeft = Left;
                _windowNormalTop = Top;
            }
            
            // Настройки для полноэкранного режима
            WindowState = WindowState.Normal; // Сначала сбрасываем в нормальное состояние
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            
            // Устанавливаем размер и положение на весь экран
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        
        private void ExitFullscreenMode()
        {
            // Восстанавливаем стиль окна
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            
            // Восстанавливаем предыдущее состояние окна
            if (_previousWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Width = _windowNormalWidth;
                Height = _windowNormalHeight;
                Left = _windowNormalLeft;
                Top = _windowNormalTop;
            }
        }
        
        private void UpdateFullscreenButtonIcon()
        {
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None || 
                (WindowStyle == WindowStyle.None && Width == SystemParameters.PrimaryScreenWidth && Height == SystemParameters.PrimaryScreenHeight))
            {
                // В полноэкранном режиме
                btnFullscreen.Content = "⮽";
                btnFullscreen.ToolTip = "Выйти из полноэкранного режима (F11)";
            }
            else
            {
                // В обычном режиме
                btnFullscreen.Content = "⛶";
                btnFullscreen.ToolTip = "Полноэкранный режим (F11)";
            }
        }
        
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            // Переключение полноэкранного режима по F11
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            // Выход из полноэкранного режима по Escape
            else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
            {
                ExitFullscreenMode();
                UpdateFullscreenButtonIcon();
                e.Handled = true;
            }
        }
    }
    
    // Простой диалог ввода текста
    public class InputDialog : Window
    {
        private TextBox txtAnswer;
        private Button btnDialogOk;
        
        public string Answer { get; private set; }
        
        public InputDialog(string question, string title)
        {
            this.Title = title;
            this.Width = 400;
            this.Height = 150;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
            Grid grid = new Grid();
            grid.Margin = new Thickness(10);
            
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            
            TextBlock textQuestion = new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 10) };
            grid.Children.Add(textQuestion);
            Grid.SetRow(textQuestion, 0);
            
            txtAnswer = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            grid.Children.Add(txtAnswer);
            Grid.SetRow(txtAnswer, 1);
            
            btnDialogOk = new Button { Content = "OK", Width = 80, Height = 30, HorizontalAlignment = HorizontalAlignment.Right };
            btnDialogOk.Click += BtnDialogOk_Click;
            grid.Children.Add(btnDialogOk);
            Grid.SetRow(btnDialogOk, 2);
            
            this.Content = grid;
            
            this.Loaded += (s, e) => txtAnswer.Focus();
            txtAnswer.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Answer = txtAnswer.Text; DialogResult = true; } };
        }
        
        private void BtnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            Answer = txtAnswer.Text;
            DialogResult = true;
        }
    }
} 