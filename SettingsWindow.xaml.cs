using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Linq;

namespace UnifiedPhotoBooth
{
    // Определим перечисление для позиций маркеров изменения размера
    public enum ResizeThumbPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    // Перечисление для режимов обработки изображений
    public enum ImageProcessingMode
    {
        Stretch,      // Растягивание
        Crop,         // Обрезка
        Scale         // Масштабирование с сохранением пропорций
    }

    public partial class SettingsWindow : Window
    {
        // Статические настройки приложения
        public static AppSettings AppSettings { get; private set; } = new AppSettings();
        
        // Флаг для обозначения того, было ли окно настроек инициализировано
        private static bool _isInitialized = false;
        
        // Переменные для редактирования позиций фотографий
        private Canvas _positionCanvas;
        private List<PositionRect> _positionRects;
        private BitmapImage _frameTemplateImage;
        private Window _positionWindow;
        
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Если настройки еще не были инициализированы, инициализируем их
            if (!_isInitialized)
            {
                LoadSettings();
                _isInitialized = true;
            }
            
            // Инициализация элементов интерфейса значениями из настроек
            InitializeUIFromSettings();
        }
        
        private void InitializeUIFromSettings()
        {
            // Заполняем список камер
            RefreshCameraList();
            
            // Заполняем список микрофонов
            RefreshMicrophoneList();
            
            // Устанавливаем выбранную камеру
            if (cbCameras.Items.Count > AppSettings.CameraIndex && AppSettings.CameraIndex >= 0)
            {
                cbCameras.SelectedIndex = AppSettings.CameraIndex;
            }
            else if (cbCameras.Items.Count > 0)
            {
                cbCameras.SelectedIndex = 0;
            }
            
            // Устанавливаем выбранный микрофон
            if (cbMicrophones.Items.Count > AppSettings.MicrophoneIndex && AppSettings.MicrophoneIndex >= 0)
            {
                cbMicrophones.SelectedIndex = AppSettings.MicrophoneIndex;
            }
            else if (cbMicrophones.Items.Count > 0)
            {
                cbMicrophones.SelectedIndex = 0;
            }
            
            // Устанавливаем режим поворота
            if (!string.IsNullOrEmpty(AppSettings.RotationMode))
            {
                foreach (ComboBoxItem item in cbRotation.Items)
                {
                    if (item.Content.ToString() == AppSettings.RotationMode)
                    {
                        cbRotation.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                cbRotation.SelectedIndex = 0;
            }
            
            // Устанавливаем зеркальное отображение
            chkMirrorMode.IsChecked = AppSettings.MirrorMode;
            
            // Устанавливаем количество фотографий
            string photoCount = AppSettings.PhotoCount.ToString();
            foreach (ComboBoxItem item in cbPhotoCount.Items)
            {
                if (item.Content.ToString() == photoCount)
                {
                    cbPhotoCount.SelectedItem = item;
                    break;
                }
            }
            
            // Устанавливаем время отсчета для фото
            string photoCountdownTime = AppSettings.PhotoCountdownTime.ToString();
            foreach (ComboBoxItem item in cbPhotoCountdownTime.Items)
            {
                if (item.Content.ToString() == photoCountdownTime)
                {
                    cbPhotoCountdownTime.SelectedItem = item;
                    break;
                }
            }
            
            // Устанавливаем длительность записи видео
            string recordingDuration = AppSettings.RecordingDuration.ToString();
            foreach (ComboBoxItem item in cbRecordingDuration.Items)
            {
                if (item.Content.ToString() == recordingDuration)
                {
                    cbRecordingDuration.SelectedItem = item;
                    break;
                }
            }
            
            // Устанавливаем время отсчета для видео
            string videoCountdownTime = AppSettings.VideoCountdownTime.ToString();
            foreach (ComboBoxItem item in cbVideoCountdownTime.Items)
            {
                if (item.Content.ToString() == videoCountdownTime)
                {
                    cbVideoCountdownTime.SelectedItem = item;
                    break;
                }
            }
            
            // Режим обработки фотографий
            int photoProcessingModeIndex = (int)AppSettings.PhotoProcessingMode;
            if (photoProcessingModeIndex >= 0 && photoProcessingModeIndex < cbPhotoProcessingMode.Items.Count)
            {
                cbPhotoProcessingMode.SelectedIndex = photoProcessingModeIndex;
            }
            else
            {
                cbPhotoProcessingMode.SelectedIndex = 0; // По умолчанию растягивание
            }
            
            // Заполняем список принтеров
            RefreshPrinterList();
            
            // Выбираем сохраненный принтер
            if (!string.IsNullOrEmpty(AppSettings.PrinterName))
            {
                foreach (string printer in cbPrinters.Items)
                {
                    if (printer == AppSettings.PrinterName)
                    {
                        cbPrinters.SelectedItem = printer;
                        break;
                    }
                }
            }
            else if (cbPrinters.Items.Count > 0)
            {
                cbPrinters.SelectedIndex = 0;
            }
            
            // Устанавливаем размеры печати
            txtPrintWidth.Text = AppSettings.PrintWidth.ToString("F2");
            txtPrintHeight.Text = AppSettings.PrintHeight.ToString("F2");
            
            // Режим обработки для печати
            int printProcessingModeIndex = (int)AppSettings.PrintProcessingMode;
            if (printProcessingModeIndex >= 0 && printProcessingModeIndex < cbPrintProcessingMode.Items.Count)
            {
                cbPrintProcessingMode.SelectedIndex = printProcessingModeIndex;
            }
            else
            {
                cbPrintProcessingMode.SelectedIndex = 0; // По умолчанию растягивание
            }
            
            // Если путь к рамке существует, отображаем его
            if (!string.IsNullOrEmpty(AppSettings.FrameTemplatePath))
            {
                txtFrameTemplatePath.Text = AppSettings.FrameTemplatePath;
            }
            
            // Устанавливаем текущий режим обработки для отображения в интерфейсе
            cbPhotoProcessingMode.SelectedIndex = (int)AppSettings.PhotoProcessingMode;
            cbPrintProcessingMode.SelectedIndex = (int)AppSettings.PrintProcessingMode;
        }
        
        private void RefreshCameraList()
        {
            cbCameras.Items.Clear();
            
            // Добавляем доступные камеры
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    // Пытаемся открыть камеру
                    using (var capture = new OpenCvSharp.VideoCapture(i))
                    {
                        if (capture.IsOpened())
                        {
                            cbCameras.Items.Add($"Камера {i}");
                        }
                    }
                }
                catch { }
            }
            
            if (cbCameras.Items.Count == 0)
            {
                cbCameras.Items.Add("Камеры не найдены");
            }
        }
        
        private void RefreshMicrophoneList()
        {
            cbMicrophones.Items.Clear();
            
            // Получаем список доступных устройств ввода
            for (int i = 0; i < NAudio.Wave.WaveIn.DeviceCount; i++)
            {
                var capabilities = NAudio.Wave.WaveIn.GetCapabilities(i);
                cbMicrophones.Items.Add($"{capabilities.ProductName}");
            }
            
            if (cbMicrophones.Items.Count == 0)
            {
                cbMicrophones.Items.Add("Микрофоны не найдены");
            }
        }
        
        private void RefreshPrinterList()
        {
            cbPrinters.Items.Clear();
            
            // Получаем список принтеров из системы
            foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                cbPrinters.Items.Add(printer);
            }
            
            if (cbPrinters.Items.Count == 0)
            {
                cbPrinters.Items.Add("Принтеры не найдены");
            }
        }
        
        private void BtnTestCamera_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = cbCameras.SelectedIndex;
            if (selectedIndex < 0) return;
            
            // Создаем окно для тестирования камеры
            var testWindow = new Window
            {
                Title = "Тест камеры",
                Width = 640,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            
            // Создаем элемент для отображения видео
            var image = new System.Windows.Controls.Image();
            testWindow.Content = image;
            
            // Открываем камеру
            var capture = new OpenCvSharp.VideoCapture(selectedIndex);
            if (!capture.IsOpened())
            {
                MessageBox.Show("Не удалось открыть выбранную камеру", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Настраиваем таймер для обновления изображения
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 fps
            };
            
            // Режим поворота
            string rotationMode = ((ComboBoxItem)cbRotation.SelectedItem).Content.ToString();
            
            // Зеркальное отображение
            bool mirrorMode = chkMirrorMode.IsChecked == true;
            
            timer.Tick += (s, args) =>
            {
                using (var frame = new OpenCvSharp.Mat())
                {
                    if (capture.Read(frame))
                    {
                        // Применяем поворот
                        OpenCvSharp.RotateFlags? rotateFlags = null;
                        switch (rotationMode)
                        {
                            case "90° вправо (вертикально)":
                                rotateFlags = OpenCvSharp.RotateFlags.Rotate90Clockwise;
                                break;
                            case "90° влево (вертикально)":
                                rotateFlags = OpenCvSharp.RotateFlags.Rotate90Counterclockwise;
                                break;
                            case "180°":
                                rotateFlags = OpenCvSharp.RotateFlags.Rotate180;
                                break;
                        }
                        
                        if (rotateFlags.HasValue)
                        {
                            OpenCvSharp.Cv2.Rotate(frame, frame, rotateFlags.Value);
                        }
                        
                        // Применяем зеркальное отображение
                        if (mirrorMode)
                        {
                            OpenCvSharp.Cv2.Flip(frame, frame, OpenCvSharp.FlipMode.Y);
                        }
                        
                        // Отображаем кадр
                        image.Source = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(frame);
                    }
                }
            };
            
            timer.Start();
            
            // Обработчик закрытия окна
            testWindow.Closed += (s, args) =>
            {
                timer.Stop();
                capture.Dispose();
            };
            
            testWindow.ShowDialog();
        }
        
        private void BtnLoadFrameTemplate_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*",
                Title = "Выберите изображение рамки"
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                // Сохраняем путь к выбранному файлу
                AppSettings.FrameTemplatePath = openFileDialog.FileName;
                txtFrameTemplatePath.Text = openFileDialog.FileName;
                
                MessageBox.Show("Рамка загружена успешно.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void BtnSetupPositions_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, выбран ли шаблон рамки
            if (string.IsNullOrEmpty(AppSettings.FrameTemplatePath) || !File.Exists(AppSettings.FrameTemplatePath))
            {
                MessageBox.Show("Сначала загрузите шаблон рамки.", "Необходим шаблон", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Загружаем изображение шаблона
            _frameTemplateImage = new BitmapImage();
            _frameTemplateImage.BeginInit();
            _frameTemplateImage.UriSource = new Uri(AppSettings.FrameTemplatePath, UriKind.Absolute);
            _frameTemplateImage.EndInit();
            
            // Создаем новое окно для настройки позиций
            _positionWindow = new Window
            {
                Title = "Настройка позиций фотографий",
                Owner = this,
                Width = 1000,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            
            // Создаем Grid для размещения элементов
            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Создаем панель для отображения шаблона и настройки позиций
            Canvas canvas = new Canvas
            {
                Width = _frameTemplateImage.PixelWidth,
                Height = _frameTemplateImage.PixelHeight,
                Background = new ImageBrush(_frameTemplateImage)
            };
            
            // Создаем ScrollViewer для канвы, если изображение большое
            ScrollViewer scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = canvas
            };
            Grid.SetRow(scrollViewer, 0);
            mainGrid.Children.Add(scrollViewer);
            
            // Сохраняем ссылку на canvas для дальнейшего использования
            _positionCanvas = canvas;
            
            // Панель для числовых настроек позиций
            StackPanel positionSettingsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10)
            };
            Grid.SetRow(positionSettingsPanel, 1);
            mainGrid.Children.Add(positionSettingsPanel);
            
            // Создаем элементы управления для выбора текущей области
            var positionControlGrid = new Grid();
            positionControlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            positionControlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            positionControlGrid.Children.Add(new TextBlock
            {
                Text = "Выберите область:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            
            ComboBox positionSelector = new ComboBox
            {
                Margin = new Thickness(5),
                MinWidth = 150
            };
            Grid.SetColumn(positionSelector, 1);
            positionControlGrid.Children.Add(positionSelector);
            
            positionSettingsPanel.Children.Add(positionControlGrid);
            
            // Сетка для ввода координат и размеров
            var coordinatesGrid = new Grid();
            coordinatesGrid.Margin = new Thickness(0, 10, 0, 0);
            
            // Определяем строки и столбцы
            for (int i = 0; i < 4; i++)
                coordinatesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coordinatesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            coordinatesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            coordinatesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            coordinatesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Заголовки
            var txtBlockX = new TextBlock { Text = "X:", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            coordinatesGrid.Children.Add(txtBlockX);
            Grid.SetRow(txtBlockX, 0);
            
            var txtBlockY = new TextBlock { Text = "Y:", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            coordinatesGrid.Children.Add(txtBlockY);
            Grid.SetRow(txtBlockY, 0);
            Grid.SetColumn(txtBlockY, 2);
            
            // Поля для X и Y
            TextBox txtPosX = new TextBox { Margin = new Thickness(5), Width = 80, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtPosX, 1);
            Grid.SetRow(txtPosX, 0);
            coordinatesGrid.Children.Add(txtPosX);
            
            TextBox txtPosY = new TextBox { Margin = new Thickness(5), Width = 80, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtPosY, 3);
            Grid.SetRow(txtPosY, 0);
            coordinatesGrid.Children.Add(txtPosY);
            
            // Заголовки для ширины и высоты
            var txtBlockWidth = new TextBlock { Text = "Ширина:", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            coordinatesGrid.Children.Add(txtBlockWidth);
            Grid.SetRow(txtBlockWidth, 1);
            
            var txtBlockHeight = new TextBlock { Text = "Высота:", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            coordinatesGrid.Children.Add(txtBlockHeight);
            Grid.SetRow(txtBlockHeight, 1);
            Grid.SetColumn(txtBlockHeight, 2);
            
            // Поля для ширины и высоты
            TextBox txtPosWidth = new TextBox { Margin = new Thickness(5), Width = 80, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtPosWidth, 1);
            Grid.SetRow(txtPosWidth, 1);
            coordinatesGrid.Children.Add(txtPosWidth);
            
            TextBox txtPosHeight = new TextBox { Margin = new Thickness(5), Width = 80, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtPosHeight, 3);
            Grid.SetRow(txtPosHeight, 1);
            coordinatesGrid.Children.Add(txtPosHeight);
            
            // Кнопка применения изменений
            Button btnApplyCoordinates = new Button
            {
                Content = "Применить координаты",
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumnSpan(btnApplyCoordinates, 4);
            Grid.SetRow(btnApplyCoordinates, 2);
            coordinatesGrid.Children.Add(btnApplyCoordinates);
            
            positionSettingsPanel.Children.Add(coordinatesGrid);
            
            // Добавляем обработчик выбора позиции
            positionSelector.SelectionChanged += (s, ev) =>
            {
                if (positionSelector.SelectedIndex >= 0 && _positionRects != null && _positionRects.Count > positionSelector.SelectedIndex)
                {
                    var selectedPos = _positionRects[positionSelector.SelectedIndex];
                    double x = Canvas.GetLeft(selectedPos.Rectangle);
                    double y = Canvas.GetTop(selectedPos.Rectangle);
                    
                    txtPosX.Text = Math.Round(x).ToString();
                    txtPosY.Text = Math.Round(y).ToString();
                    txtPosWidth.Text = Math.Round(selectedPos.Rectangle.Width).ToString();
                    txtPosHeight.Text = Math.Round(selectedPos.Rectangle.Height).ToString();
                }
            };
            
            // Добавляем обработчик изменения координат
            btnApplyCoordinates.Click += (s, ev) =>
            {
                if (positionSelector.SelectedIndex >= 0 && _positionRects != null && _positionRects.Count > positionSelector.SelectedIndex)
                {
                    var selectedPos = _positionRects[positionSelector.SelectedIndex];
                    
                    // Парсим значения
                    if (double.TryParse(txtPosX.Text, out double x) &&
                        double.TryParse(txtPosY.Text, out double y) &&
                        double.TryParse(txtPosWidth.Text, out double width) &&
                        double.TryParse(txtPosHeight.Text, out double height))
                    {
                        // Применяем ограничения
                        x = Math.Max(0, Math.Min(x, _positionCanvas.Width - 10));
                        y = Math.Max(0, Math.Min(y, _positionCanvas.Height - 10));
                        width = Math.Max(50, Math.Min(width, _positionCanvas.Width - x));
                        height = Math.Max(50, Math.Min(height, _positionCanvas.Height - y));
                        
                        // Обновляем позицию и размеры
                        Canvas.SetLeft(selectedPos.Rectangle, x);
                        Canvas.SetTop(selectedPos.Rectangle, y);
                        selectedPos.Rectangle.Width = width;
                        selectedPos.Rectangle.Height = height;
                        
                        // Обновляем позицию метки с номером
                        Canvas.SetLeft(selectedPos.NumberLabel, x + width / 2 - 15);
                        Canvas.SetTop(selectedPos.NumberLabel, y + height / 2 - 15);
                        
                        // Обновляем позиции маркеров изменения размера
                        selectedPos.NotifyPositionChanged();
                    }
                }
            };
            
            // Создаем панель с кнопками управления
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };
            Grid.SetRow(buttonPanel, 2);
            
            Button btnAddPosition = new Button
            {
                Content = "Добавить область",
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5)
            };
            buttonPanel.Children.Add(btnAddPosition);
            
            Button btnRemovePosition = new Button
            {
                Content = "Удалить область",
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5)
            };
            buttonPanel.Children.Add(btnRemovePosition);
            
            Button btnSavePositions = new Button
            {
                Content = "Сохранить",
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = Brushes.White
            };
            buttonPanel.Children.Add(btnSavePositions);
            
            Button btnCancel = new Button
            {
                Content = "Отмена",
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5)
            };
            buttonPanel.Children.Add(btnCancel);
            
            mainGrid.Children.Add(buttonPanel);
            
            // Инициализируем список прямоугольников для позиций
            _positionRects = new List<PositionRect>();
            
            // Добавляем существующие позиции, если они есть
            int positionIndex = 1;
            if (AppSettings.PhotoPositions != null && AppSettings.PhotoPositions.Count > 0)
            {
                foreach (var position in AppSettings.PhotoPositions)
                {
                    AddPositionRect(position.X, position.Y, position.Width, position.Height, positionIndex);
                    positionSelector.Items.Add($"Область {positionIndex}");
                    positionIndex++;
                }
            }
            
            // Обработчик добавления новой позиции
            btnAddPosition.Click += (s, ev) =>
            {
                // Вычисляем центр канвы для размещения нового прямоугольника
                double centerX = _positionCanvas.Width / 2 - 100;
                double centerY = _positionCanvas.Height / 2 - 100;
                
                // Добавляем новый прямоугольник
                AddPositionRect(centerX, centerY, 200, 200, positionIndex);
                
                // Обновляем выпадающий список
                positionSelector.Items.Add($"Область {positionIndex}");
                positionSelector.SelectedIndex = positionIndex - 1;
                
                positionIndex++;
            };
            
            // Обработчик удаления позиции
            btnRemovePosition.Click += (s, ev) =>
            {
                int selectedIndex = positionSelector.SelectedIndex;
                if (selectedIndex >= 0 && _positionRects.Count > selectedIndex)
                {
                    // Удаляем прямоугольник и его элементы управления
                    var rect = _positionRects[selectedIndex];
                    _positionCanvas.Children.Remove(rect.Rectangle);
                    _positionCanvas.Children.Remove(rect.NumberLabel);
                    foreach (var thumb in rect.ResizeThumbs)
                    {
                        _positionCanvas.Children.Remove(thumb);
                    }
                    
                    _positionRects.RemoveAt(selectedIndex);
                    
                    // Обновляем нумерацию
                    for (int i = selectedIndex; i < _positionRects.Count; i++)
                    {
                        _positionRects[i].Number = i + 1;
                        _positionRects[i].NumberLabel.Text = (i + 1).ToString();
                    }
                    
                    // Обновляем выпадающий список
                    positionSelector.Items.Clear();
                    for (int i = 0; i < _positionRects.Count; i++)
                    {
                        positionSelector.Items.Add($"Область {i + 1}");
                    }
                    
                    positionSelector.SelectedIndex = Math.Min(selectedIndex, _positionRects.Count - 1);
                    
                    // Обновляем индекс для добавления новых позиций
                    positionIndex = _positionRects.Count + 1;
                }
            };
            
            // Обработчик сохранения позиций
            btnSavePositions.Click += (s, ev) =>
            {
                // Очищаем текущие позиции фотографий
                AppSettings.PhotoPositions.Clear();
                
                // Сохраняем новые позиции из _positionRects
                foreach (var posRect in _positionRects)
                {
                    double x = Canvas.GetLeft(posRect.Rectangle);
                    double y = Canvas.GetTop(posRect.Rectangle);
                    double width = posRect.Rectangle.Width;
                    double height = posRect.Rectangle.Height;
                    
                    AppSettings.PhotoPositions.Add(new PhotoPosition
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height
                    });
                }
                
                // Логируем сохраненные позиции для отладки
                System.Diagnostics.Debug.WriteLine($"Сохранены настроенные позиции ({AppSettings.PhotoPositions.Count}):");
                for (int i = 0; i < AppSettings.PhotoPositions.Count; i++)
                {
                    var pos = AppSettings.PhotoPositions[i];
                    System.Diagnostics.Debug.WriteLine($"Позиция {i+1}: X={pos.X}, Y={pos.Y}, Width={pos.Width}, Height={pos.Height}");
                }
                
                // Закрываем окно с результатом true
                _positionWindow.DialogResult = true;
                _positionWindow.Close();
            };
            
            // Обработчик отмены
            btnCancel.Click += (s, ev) =>
            {
                _positionWindow.DialogResult = false;
                _positionWindow.Close();
            };
            
            // Устанавливаем содержимое окна
            _positionWindow.Content = mainGrid;
            
            // Показываем окно
            bool? result = _positionWindow.ShowDialog();
            if (result == true)
            {
                MessageBox.Show("Позиции фотографий сохранены", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void AddPositionRect(double x, double y, double width, double height, int number)
        {
            // Создаем прямоугольник
            var rectangle = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
            };
            
            // Устанавливаем позицию
            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            
            // Добавляем на канву
            _positionCanvas.Children.Add(rectangle);
            
            // Создаем метку с номером позиции
            var numberLabel = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Устанавливаем позицию метки по центру прямоугольника
            Canvas.SetLeft(numberLabel, x + width / 2 - 15);
            Canvas.SetTop(numberLabel, y + height / 2 - 15);
            
            // Добавляем метку на канву
            _positionCanvas.Children.Add(numberLabel);
            
            // Создаем обертку для управления прямоугольником
            var positionRect = new PositionRect { 
                Rectangle = rectangle, 
                NumberLabel = numberLabel,
                Number = number,
                ResizeThumbs = new List<Thumb>() 
            };
            
            // Добавляем возможность перетаскивания
            rectangle.MouseLeftButtonDown += (s, e) =>
            {
                positionRect.IsDragging = true;
                positionRect.LastMousePosition = e.GetPosition(_positionCanvas);
                rectangle.CaptureMouse();
                e.Handled = true;
            };
            
            rectangle.MouseLeftButtonUp += (s, e) =>
            {
                positionRect.IsDragging = false;
                rectangle.ReleaseMouseCapture();
                e.Handled = true;
            };
            
            rectangle.MouseMove += (s, e) =>
            {
                if (positionRect.IsDragging)
                {
                    var currentPosition = e.GetPosition(_positionCanvas);
                    var deltaX = currentPosition.X - positionRect.LastMousePosition.X;
                    var deltaY = currentPosition.Y - positionRect.LastMousePosition.Y;
                    
                    var newLeft = Canvas.GetLeft(rectangle) + deltaX;
                    var newTop = Canvas.GetTop(rectangle) + deltaY;
                    
                    // Ограничиваем позицию границами канвы
                    newLeft = Math.Max(0, Math.Min(_positionCanvas.Width - rectangle.Width, newLeft));
                    newTop = Math.Max(0, Math.Min(_positionCanvas.Height - rectangle.Height, newTop));
                    
                    Canvas.SetLeft(rectangle, newLeft);
                    Canvas.SetTop(rectangle, newTop);
                    
                    // Обновляем позицию метки с номером
                    Canvas.SetLeft(numberLabel, newLeft + rectangle.Width / 2 - 15);
                    Canvas.SetTop(numberLabel, newTop + rectangle.Height / 2 - 15);
                    
                    positionRect.LastMousePosition = currentPosition;
                    
                    // Обновляем позиции маркеров изменения размера
                    positionRect.NotifyPositionChanged();
                }
            };
            
            // Добавляем маркеры для изменения размера
            AddResizeThumb(rectangle, numberLabel, positionRect, ResizeThumbPosition.TopLeft);
            AddResizeThumb(rectangle, numberLabel, positionRect, ResizeThumbPosition.TopRight);
            AddResizeThumb(rectangle, numberLabel, positionRect, ResizeThumbPosition.BottomLeft);
            AddResizeThumb(rectangle, numberLabel, positionRect, ResizeThumbPosition.BottomRight);
            
            // Добавляем в список
            _positionRects.Add(positionRect);
        }
        
        private void AddResizeThumb(Rectangle rectangle, TextBlock numberLabel, PositionRect positionRect, ResizeThumbPosition position)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
            
            // Размещаем маркер в зависимости от позиции
            Panel.SetZIndex(thumb, 100);
            _positionCanvas.Children.Add(thumb);
            
            // Добавляем маркер в список маркеров прямоугольника
            positionRect.ResizeThumbs.Add(thumb);
            
            // Обновляем позицию маркера при изменении размеров прямоугольника
            void UpdateThumbPosition()
            {
                double left = Canvas.GetLeft(rectangle);
                double top = Canvas.GetTop(rectangle);
                
                switch (position)
                {
                    case ResizeThumbPosition.TopLeft:
                        Canvas.SetLeft(thumb, left - 5);
                        Canvas.SetTop(thumb, top - 5);
                        break;
                    case ResizeThumbPosition.TopRight:
                        Canvas.SetLeft(thumb, left + rectangle.Width - 5);
                        Canvas.SetTop(thumb, top - 5);
                        break;
                    case ResizeThumbPosition.BottomLeft:
                        Canvas.SetLeft(thumb, left - 5);
                        Canvas.SetTop(thumb, top + rectangle.Height - 5);
                        break;
                    case ResizeThumbPosition.BottomRight:
                        Canvas.SetLeft(thumb, left + rectangle.Width - 5);
                        Canvas.SetTop(thumb, top + rectangle.Height - 5);
                        break;
                }
                
                // Обновляем позицию метки с номером
                Canvas.SetLeft(numberLabel, left + rectangle.Width / 2 - 15);
                Canvas.SetTop(numberLabel, top + rectangle.Height / 2 - 15);
            }
            
            // Инициализируем позицию
            UpdateThumbPosition();
            
            // Регистрируем обработчик события изменения позиции
            positionRect.PositionChanged += UpdateThumbPosition;
            
            // Обработчик для изменения размера
            thumb.DragDelta += (s, e) =>
            {
                double left = Canvas.GetLeft(rectangle);
                double top = Canvas.GetTop(rectangle);
                double width = rectangle.Width;
                double height = rectangle.Height;
                
                switch (position)
                {
                    case ResizeThumbPosition.TopLeft:
                        // Изменяем левый верхний угол
                        double newLeft = left + e.HorizontalChange;
                        double newTop = top + e.VerticalChange;
                        double newWidth = width - e.HorizontalChange;
                        double newHeight = height - e.VerticalChange;
                        
                        if (newWidth >= 50 && newLeft >= 0)
                        {
                            Canvas.SetLeft(rectangle, newLeft);
                            rectangle.Width = newWidth;
                        }
                        
                        if (newHeight >= 50 && newTop >= 0)
                        {
                            Canvas.SetTop(rectangle, newTop);
                            rectangle.Height = newHeight;
                        }
                        break;
                    
                    case ResizeThumbPosition.TopRight:
                        // Изменяем правый верхний угол
                        newTop = top + e.VerticalChange;
                        newWidth = width + e.HorizontalChange;
                        newHeight = height - e.VerticalChange;
                        
                        if (newWidth >= 50 && left + newWidth <= _positionCanvas.Width)
                        {
                            rectangle.Width = newWidth;
                        }
                        
                        if (newHeight >= 50 && newTop >= 0)
                        {
                            Canvas.SetTop(rectangle, newTop);
                            rectangle.Height = newHeight;
                        }
                        break;
                    
                    case ResizeThumbPosition.BottomLeft:
                        // Изменяем левый нижний угол
                        newLeft = left + e.HorizontalChange;
                        newWidth = width - e.HorizontalChange;
                        newHeight = height + e.VerticalChange;
                        
                        if (newWidth >= 50 && newLeft >= 0)
                        {
                            Canvas.SetLeft(rectangle, newLeft);
                            rectangle.Width = newWidth;
                        }
                        
                        if (newHeight >= 50 && top + newHeight <= _positionCanvas.Height)
                        {
                            rectangle.Height = newHeight;
                        }
                        break;
                    
                    case ResizeThumbPosition.BottomRight:
                        // Изменяем правый нижний угол
                        newWidth = width + e.HorizontalChange;
                        newHeight = height + e.VerticalChange;
                        
                        if (newWidth >= 50 && left + newWidth <= _positionCanvas.Width)
                        {
                            rectangle.Width = newWidth;
                        }
                        
                        if (newHeight >= 50 && top + newHeight <= _positionCanvas.Height)
                        {
                            rectangle.Height = newHeight;
                        }
                        break;
                }
                
                // Обновляем позицию маркера
                UpdateThumbPosition();
                
                // Уведомляем об изменении для обновления других маркеров
                positionRect.NotifyPositionChanged();
            };
            
            // Обновляем позицию маркера при изменении размера прямоугольника
            rectangle.SizeChanged += (s, e) => {
                UpdateThumbPosition();
                positionRect.NotifyPositionChanged();
            };
        }
        
        private void BtnClearFrameTemplate_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.FrameTemplatePath = null;
            AppSettings.PhotoPositions.Clear();
            MessageBox.Show("Рамка и позиции фотографий очищены", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void BtnLoadOverlay_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*",
                Title = "Выберите изображение оверлея"
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                // Сохраняем путь к выбранному файлу
                AppSettings.OverlayImagePath = openFileDialog.FileName;
                MessageBox.Show("Оверлей загружен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void BtnClearOverlay_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.OverlayImagePath = null;
            MessageBox.Show("Оверлей очищен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем настройки из интерфейса
                if (cbCameras.SelectedIndex >= 0 && cbCameras.SelectedIndex < 10)
                {
                    AppSettings.CameraIndex = cbCameras.SelectedIndex;
                }
                
                if (cbMicrophones.SelectedIndex >= 0 && cbMicrophones.SelectedIndex < NAudio.Wave.WaveIn.DeviceCount)
                {
                    AppSettings.MicrophoneIndex = cbMicrophones.SelectedIndex;
                }
                
                AppSettings.RotationMode = ((ComboBoxItem)cbRotation.SelectedItem).Content.ToString();
                AppSettings.MirrorMode = chkMirrorMode.IsChecked == true;
                
                AppSettings.PhotoCount = int.Parse(((ComboBoxItem)cbPhotoCount.SelectedItem).Content.ToString());
                AppSettings.PhotoCountdownTime = int.Parse(((ComboBoxItem)cbPhotoCountdownTime.SelectedItem).Content.ToString());
                
                AppSettings.RecordingDuration = int.Parse(((ComboBoxItem)cbRecordingDuration.SelectedItem).Content.ToString());
                AppSettings.VideoCountdownTime = int.Parse(((ComboBoxItem)cbVideoCountdownTime.SelectedItem).Content.ToString());
                
                // Сохраняем режим обработки фотографий
                AppSettings.PhotoProcessingMode = (ImageProcessingMode)cbPhotoProcessingMode.SelectedIndex;
                
                // Сохраняем настройки принтера
                if (cbPrinters.SelectedIndex >= 0 && cbPrinters.SelectedItem.ToString() != "Принтеры не найдены")
                {
                    AppSettings.PrinterName = cbPrinters.SelectedItem.ToString();
                }
                
                // Сохраняем размеры печати
                if (double.TryParse(txtPrintWidth.Text, out double printWidth))
                {
                    AppSettings.PrintWidth = printWidth;
                }
                
                if (double.TryParse(txtPrintHeight.Text, out double printHeight))
                {
                    AppSettings.PrintHeight = printHeight;
                }
                
                // Сохраняем режим обработки для печати
                AppSettings.PrintProcessingMode = (ImageProcessingMode)cbPrintProcessingMode.SelectedIndex;
                
                // Сохраняем путь к шаблону рамки
                AppSettings.FrameTemplatePath = txtFrameTemplatePath.Text;
                
                // Проверяем, были ли уже настроены позиции фотографий
                if (string.IsNullOrEmpty(AppSettings.FrameTemplatePath) || 
                    AppSettings.PhotoPositions == null || 
                    AppSettings.PhotoPositions.Count == 0)
                {
                    // Создаем позиции по умолчанию только если они не были настроены
                    InitializeDefaultPhotoPositions();
                }
                else if (AppSettings.PhotoCount != AppSettings.PhotoPositions.Count)
                {
                    // Если изменилось количество фотографий, но есть шаблон, пересоздаем позиции
                    System.Diagnostics.Debug.WriteLine($"Количество фотографий изменилось с {AppSettings.PhotoPositions.Count} на {AppSettings.PhotoCount}. Пересоздаем позиции.");
                    InitializeDefaultPhotoPositions();
                }
                
                // Логируем текущие позиции фотографий
                System.Diagnostics.Debug.WriteLine($"Текущие позиции фотографий ({AppSettings.PhotoPositions.Count}):");
                for (int i = 0; i < AppSettings.PhotoPositions.Count; i++)
                {
                    var pos = AppSettings.PhotoPositions[i];
                    System.Diagnostics.Debug.WriteLine($"Позиция {i+1}: X={pos.X}, Y={pos.Y}, Width={pos.Width}, Height={pos.Height}");
                }
                
                // Сохраняем настройки
                SaveSettings();
                
                // Закрываем окно
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении настроек: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Метод для инициализации позиций фотографий по умолчанию
        private void InitializeDefaultPhotoPositions()
        {
            try
            {
                int photoCount = AppSettings.PhotoCount;
                // Очищаем существующие позиции
                AppSettings.PhotoPositions.Clear();
                
                // Определяем размер для шаблона 1200x1800
                double frameWidth = 1200;
                double frameHeight = 1800;
                
                // Параметры отступов
                double margin = Math.Min(frameWidth, frameHeight) / 20;
                
                System.Diagnostics.Debug.WriteLine($"Создаем {photoCount} позиций по умолчанию");
                
                // Определяем размер и расположение областей в зависимости от количества фотографий
                for (int i = 0; i < photoCount; i++)
                {
                    double width, height, x, y;
                    
                    switch(photoCount)
                    {
                        case 1:
                            // Одна область на весь кадр с отступами
                            width = frameWidth - 2 * margin;
                            height = frameHeight - 2 * margin;
                            x = margin;
                            y = margin;
                            break;
                            
                        case 2:
                            // Две области вертикально
                            width = frameWidth - 2 * margin;
                            height = (frameHeight - 3 * margin) / 2;
                            x = margin;
                            y = margin + i * (height + margin);
                            break;
                            
                        case 3:
                            // Три области - одна сверху, две снизу
                            if (i == 0) {
                                // Верхняя область на всю ширину
                                width = frameWidth - 2 * margin;
                                height = (frameHeight - 3 * margin) / 2;
                                x = margin;
                                y = margin;
                            } else {
                                // Две нижние области
                                width = (frameWidth - 3 * margin) / 2;
                                height = (frameHeight - 3 * margin) / 2;
                                x = margin + (i - 1) * (width + margin);
                                y = 2 * margin + (frameHeight - 3 * margin) / 2;
                            }
                            break;
                            
                        case 4:
                        default:
                            // Четыре области в сетке 2x2
                            width = (frameWidth - 3 * margin) / 2;
                            height = (frameHeight - 3 * margin) / 2;
                            x = margin + (i % 2) * (width + margin);
                            y = margin + (i / 2) * (height + margin);
                            break;
                    }
                    
                    AppSettings.PhotoPositions.Add(new PhotoPosition
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"Созданы позиции по умолчанию ({AppSettings.PhotoPositions.Count}):");
                for (int i = 0; i < AppSettings.PhotoPositions.Count; i++)
                {
                    var pos = AppSettings.PhotoPositions[i];
                    System.Diagnostics.Debug.WriteLine($"Позиция {i+1}: X={pos.X}, Y={pos.Y}, Width={pos.Width}, Height={pos.Height}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при создании позиций по умолчанию: {ex.Message}");
                AppSettings.PhotoPositions = new List<PhotoPosition>();
            }
        }
        
        private void LoadSettings()
        {
            try
            {
                // Загружаем настройки через менеджер настроек
                AppSettings = SettingsManager.LoadSettings();
                System.Diagnostics.Debug.WriteLine($"Настройки загружены успешно. Путь: {SettingsManager.GetSettingsFilePath()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке настроек: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                AppSettings = new AppSettings();
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                // Сохраняем настройки через менеджер настроек
                bool success = SettingsManager.SaveSettings(AppSettings);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"Настройки сохранены успешно. Путь: {SettingsManager.GetSettingsFilePath()}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка при сохранении настроек через менеджер настроек");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении настроек: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void BtnTestPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, выбран ли принтер
                if (cbPrinters.SelectedIndex < 0 || cbPrinters.SelectedItem.ToString() == "Принтеры не найдены")
                {
                    MessageBox.Show("Пожалуйста, выберите принтер", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Создаем диалог печати
                System.Windows.Controls.PrintDialog printDialog = new System.Windows.Controls.PrintDialog();
                printDialog.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), cbPrinters.SelectedItem.ToString());
                
                // Создаем тестовое изображение
                System.Windows.Controls.Canvas canvas = new System.Windows.Controls.Canvas();
                canvas.Width = ConvertCmToPixels(double.Parse(txtPrintWidth.Text));
                canvas.Height = ConvertCmToPixels(double.Parse(txtPrintHeight.Text));
                canvas.Background = Brushes.White;
                
                // Добавляем текст
                TextBlock textBlock = new TextBlock();
                textBlock.Text = "Тестовая печать";
                textBlock.FontSize = 24;
                textBlock.Foreground = Brushes.Black;
                Canvas.SetLeft(textBlock, (canvas.Width - 200) / 2);
                Canvas.SetTop(textBlock, canvas.Height / 2 - 50);
                canvas.Children.Add(textBlock);
                
                // Добавляем информацию о размерах
                TextBlock sizeInfo = new TextBlock();
                sizeInfo.Text = $"Размер: {txtPrintWidth.Text} x {txtPrintHeight.Text} см";
                sizeInfo.FontSize = 16;
                sizeInfo.Foreground = Brushes.Black;
                Canvas.SetLeft(sizeInfo, (canvas.Width - 200) / 2);
                Canvas.SetTop(sizeInfo, canvas.Height / 2);
                canvas.Children.Add(sizeInfo);
                
                // Рисуем рамку
                Rectangle border = new Rectangle();
                border.Width = canvas.Width;
                border.Height = canvas.Height;
                border.Stroke = Brushes.Black;
                border.StrokeThickness = 2;
                canvas.Children.Add(border);
                
                // Рисуем линии по центру
                Line horizontalLine = new Line();
                horizontalLine.X1 = 0;
                horizontalLine.Y1 = canvas.Height / 2;
                horizontalLine.X2 = canvas.Width;
                horizontalLine.Y2 = canvas.Height / 2;
                horizontalLine.Stroke = Brushes.Black;
                horizontalLine.StrokeThickness = 1;
                horizontalLine.StrokeDashArray = new DoubleCollection { 5, 5 };
                canvas.Children.Add(horizontalLine);
                
                Line verticalLine = new Line();
                verticalLine.X1 = canvas.Width / 2;
                verticalLine.Y1 = 0;
                verticalLine.X2 = canvas.Width / 2;
                verticalLine.Y2 = canvas.Height;
                verticalLine.Stroke = Brushes.Black;
                verticalLine.StrokeThickness = 1;
                verticalLine.StrokeDashArray = new DoubleCollection { 5, 5 };
                canvas.Children.Add(verticalLine);
                
                // Отправляем на печать
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintVisual(canvas, "Тестовая печать");
                    MessageBox.Show("Тестовая страница отправлена на печать", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при тестовой печати: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private double ConvertCmToPixels(double cm)
        {
            // Разрешение экрана приблизительно 96 DPI
            const double INCH_TO_CM = 2.54;
            const double DPI = 96;
            return cm * DPI / INCH_TO_CM;
        }
    }
    
    // Класс настроек приложения
    public class AppSettings
    {
        // Настройки камеры
        public int CameraIndex { get; set; } = 0;
        public string RotationMode { get; set; } = "Без поворота";
        public bool MirrorMode { get; set; } = false;
        
        // Настройки фотобудки
        public int PhotoCount { get; set; } = 4;
        public int PhotoCountdownTime { get; set; } = 3;
        public string FrameTemplatePath { get; set; }
        public List<PhotoPosition> PhotoPositions { get; set; } = new List<PhotoPosition>();
        public ImageProcessingMode PhotoProcessingMode { get; set; } = ImageProcessingMode.Stretch;
        
        // Настройки видеобудки
        public int RecordingDuration { get; set; } = 15;
        public int VideoCountdownTime { get; set; } = 3;
        public int MicrophoneIndex { get; set; } = 0;
        public string OverlayImagePath { get; set; }
        
        // Настройки принтера
        public string PrinterName { get; set; }
        public double PrintWidth { get; set; } = 10.16;  // 4 дюйма = 10.16 см
        public double PrintHeight { get; set; } = 15.24; // 6 дюймов = 15.24 см
        public ImageProcessingMode PrintProcessingMode { get; set; } = ImageProcessingMode.Stretch;
    }
    
    // Класс для хранения позиции фотографии
    public class PhotoPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
    
    // Вспомогательный класс для управления прямоугольниками
    internal class PositionRect
    {
        public Rectangle Rectangle { get; set; }
        public bool IsDragging { get; set; }
        public System.Windows.Point LastMousePosition { get; set; }
        public List<Thumb> ResizeThumbs { get; set; } = new List<Thumb>();
        public event Action PositionChanged;
        public TextBlock NumberLabel { get; set; }
        public int Number { get; set; }
        
        public void NotifyPositionChanged()
        {
            PositionChanged?.Invoke();
        }
    }
} 