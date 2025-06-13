using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace UnifiedPhotoBooth
{
    public partial class MainWindow : System.Windows.Window
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
            InputDialog inputDialog = new InputDialog("Введите название события:", "Новое событие");
            if (inputDialog.ShowDialog() == true)
            {
                string newEventName = inputDialog.Answer.Trim();
                if (!string.IsNullOrEmpty(newEventName))
                {
                    try
                    {
                        // Создаем новое событие
                        _driveService.CreateEvent(newEventName);
                        
                        // Обновляем список событий
                        RefreshEvents();
                        
                        // Выбираем новое событие в списке
                        for (int i = 0; i < cbEvents.Items.Count; i++)
                        {
                            if (cbEvents.Items[i].ToString() == newEventName)
                            {
                                cbEvents.SelectedIndex = i;
                                break;
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
        
        private void BtnGallery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainFrame.Navigate(new GalleryPage());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии галереи: {ex.Message}", "Ошибка", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }
        
        private void ToggleFullscreen()
        {
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                ExitFullscreenMode();
            }
            else
            {
                EnterFullscreenMode();
            }
            
            UpdateFullscreenButtonIcon();
        }
        
        private void EnterFullscreenMode()
        {
            // Сохраняем текущее состояние окна
            _previousWindowState = WindowState;
            _windowNormalWidth = Width;
            _windowNormalHeight = Height;
            _windowNormalLeft = Left;
            _windowNormalTop = Top;

            // Скрываем панель задач
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            ShowWindow(taskbar, 0); // SW_HIDE

            // Устанавливаем полноэкранный режим
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
        }
        
        private void ExitFullscreenMode()
        {
            // Показываем панель задач
            var taskbar = FindWindow("Shell_TrayWnd", null);
            ShowWindow(taskbar, 1); // SW_SHOW

            // Восстанавливаем предыдущее состояние
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _previousWindowState;
            Width = _windowNormalWidth;
            Height = _windowNormalHeight;
            Left = _windowNormalLeft;
            Top = _windowNormalTop;
            Topmost = false;
        }
        
        private void UpdateFullscreenButtonIcon()
        {
            // Обновляем иконку кнопки полноэкранного режима
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                // В полноэкранном режиме показываем иконку выхода из него
                btnFullscreen.Content = "⮽";
                btnFullscreen.ToolTip = "Выйти из полноэкранного режима (F11)";
            }
            else
            {
                // В обычном режиме показываем иконку входа в полноэкранный режим
                btnFullscreen.Content = "⛶";
                btnFullscreen.ToolTip = "Полноэкранный режим (F11)";
            }
        }
        
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            // Обработка нажатия клавиши F11 для переключения полноэкранного режима
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            
            // Обработка нажатия клавиши Escape для выхода из полноэкранного режима
            if (e.Key == Key.Escape && WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                ExitFullscreenMode();
                UpdateFullscreenButtonIcon();
                e.Handled = true;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
    
    public class InputDialog : System.Windows.Window
    {
        private TextBox txtAnswer;
        private Button btnDialogOk;
        
        public string Answer { get; private set; }
        
        public InputDialog(string question, string title)
        {
            this.Title = title;
            
            // Создаем элементы диалогового окна
            Grid grid = new Grid();
            grid.Margin = new Thickness(10);
            
            // Определяем строки
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Добавляем текст вопроса
            TextBlock questionText = new TextBlock();
            questionText.Text = question;
            questionText.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(questionText, 0);
            grid.Children.Add(questionText);
            
            // Добавляем поле ввода
            txtAnswer = new TextBox();
            txtAnswer.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(txtAnswer, 1);
            grid.Children.Add(txtAnswer);
            
            // Добавляем кнопки
            StackPanel buttonsPanel = new StackPanel();
            buttonsPanel.Orientation = Orientation.Horizontal;
            buttonsPanel.HorizontalAlignment = HorizontalAlignment.Right;
            
            Button btnDialogCancel = new Button();
            btnDialogCancel.Content = "Отмена";
            btnDialogCancel.Margin = new Thickness(5, 0, 0, 0);
            btnDialogCancel.Click += (sender, e) => { this.DialogResult = false; this.Close(); };
            
            btnDialogOk = new Button();
            btnDialogOk.Content = "OK";
            btnDialogOk.IsDefault = true;
            btnDialogOk.Click += BtnDialogOk_Click;
            
            buttonsPanel.Children.Add(btnDialogOk);
            buttonsPanel.Children.Add(btnDialogCancel);
            
            Grid.SetRow(buttonsPanel, 2);
            grid.Children.Add(buttonsPanel);
            
            // Добавляем сетку в окно
            this.Content = grid;
            
            // Настройки окна
            this.Width = 300;
            this.SizeToContent = SizeToContent.Height;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;
        }
        
        private void BtnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            Answer = txtAnswer.Text;
            DialogResult = true;
            Close();
        }
    }
} 