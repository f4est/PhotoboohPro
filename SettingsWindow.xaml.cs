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
using System.Windows.Shapes;
using System.IO;
using System.Xml.Serialization;
using System.Reflection;
using System.ComponentModel;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;

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

    // Типы текстовых элементов
    public enum TextElementType
    {
        DateTime,     // Дата и время
        EventName,    // Название события
        Custom        // Произвольный текст
    }

    // Класс для хранения информации о текстовом элементе
    public class TextElement
    {
        public bool Enabled { get; set; } = true;           // Включен ли элемент
        public double X { get; set; }                       // Позиция X
        public double Y { get; set; }                       // Позиция Y
        public double FontSize { get; set; } = 1.0;         // Размер шрифта
        public string Text { get; set; } = "";              // Текст (для Custom)
        public TextElementType Type { get; set; }           // Тип текстового элемента
        public string FontColor { get; set; } = "#000000";  // Цвет текста (черный по умолчанию)
        public string FontFamily { get; set; } = "Arial";   // Шрифт текста (Arial по умолчанию)
        public bool IsBold { get; set; } = false;           // Жирный текст
        public bool IsItalic { get; set; } = false;         // Курсив
        public bool IsUnderline { get; set; } = false;      // Подчеркивание
        
        // Конструктор по умолчанию
        public TextElement() { }
        
        // Конструктор с параметрами
        public TextElement(TextElementType type, double x, double y, double fontSize = 1.0, string fontFamily = "Arial")
        {
            Type = type;
            X = x;
            Y = y;
            FontSize = fontSize;
            FontFamily = fontFamily;
        }
    }

    // Класс для отображения текстовых маркеров на канве
    internal class TextMarker
    {
        public TextBlock TextBlock { get; set; }
        public Rectangle Background { get; set; }
        public bool IsDragging { get; set; }
        public System.Windows.Point LastMousePosition { get; set; }
        public int Index { get; set; }
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
            
            // Загружаем настройки при старте, если они есть
            LoadSettings();
            
            // Инициализируем UI элементы на основе настроек
            InitializeUIFromSettings();
            
            // Подписываемся на события для QR-кода
            InitializeQrCodeEvents();
            
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
            
            // Инициализация элементов настройки QR-кода
            sliderQrSize.Value = AppSettings.QrCodeSize;
            txtQrSize.Text = AppSettings.QrCodeSize.ToString();
            
            rectQrBackColor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.QrBackgroundColor));
            rectQrForeColor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.QrForegroundColor));
            
            txtQrLogoPath.Text = AppSettings.QrLogoPath;
            
            sliderQrLogoSize.Value = AppSettings.QrLogoSize;
            txtQrLogoSize.Text = $"{AppSettings.QrLogoSize}%";
            
            // Обновляем предпросмотр QR-кода
            UpdateQrPreview();
        }
        
        private void InitializeQrCodeEvents()
        {
            // События для размера QR-кода
            sliderQrSize.ValueChanged += (s, e) => 
            {
                int size = (int)sliderQrSize.Value;
                txtQrSize.Text = size.ToString();
                UpdateQrPreview();
            };
            
            // События для выбора цвета фона
            btnQrBackColor.Click += BtnQrBackColor_Click;
            
            // События для выбора цвета QR-кода
            btnQrForeColor.Click += BtnQrForeColor_Click;
            
            // События для логотипа
            btnLoadQrLogo.Click += BtnLoadQrLogo_Click;
            btnClearQrLogo.Click += BtnClearQrLogo_Click;
            
            // События для размера логотипа
            sliderQrLogoSize.ValueChanged += (s, e) => 
            {
                int size = (int)sliderQrLogoSize.Value;
                txtQrLogoSize.Text = $"{size}%";
                UpdateQrPreview();
            };
        }
        
        private void BtnQrBackColor_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog();
            colorDialog.Color = System.Drawing.ColorTranslator.FromHtml(AppSettings.QrBackgroundColor);
            
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Преобразуем цвет в строку формата #RRGGBB
                string colorHex = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                AppSettings.QrBackgroundColor = colorHex;
                
                // Обновляем интерфейс
                rectQrBackColor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                UpdateQrPreview();
            }
        }
        
        private void BtnQrForeColor_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog();
            colorDialog.Color = System.Drawing.ColorTranslator.FromHtml(AppSettings.QrForegroundColor);
            
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Преобразуем цвет в строку формата #RRGGBB
                string colorHex = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                AppSettings.QrForegroundColor = colorHex;
                
                // Обновляем интерфейс
                rectQrForeColor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                UpdateQrPreview();
            }
        }
        
        private void BtnLoadQrLogo_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
            dlg.Title = "Выберите логотип для QR-кода";
            
            if (dlg.ShowDialog() == true)
            {
                AppSettings.QrLogoPath = dlg.FileName;
                txtQrLogoPath.Text = dlg.FileName;
                UpdateQrPreview();
            }
        }
        
        private void BtnClearQrLogo_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.QrLogoPath = "";
            txtQrLogoPath.Text = "";
            UpdateQrPreview();
        }
        
        private void UpdateQrPreview()
        {
            try
            {
                // Создаем временный QR-код для предпросмотра
                string previewText = "https://example.com/preview";
                int size = (int)sliderQrSize.Value;
                int logoSize = (int)sliderQrLogoSize.Value;
                
                // Получаем QR-код с нашими настройками
                var qr = GenerateCustomQrCode(
                    previewText, 
                    size,
                    AppSettings.QrForegroundColor, 
                    AppSettings.QrBackgroundColor, 
                    AppSettings.QrLogoPath, 
                    logoSize);
                
                // Преобразуем для отображения
                BitmapImage bitmapImage = new BitmapImage();
                using (MemoryStream ms = new MemoryStream())
                {
                    qr.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                }
                
                imgQrPreview.Source = bitmapImage;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании предпросмотра QR-кода: {ex.Message}", "Ошибка", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private System.Drawing.Bitmap GenerateCustomQrCode(string content, int size, string foreColor, string backColor, string logoPath, int logoSizePercent)
        {
            // Создаем генератор QR-кода
            QRCoder.QRCodeGenerator qrGenerator = new QRCoder.QRCodeGenerator();
            QRCoder.QRCodeData qrCodeData = qrGenerator.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.H);
            QRCoder.QRCode qrCode = new QRCoder.QRCode(qrCodeData);
            
            // Преобразуем цвета из строк в объекты цветов
            System.Drawing.Color qrForeColor = System.Drawing.ColorTranslator.FromHtml(foreColor);
            System.Drawing.Color qrBackColor = System.Drawing.ColorTranslator.FromHtml(backColor);
            
            // Создаем QR-код с нашими цветами
            System.Drawing.Bitmap qrBitmap = qrCode.GetGraphic(20, qrForeColor, qrBackColor, false);
            
            // Изменяем размер QR-кода до нужного
            System.Drawing.Bitmap resizedQrBitmap = new System.Drawing.Bitmap(size, size);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(resizedQrBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(qrBitmap, new System.Drawing.Rectangle(0, 0, size, size));
            }
            
            // Если указан путь к логотипу, добавляем его в центр QR-кода
            if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
            {
                try
                {
                    // Загружаем логотип
                    System.Drawing.Bitmap logo = new System.Drawing.Bitmap(logoPath);
                    
                    // Определяем размер логотипа в процентах от размера QR-кода
                    int logoWidth = (int)(size * logoSizePercent / 100.0);
                    int logoHeight = (int)(size * logoSizePercent / 100.0);
                    
                    // Масштабируем логотип до нужного размера
                    System.Drawing.Bitmap resizedLogo = new System.Drawing.Bitmap(logo, new System.Drawing.Size(logoWidth, logoHeight));
                    
                    // Рассчитываем позицию для вставки логотипа в центр QR-кода
                    int logoX = (size - logoWidth) / 2;
                    int logoY = (size - logoHeight) / 2;
                    
                    // Вставляем логотип в QR-код
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(resizedQrBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(resizedLogo, new System.Drawing.Rectangle(logoX, logoY, logoWidth, logoHeight));
                    }
                    
                    // Освобождаем ресурсы
                    logo.Dispose();
                    resizedLogo.Dispose();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при добавлении логотипа: {ex.Message}", "Ошибка", 
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            return resizedQrBitmap;
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
            
            // Создаем новое окно с возможностью изменения размера
            _positionWindow = new Window
            {
                Title = "Настройка позиций",
                Width = Math.Min(1400, SystemParameters.WorkArea.Width * 0.9),
                Height = Math.Min(900, SystemParameters.WorkArea.Height * 0.9),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                MinWidth = 800,
                MinHeight = 600,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 245))
            };
            
            // Создаем Grid для размещения элементов
            Grid mainGrid = new Grid();
            
            // Определяем строки для разделения окна на секции
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) }); // Верхняя часть для изображения
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Нижняя часть для настроек
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Строка для кнопок
            
            // Создаем канву для отображения шаблона и областей
            Canvas canvas = new Canvas
            {
                Width = _frameTemplateImage.PixelWidth,
                Height = _frameTemplateImage.PixelHeight,
                Background = new ImageBrush(_frameTemplateImage)
            };
            
            // Создаем скроллвьювер для канвы
            ScrollViewer scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = canvas,
                Margin = new Thickness(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };
            
            mainGrid.Children.Add(scrollViewer);
            _positionCanvas = canvas;
            
            // Добавляем вкладки для разделения настроек позиций и текстовых надписей
            TabControl setupTabControl = new TabControl();
            Grid.SetRow(setupTabControl, 1);
            setupTabControl.Margin = new Thickness(10, 5, 10, 5);
            setupTabControl.BorderThickness = new Thickness(1);
            setupTabControl.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 220));
            mainGrid.Children.Add(setupTabControl);
            
            // Создаем стиль для TabItem
            Style tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(15, 8, 15, 8)));
            tabItemStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(245, 245, 250))));
            tabItemStyle.Setters.Add(new Setter(TabItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 80))));
            tabItemStyle.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeights.SemiBold));
            
            // Вкладка настроек позиций фотографий
            TabItem photoPositionsTab = new TabItem 
            { 
                Header = "Позиции фотографий", 
                Style = tabItemStyle
            };
            setupTabControl.Items.Add(photoPositionsTab);
            
            // Вкладка настроек текстовых надписей
            TabItem textElementsTab = new TabItem 
            { 
                Header = "Текстовые надписи", 
                Style = tabItemStyle
            };
            setupTabControl.Items.Add(textElementsTab);
            
            // Создаем панель настроек текстовых элементов с возможностью прокрутки
            // Создаем Grid для текстовой панели, чтобы контролировать высоту
            Grid textPanelGrid = new Grid();
            textPanelGrid.MaxHeight = 350; // Ограничиваем высоту, чтобы не перекрывать картинку
            
            ScrollViewer textScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(5)
            };
            
            Border textSettingsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(15, 10, 15, 10)
            };
            
            StackPanel textSettingsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            
            textSettingsBorder.Child = textSettingsPanel;
            textScrollViewer.Content = textSettingsBorder;
            textPanelGrid.Children.Add(textScrollViewer);
            textElementsTab.Content = textPanelGrid;
            
            // Заголовок раздела
            TextBlock headerTextBlock = new TextBlock
            {
                Text = "Управление текстовыми надписями",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 80))
            };
            textSettingsPanel.Children.Add(headerTextBlock);
            
            // 1. Настройки даты и времени
            Border dateTimeBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 255))
            };
            
            Grid dateTimeGrid = new Grid();
            dateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            dateTimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dateTimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Заголовок и чекбокс
            TextBlock dateTimeHeader = new TextBlock
            {
                Text = "Дата и время:",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetRow(dateTimeHeader, 0);
            Grid.SetColumn(dateTimeHeader, 0);
            dateTimeGrid.Children.Add(dateTimeHeader);
            
            CheckBox dateTimeCheckBox = new CheckBox
            {
                Content = "Включить",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetRow(dateTimeCheckBox, 0);
            Grid.SetColumn(dateTimeCheckBox, 1);
            dateTimeGrid.Children.Add(dateTimeCheckBox);
            
            // Размер шрифта
            TextBlock fontSizeLabel = new TextBlock
            {
                Text = "Размер:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 5, 5, 5)
            };
            Grid.SetRow(fontSizeLabel, 0);
            Grid.SetColumn(fontSizeLabel, 2);
            dateTimeGrid.Children.Add(fontSizeLabel);
            
            ComboBox fontSizeCombo = new ComboBox
            {
                Width = 60,
                Margin = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            for (double size = 0.5; size <= 3.0; size += 0.25)
            {
                fontSizeCombo.Items.Add(size.ToString("0.00"));
            }
            fontSizeCombo.SelectedIndex = 2; // 1.0 по умолчанию
            Grid.SetRow(fontSizeCombo, 0);
            Grid.SetColumn(fontSizeCombo, 3);
            dateTimeGrid.Children.Add(fontSizeCombo);
            
            // Выбор шрифта
            TextBlock fontFamilyLabel = new TextBlock
            {
                Text = "Шрифт:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 5, 5, 5)
            };
            Grid.SetRow(fontFamilyLabel, 0);
            Grid.SetColumn(fontFamilyLabel, 4);
            dateTimeGrid.Children.Add(fontFamilyLabel);
            
            ComboBox fontFamilyCombo = new ComboBox
            {
                Width = 120,
                Margin = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Добавляем популярные шрифты
            var fontFamilies = new[] { "Arial", "Times New Roman", "Calibri", "Segoe UI", "Comic Sans MS", "Tahoma", "Verdana" };
            foreach (var font in fontFamilies)
            {
                fontFamilyCombo.Items.Add(font);
            }
            fontFamilyCombo.SelectedIndex = 0; // Arial по умолчанию
            Grid.SetRow(fontFamilyCombo, 0);
            Grid.SetColumn(fontFamilyCombo, 5);
            dateTimeGrid.Children.Add(fontFamilyCombo);
            
            // Пример отображения
            TextBlock previewLabel = new TextBlock
            {
                Text = "Пример: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                Margin = new Thickness(5),
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
            Grid.SetRow(previewLabel, 1);
            Grid.SetColumnSpan(previewLabel, 5);
            dateTimeGrid.Children.Add(previewLabel);
            
            dateTimeBorder.Child = dateTimeGrid;
            textSettingsPanel.Children.Add(dateTimeBorder);
            
            // 2. Настройки названия события
            Border eventNameBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 255))
            };
            
            Grid eventNameGrid = new Grid();
            eventNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            eventNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            eventNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            eventNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            eventNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            eventNameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            eventNameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            eventNameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Заголовок и чекбокс
            TextBlock eventNameHeader = new TextBlock
            {
                Text = "Название события:",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetRow(eventNameHeader, 0);
            Grid.SetColumn(eventNameHeader, 0);
            eventNameGrid.Children.Add(eventNameHeader);
            
            CheckBox eventNameCheckBox = new CheckBox
            {
                Content = "Включить",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetRow(eventNameCheckBox, 0);
            Grid.SetColumn(eventNameCheckBox, 1);
            eventNameGrid.Children.Add(eventNameCheckBox);
            
            // Размер шрифта
            TextBlock eventFontSizeLabel = new TextBlock
            {
                Text = "Размер:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 5, 5, 5)
            };
            Grid.SetRow(eventFontSizeLabel, 0);
            Grid.SetColumn(eventFontSizeLabel, 2);
            eventNameGrid.Children.Add(eventFontSizeLabel);
            
            ComboBox eventFontSizeCombo = new ComboBox
            {
                Width = 60,
                Margin = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            for (double size = 0.5; size <= 3.0; size += 0.25)
            {
                eventFontSizeCombo.Items.Add(size.ToString("0.00"));
            }
            eventFontSizeCombo.SelectedIndex = 2; // 1.0 по умолчанию
            Grid.SetRow(eventFontSizeCombo, 0);
            Grid.SetColumn(eventFontSizeCombo, 3);
            eventNameGrid.Children.Add(eventFontSizeCombo);
            
            // Выбор шрифта
            TextBlock eventFontFamilyLabel = new TextBlock
            {
                Text = "Шрифт:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 5, 5, 5)
            };
            Grid.SetRow(eventFontFamilyLabel, 0);
            Grid.SetColumn(eventFontFamilyLabel, 4);
            eventNameGrid.Children.Add(eventFontFamilyLabel);
            
            ComboBox eventFontFamilyCombo = new ComboBox
            {
                Width = 120,
                Margin = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Добавляем популярные шрифты
            foreach (var font in fontFamilies)
            {
                eventFontFamilyCombo.Items.Add(font);
            }
            eventFontFamilyCombo.SelectedIndex = 0; // Arial по умолчанию
            Grid.SetRow(eventFontFamilyCombo, 0);
            Grid.SetColumn(eventFontFamilyCombo, 5);
            eventNameGrid.Children.Add(eventFontFamilyCombo);
            
            // Ввод текста события
            TextBlock eventTextLabel = new TextBlock
            {
                Text = "Текст события:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetRow(eventTextLabel, 1);
            Grid.SetColumn(eventTextLabel, 0);
            eventNameGrid.Children.Add(eventTextLabel);
            
            TextBox eventTextBox = new TextBox
            {
                Margin = new Thickness(5),
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(eventTextBox, 1);
            Grid.SetColumn(eventTextBox, 1);
            Grid.SetColumnSpan(eventTextBox, 4);
            eventNameGrid.Children.Add(eventTextBox);
            
            // Пример отображения
            TextBlock eventPreviewLabel = new TextBlock
            {
                Text = "Пример: Название события будет отображаться в верхней части фотографии",
                Margin = new Thickness(5),
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
            Grid.SetRow(eventPreviewLabel, 2);
            Grid.SetColumnSpan(eventPreviewLabel, 5);
            eventNameGrid.Children.Add(eventPreviewLabel);
            
            eventNameBorder.Child = eventNameGrid;
            textSettingsPanel.Children.Add(eventNameBorder);
            
            // 3. Пользовательские тексты
            Border customTextsBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 255))
            };
            
            StackPanel customTextsPanel = new StackPanel();
            
            TextBlock customTextsHeader = new TextBlock
            {
                Text = "Произвольные тексты",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            customTextsPanel.Children.Add(customTextsHeader);
            
            // Список существующих текстов
            TextBlock existingTextsLabel = new TextBlock
            {
                Text = "Существующие тексты:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            customTextsPanel.Children.Add(existingTextsLabel);
            
            ListView customTextsList = new ListView
            {
                Height = 100,
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            customTextsPanel.Children.Add(customTextsList);
            
            // Кнопки управления текстами
            StackPanel customTextButtonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            Button addTextButton = new Button
            {
                Content = "Добавить текст",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White
            };
            customTextButtonsPanel.Children.Add(addTextButton);
            
            Button editTextButton = new Button
            {
                Content = "Редактировать",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 10, 0)
            };
            customTextButtonsPanel.Children.Add(editTextButton);
            
            Button removeTextButton = new Button
            {
                Content = "Удалить",
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromRgb(239, 83, 80)),
                Foreground = Brushes.White
            };
            customTextButtonsPanel.Children.Add(removeTextButton);
            
            customTextsPanel.Children.Add(customTextButtonsPanel);
            
            // Панель добавления/редактирования текста
            Border editTextBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 255)),
                Visibility = Visibility.Collapsed // Скрыта по умолчанию
            };
            
            // Отображаем визуальный маркер позиции текста на канве
            List<TextMarker> textMarkers = new List<TextMarker>();
            
            Grid editTextGrid = new Grid();
            editTextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            editTextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editTextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            editTextGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editTextGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editTextGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Текст
            TextBlock customTextLabel = new TextBlock
            {
                Text = "Текст:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 10, 5)
            };
            Grid.SetRow(customTextLabel, 0);
            Grid.SetColumn(customTextLabel, 0);
            editTextGrid.Children.Add(customTextLabel);
            
            TextBox customTextBox = new TextBox
            {
                Margin = new Thickness(0, 5, 0, 5),
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(customTextBox, 0);
            Grid.SetColumn(customTextBox, 1);
            Grid.SetColumnSpan(customTextBox, 2);
            editTextGrid.Children.Add(customTextBox);
            
            // Размер шрифта
            TextBlock customFontSizeLabel = new TextBlock
            {
                Text = "Размер:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 10, 5)
            };
            Grid.SetRow(customFontSizeLabel, 1);
            Grid.SetColumn(customFontSizeLabel, 0);
            editTextGrid.Children.Add(customFontSizeLabel);
            
            ComboBox customFontSizeCombo = new ComboBox
            {
                Width = 80,
                Margin = new Thickness(0, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            for (double size = 0.5; size <= 3.0; size += 0.25)
            {
                customFontSizeCombo.Items.Add(size.ToString("0.00"));
            }
            customFontSizeCombo.SelectedIndex = 2; // 1.0 по умолчанию
            Grid.SetRow(customFontSizeCombo, 1);
            Grid.SetColumn(customFontSizeCombo, 1);
            editTextGrid.Children.Add(customFontSizeCombo);
            
            // Выбор шрифта
            TextBlock customFontFamilyLabel = new TextBlock
            {
                Text = "Шрифт:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 10, 5)
            };
            Grid.SetRow(customFontFamilyLabel, 2);
            Grid.SetColumn(customFontFamilyLabel, 0);
            editTextGrid.Children.Add(customFontFamilyLabel);
            
            ComboBox customFontFamilyCombo = new ComboBox
            {
                Width = 120,
                Margin = new Thickness(0, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Добавляем популярные шрифты
            foreach (var font in fontFamilies)
            {
                customFontFamilyCombo.Items.Add(font);
            }
            customFontFamilyCombo.SelectedIndex = 0; // Arial по умолчанию
            Grid.SetRow(customFontFamilyCombo, 2);
            Grid.SetColumn(customFontFamilyCombo, 1);
            editTextGrid.Children.Add(customFontFamilyCombo);
            
            // Стили шрифта (жирный, курсив, подчеркнутый)
            StackPanel stylePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            
            // Жирный
            CheckBox boldCheckBox = new CheckBox
            {
                Content = "Жирный",
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stylePanel.Children.Add(boldCheckBox);
            
            // Курсив
            CheckBox italicCheckBox = new CheckBox
            {
                Content = "Курсив",
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stylePanel.Children.Add(italicCheckBox);
            
            // Подчеркивание
            CheckBox underlineCheckBox = new CheckBox
            {
                Content = "Подчеркнутый",
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stylePanel.Children.Add(underlineCheckBox);
            
            Grid.SetRow(stylePanel, 3);
            Grid.SetColumn(stylePanel, 0);
            Grid.SetColumnSpan(stylePanel, 3);
            editTextGrid.Children.Add(stylePanel);
            
            // Кнопка сохранения текста
            Button saveTextButton = new Button
            {
                Content = "Сохранить",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(66, 135, 245)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(saveTextButton, 4);
            Grid.SetColumn(saveTextButton, 1);
            Grid.SetColumnSpan(saveTextButton, 2);
            editTextGrid.Children.Add(saveTextButton);
            
            editTextBorder.Child = editTextGrid;
            customTextsPanel.Children.Add(editTextBorder);
            
            customTextsBorder.Child = customTextsPanel;
            textSettingsPanel.Children.Add(customTextsBorder);
            
            // Подключение обработчиков событий
            // 1. Для даты и времени
            var dateTimeElement = AppSettings.TextElements.FirstOrDefault(e => e.Type == TextElementType.DateTime);
            if (dateTimeElement != null)
            {
                dateTimeCheckBox.IsChecked = dateTimeElement.Enabled;
                int fontSizeIndex = fontSizeCombo.Items.IndexOf(dateTimeElement.FontSize.ToString("0.00"));
                if (fontSizeIndex >= 0)
                    fontSizeCombo.SelectedIndex = fontSizeIndex;
            }
            
            dateTimeCheckBox.Checked += (s, e) => {
                if (dateTimeElement != null)
                    dateTimeElement.Enabled = true;
            };
            
            dateTimeCheckBox.Unchecked += (s, e) => {
                if (dateTimeElement != null)
                    dateTimeElement.Enabled = false;
            };
            
            fontSizeCombo.SelectionChanged += (s, e) => {
                if (dateTimeElement != null && fontSizeCombo.SelectedItem != null)
                {
                    if (double.TryParse(fontSizeCombo.SelectedItem.ToString(), out double fontSize))
                        dateTimeElement.FontSize = fontSize;
                }
            };
            
            // Обработчик изменения шрифта
            fontFamilyCombo.SelectionChanged += (s, e) => {
                if (dateTimeElement != null && fontFamilyCombo.SelectedItem != null)
                {
                    dateTimeElement.FontFamily = fontFamilyCombo.SelectedItem.ToString();
                }
            };
            
            // 2. Для названия события
            var eventNameElement = AppSettings.TextElements.FirstOrDefault(e => e.Type == TextElementType.EventName);
            if (eventNameElement != null)
            {
                eventNameCheckBox.IsChecked = eventNameElement.Enabled;
                int fontSizeIndex = eventFontSizeCombo.Items.IndexOf(eventNameElement.FontSize.ToString("0.00"));
                if (fontSizeIndex >= 0)
                    eventFontSizeCombo.SelectedIndex = fontSizeIndex;
                
                eventTextBox.Text = eventNameElement.Text;
            }
            
            eventNameCheckBox.Checked += (s, e) => {
                if (eventNameElement != null)
                    eventNameElement.Enabled = true;
            };
            
            eventNameCheckBox.Unchecked += (s, e) => {
                if (eventNameElement != null)
                    eventNameElement.Enabled = false;
            };
            
            eventFontSizeCombo.SelectionChanged += (s, e) => {
                if (eventNameElement != null && eventFontSizeCombo.SelectedItem != null)
                {
                    if (double.TryParse(eventFontSizeCombo.SelectedItem.ToString(), out double fontSize))
                        eventNameElement.FontSize = fontSize;
                }
            };
            
            // Обработчик изменения шрифта
            eventFontFamilyCombo.SelectionChanged += (s, e) => {
                if (eventNameElement != null && eventFontFamilyCombo.SelectedItem != null)
                {
                    eventNameElement.FontFamily = eventFontFamilyCombo.SelectedItem.ToString();
                }
            };
            
            eventTextBox.TextChanged += (s, e) => {
                if (eventNameElement != null)
                    eventNameElement.Text = eventTextBox.Text;
            };
            
            // 3. Для пользовательских текстов
            var customElements = AppSettings.TextElements.Where(e => e.Type == TextElementType.Custom).ToList();
            foreach (var customElement in customElements)
            {
                customTextsList.Items.Add(customElement.Text);
            }
            
            // Добавление нового текста
            addTextButton.Click += (s, e) => {
                editTextBorder.Visibility = Visibility.Visible;
                customTextBox.Text = "";
                customFontSizeCombo.SelectedIndex = 2; // 1.0 по умолчанию
                customTextBox.Tag = null; // Новый текст
            };
            
            // Редактирование выбранного текста
            editTextButton.Click += (s, e) => {
                if (customTextsList.SelectedIndex >= 0)
                {
                    var selectedIndex = customTextsList.SelectedIndex;
                    var customTexts = AppSettings.TextElements.Where(el => el.Type == TextElementType.Custom).ToList();
                    
                    if (selectedIndex < customTexts.Count)
                    {
                        var selectedElement = customTexts[selectedIndex];
                        
                        // Загружаем текст
                        customTextBox.Text = selectedElement.Text;
                        
                        // Загружаем размер шрифта
                        int fontSizeIndex = customFontSizeCombo.Items.IndexOf(selectedElement.FontSize.ToString("0.00"));
                        if (fontSizeIndex >= 0)
                            customFontSizeCombo.SelectedIndex = fontSizeIndex;
                        
                        // Загружаем шрифт
                        int fontFamilyIndex = customFontFamilyCombo.Items.IndexOf(selectedElement.FontFamily);
                        if (fontFamilyIndex >= 0)
                            customFontFamilyCombo.SelectedIndex = fontFamilyIndex;
                        
                        // Загружаем стили текста
                        boldCheckBox.IsChecked = selectedElement.IsBold;
                        italicCheckBox.IsChecked = selectedElement.IsItalic;
                        underlineCheckBox.IsChecked = selectedElement.IsUnderline;
                        
                        customTextBox.Tag = selectedIndex; // Сохраняем индекс для редактирования
                        editTextBorder.Visibility = Visibility.Visible;
                    }
                }
            };
            
            // Удаление текста
            removeTextButton.Click += (s, e) => {
                if (customTextsList.SelectedIndex >= 0)
                {
                    var selectedIndex = customTextsList.SelectedIndex;
                    var customTexts = AppSettings.TextElements.Where(el => el.Type == TextElementType.Custom).ToList();
                    
                    if (selectedIndex < customTexts.Count)
                    {
                        var selectedElement = customTexts[selectedIndex];
                        AppSettings.TextElements.Remove(selectedElement);
                        customTextsList.Items.RemoveAt(selectedIndex);
                    }
                }
            };
            
            // Сохранение текста
            saveTextButton.Click += (s, e) => {
                if (string.IsNullOrWhiteSpace(customTextBox.Text))
                    return;
                
                double fontSize = 1.0;
                if (customFontSizeCombo.SelectedItem != null)
                {
                    if (!double.TryParse(customFontSizeCombo.SelectedItem.ToString(), out fontSize))
                        fontSize = 1.0;
                }
                
                // Получаем выбранный шрифт
                string fontFamily = "Arial";
                if (customFontFamilyCombo.SelectedItem != null)
                {
                    fontFamily = customFontFamilyCombo.SelectedItem.ToString();
                }
                
                if (customTextBox.Tag == null) // Новый текст
                {
                    var newElement = new TextElement
                    {
                        Type = TextElementType.Custom,
                        Text = customTextBox.Text,
                        FontSize = fontSize,
                        FontFamily = fontFamily,
                        X = 20,
                        Y = 100,
                        Enabled = true,
                        IsBold = boldCheckBox.IsChecked == true,
                        IsItalic = italicCheckBox.IsChecked == true,
                        IsUnderline = underlineCheckBox.IsChecked == true
                    };
                    
                    AppSettings.TextElements.Add(newElement);
                    customTextsList.Items.Add(newElement.Text);
                    
                    // Добавляем маркер текста на канву
                    AddTextMarker(newElement, canvas, textMarkers);
                }
                else // Редактирование существующего
                {
                    int editIndex = (int)customTextBox.Tag;
                    var customTexts = AppSettings.TextElements.Where(el => el.Type == TextElementType.Custom).ToList();
                    
                    if (editIndex < customTexts.Count)
                    {
                        // Находим индекс элемента в полном списке текстовых элементов
                        int actualElementIndex = AppSettings.TextElements.IndexOf(customTexts[editIndex]);
                        
                        if (actualElementIndex >= 0)
                        {
                            var element = AppSettings.TextElements[actualElementIndex];
                            element.Text = customTextBox.Text;
                            element.FontSize = fontSize;
                            element.FontFamily = fontFamily;
                            element.IsBold = boldCheckBox.IsChecked == true;
                            element.IsItalic = italicCheckBox.IsChecked == true;
                            element.IsUnderline = underlineCheckBox.IsChecked == true;
                            
                            customTextsList.Items[editIndex] = element.Text;
                            
                            // Обновляем текст в существующем маркере
                            // Ищем соответствующий маркер по индексу
                            var markerToUpdate = textMarkers.FirstOrDefault(m => m.Index == actualElementIndex);
                            if (markerToUpdate != null && markerToUpdate.TextBlock != null)
                            {
                                // Удаляем старый маркер
                                canvas.Children.Remove(markerToUpdate.TextBlock);
                                canvas.Children.Remove(markerToUpdate.Background);
                                textMarkers.Remove(markerToUpdate);
                                
                                // Добавляем новый маркер с обновленными настройками
                                AddTextMarker(element, canvas, textMarkers);
                            }
                        }
                    }
                }
                
                // Сохраняем настройки сразу
                SettingsManager.SaveSettings(AppSettings);
                
                editTextBorder.Visibility = Visibility.Collapsed;
            };
            
            // Метод для обновления текстовых маркеров при изменении настроек
            Action updateTextMarkers = () => {
                // Очищаем все маркеры
                foreach (var marker in textMarkers)
                {
                    if (marker.TextBlock != null)
                        canvas.Children.Remove(marker.TextBlock);
                    if (marker.Background != null)
                        canvas.Children.Remove(marker.Background);
                }
                textMarkers.Clear();
                
                // Перерисовываем все текстовые элементы
                foreach (var textElem in AppSettings.TextElements)
                {
                    AddTextMarker(textElem, canvas, textMarkers);
                }
                
                // Сохраняем настройки сразу
                SettingsManager.SaveSettings(AppSettings);
            };
            
            // Подключаем обновление текстовых маркеров к изменению основных настроек
            dateTimeCheckBox.Checked += (s, e) => {
                if (dateTimeElement != null)
                {
                    dateTimeElement.Enabled = true;
                    updateTextMarkers();
                }
            };
            
            dateTimeCheckBox.Unchecked += (s, e) => {
                if (dateTimeElement != null)
                {
                    dateTimeElement.Enabled = false;
                    updateTextMarkers();
                }
            };
            
            fontSizeCombo.SelectionChanged += (s, e) => {
                if (dateTimeElement != null && fontSizeCombo.SelectedItem != null)
                {
                    if (double.TryParse(fontSizeCombo.SelectedItem.ToString(), out double fontSize))
                    {
                        dateTimeElement.FontSize = fontSize;
                        updateTextMarkers();
                    }
                }
            };
            
            // Обработчик изменения шрифта
            fontFamilyCombo.SelectionChanged += (s, e) => {
                if (dateTimeElement != null && fontFamilyCombo.SelectedItem != null)
                {
                    dateTimeElement.FontFamily = fontFamilyCombo.SelectedItem.ToString();
                    updateTextMarkers();
                }
            };
            
            // Обновляем обработчики для названия события
            eventNameCheckBox.Checked += (s, e) => {
                if (eventNameElement != null)
                {
                    eventNameElement.Enabled = true;
                    updateTextMarkers();
                }
            };
            
            eventNameCheckBox.Unchecked += (s, e) => {
                if (eventNameElement != null)
                {
                    eventNameElement.Enabled = false;
                    updateTextMarkers();
                }
            };
            
            eventFontSizeCombo.SelectionChanged += (s, e) => {
                if (eventNameElement != null && eventFontSizeCombo.SelectedItem != null)
                {
                    if (double.TryParse(eventFontSizeCombo.SelectedItem.ToString(), out double fontSize))
                    {
                        eventNameElement.FontSize = fontSize;
                        updateTextMarkers();
                    }
                }
            };
            
            // Обработчик изменения шрифта
            eventFontFamilyCombo.SelectionChanged += (s, e) => {
                if (eventNameElement != null && eventFontFamilyCombo.SelectedItem != null)
                {
                    eventNameElement.FontFamily = eventFontFamilyCombo.SelectedItem.ToString();
                    updateTextMarkers();
                }
            };
            
            eventTextBox.TextChanged += (s, e) => {
                if (eventNameElement != null)
                {
                    eventNameElement.Text = eventTextBox.Text;
                    updateTextMarkers();
                }
            };
            
            // Загружаем существующие текстовые элементы
            if (AppSettings.TextElements != null && AppSettings.TextElements.Count > 0)
            {
                foreach (var textElem in AppSettings.TextElements)
                {
                    // Добавляем маркер текста на канву
                    AddTextMarker(textElem, canvas, textMarkers);
                }
            }
            
            // Создаем панель для настроек позиций фотографий
            Border positionSettingsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(5)
            };
            
            StackPanel positionSettingsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            positionSettingsBorder.Child = positionSettingsPanel;
            photoPositionsTab.Content = positionSettingsBorder;
            
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
            txtFrameTemplatePath.Text = "";
            
            // Очищаем элементы на UI
            if (_frameTemplateImage != null)
            {
                _frameTemplateImage = null;
            }
            
            // Применяем настройки сразу
            SettingsManager.SaveSettings(AppSettings);
            
            MessageBox.Show("Шаблон рамки очищен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
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
        
        // Метод для добавления текстового маркера на канву
        private void AddTextMarker(TextElement textElement, Canvas canvas, List<TextMarker> markers)
        {
            // Скрываем маркер, если элемент отключен
            if (!textElement.Enabled && (textElement.Type == TextElementType.DateTime || textElement.Type == TextElementType.EventName))
            {
                return;
            }
            
            // Создаем текстовый блок для измерения размеров текста
            string displayText;
            switch (textElement.Type)
            {
                case TextElementType.DateTime:
                    displayText = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                    break;
                case TextElementType.EventName:
                    displayText = !string.IsNullOrEmpty(textElement.Text) ? textElement.Text : "Название события";
                    break;
                default:
                    displayText = textElement.Text;
                    break;
            }
            
            // Создаем временный текстовый блок для измерения
            TextBlock measureBlock = new TextBlock
            {
                Text = displayText,
                FontSize = 14 * textElement.FontSize,
                FontFamily = new FontFamily(textElement.FontFamily),
                FontWeight = textElement.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = textElement.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = textElement.IsUnderline ? TextDecorations.Underline : null,
                Margin = new Thickness(5)
            };
            
            // Добавляем на канву для измерения
            canvas.Children.Add(measureBlock);
            measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            
            // Получаем размеры текста
            double textWidth = measureBlock.DesiredSize.Width + 10; // Добавляем отступы
            double textHeight = measureBlock.DesiredSize.Height + 6;
            
            // Удаляем временный блок после измерения
            canvas.Children.Remove(measureBlock);
            
            // Создаем фон текста с учетом измеренных размеров
            Rectangle background = new Rectangle
            {
                Width = Math.Max(200, textWidth), // Минимальная ширина 200px
                Height = Math.Max(30, textHeight), // Минимальная высота 30px
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 255)),
                StrokeThickness = 1,
                RadiusX = 3,
                RadiusY = 3
            };
            
            // Устанавливаем позицию фона
            Canvas.SetLeft(background, textElement.X);
            Canvas.SetTop(background, textElement.Y);
            
            // Добавляем на канву
            canvas.Children.Add(background);
            
            // Создаем текстовый блок с правильными настройками
            TextBlock textBlock = new TextBlock
            {
                Text = displayText,
                FontSize = 14 * textElement.FontSize,
                FontFamily = new FontFamily(textElement.FontFamily),
                FontWeight = textElement.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = textElement.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = textElement.IsUnderline ? TextDecorations.Underline : null,
                Margin = new Thickness(5),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Устанавливаем позицию текста
            Canvas.SetLeft(textBlock, textElement.X + 5);
            Canvas.SetTop(textBlock, textElement.Y + 3);
            
            // Добавляем на канву
            canvas.Children.Add(textBlock);
            
            // Найдем индекс текущего элемента в общем списке
            int elementIndex = AppSettings.TextElements.IndexOf(textElement);
            
            // Создаем текстовый маркер
            TextMarker marker = new TextMarker
            {
                TextBlock = textBlock,
                Background = background,
                Index = elementIndex >= 0 ? elementIndex : markers.Count,
                IsDragging = false
            };
            
            // Добавляем возможность перетаскивания
            background.MouseLeftButtonDown += (s, e) =>
            {
                marker.IsDragging = true;
                marker.LastMousePosition = e.GetPosition(canvas);
                background.CaptureMouse();
                e.Handled = true;
                
                // Выделяем выбранный элемент
                background.Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255));
                background.StrokeThickness = 2;
            };
            
            background.MouseLeftButtonUp += (s, e) =>
            {
                marker.IsDragging = false;
                background.ReleaseMouseCapture();
                e.Handled = true;
                
                // Снимаем выделение
                background.Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 255));
                background.StrokeThickness = 1;
                
                // Обновляем позицию в настройках
                textElement.X = Canvas.GetLeft(background);
                textElement.Y = Canvas.GetTop(background);
            };
            
            background.MouseMove += (s, e) =>
            {
                if (marker.IsDragging)
                {
                    var currentPosition = e.GetPosition(canvas);
                    var deltaX = currentPosition.X - marker.LastMousePosition.X;
                    var deltaY = currentPosition.Y - marker.LastMousePosition.Y;
                    
                    var newLeft = Canvas.GetLeft(background) + deltaX;
                    var newTop = Canvas.GetTop(background) + deltaY;
                    
                    // Ограничиваем позицию границами канвы
                    newLeft = Math.Max(0, Math.Min(canvas.Width - background.Width, newLeft));
                    newTop = Math.Max(0, Math.Min(canvas.Height - background.Height, newTop));
                    
                    Canvas.SetLeft(background, newLeft);
                    Canvas.SetTop(background, newTop);
                    
                    // Обновляем позицию текста
                    Canvas.SetLeft(textBlock, newLeft + 5);
                    Canvas.SetTop(textBlock, newTop + 3);
                    
                    marker.LastMousePosition = currentPosition;
                }
            };
            
            // Добавляем маркер в список
            markers.Add(marker);
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
                if (cbCameras.SelectedIndex >= 0)
                {
                    AppSettings.CameraIndex = cbCameras.SelectedIndex;
                }
                
                AppSettings.RotationMode = cbRotation.SelectedValue?.ToString() ?? "Без поворота";
                AppSettings.MirrorMode = chkMirrorMode.IsChecked ?? false;
                
                if (int.TryParse(cbPhotoCount.SelectedValue?.ToString(), out int photoCount))
                {
                    AppSettings.PhotoCount = photoCount;
                }
                
                if (int.TryParse(cbPhotoCountdownTime.SelectedValue?.ToString(), out int photoCountdownTime))
                {
                    AppSettings.PhotoCountdownTime = photoCountdownTime;
                }
                
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
                
                // Инициализируем текстовые элементы, если их нет
                if (AppSettings.TextElements == null || AppSettings.TextElements.Count == 0)
                {
                    InitializeDefaultTextElements();
                }
                
                // Логируем текущие позиции фотографий
                System.Diagnostics.Debug.WriteLine($"Сохранено {AppSettings.PhotoPositions.Count} позиций фотографий:");
                for (int i = 0; i < AppSettings.PhotoPositions.Count; i++)
                {
                    var pos = AppSettings.PhotoPositions[i];
                    System.Diagnostics.Debug.WriteLine($"Позиция {i+1}: X={pos.X}, Y={pos.Y}, Width={pos.Width}, Height={pos.Height}");
                }
                
                if (int.TryParse(cbRecordingDuration.SelectedValue?.ToString(), out int recordingDuration))
                {
                    AppSettings.RecordingDuration = recordingDuration;
                }
                
                if (int.TryParse(cbVideoCountdownTime.SelectedValue?.ToString(), out int videoCountdownTime))
                {
                    AppSettings.VideoCountdownTime = videoCountdownTime;
                }
                
                if (cbMicrophones.SelectedIndex >= 0)
                {
                    AppSettings.MicrophoneIndex = cbMicrophones.SelectedIndex;
                }
                
                // Сохраняем настройки принтера
                if (cbPrinters.SelectedValue != null)
                {
                    AppSettings.SelectedPrinter = cbPrinters.SelectedValue.ToString();
                }
                
                if (double.TryParse(txtPrintWidth.Text, out double printWidth))
                {
                    AppSettings.PrintWidth = printWidth;
                }
                
                if (double.TryParse(txtPrintHeight.Text, out double printHeight))
                {
                    AppSettings.PrintHeight = printHeight;
                }
                
                // Сохраняем режимы обработки изображений
                AppSettings.PhotoProcessingMode = (ImageProcessingMode)cbPhotoProcessingMode.SelectedIndex;
                AppSettings.PrintProcessingMode = (ImageProcessingMode)cbPrintProcessingMode.SelectedIndex;
                
                // Сохраняем настройки QR-кода
                AppSettings.QrCodeSize = (int)sliderQrSize.Value;
                AppSettings.QrLogoSize = (int)sliderQrLogoSize.Value;
                // Остальные настройки QR-кода (цвета и путь к логотипу) сохраняются при их изменении
                
                // Сохраняем настройки
                if (SettingsManager.SaveSettings(AppSettings))
                {
                    MessageBox.Show("Настройки успешно сохранены.", "Информация", 
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Закрываем окно настроек
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Не удалось сохранить настройки.", "Ошибка", 
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении настроек: {ex.Message}", "Ошибка", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
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
        
        // Метод для инициализации текстовых элементов по умолчанию
        private void InitializeDefaultTextElements()
        {
            try
            {
                // Очищаем существующие текстовые элементы
                AppSettings.TextElements.Clear();
                
                // Добавляем элемент даты и времени (внизу слева)
                AppSettings.TextElements.Add(new TextElement
                {
                    Type = TextElementType.DateTime,
                    X = 20,
                    Y = 1780, // Внизу (1800 - 20)
                    FontSize = 1.0,
                    FontFamily = "Arial",
                    IsBold = false,
                    IsItalic = false,
                    IsUnderline = false,
                    Enabled = false // По умолчанию отключен
                });
                
                // Добавляем элемент с названием события (вверху слева)
                AppSettings.TextElements.Add(new TextElement
                {
                    Type = TextElementType.EventName,
                    X = 20,
                    Y = 40,
                    FontSize = 1.0,
                    FontFamily = "Arial",
                    IsBold = true,  // Название события по умолчанию жирным
                    IsItalic = false,
                    IsUnderline = false,
                    Enabled = false // По умолчанию отключен
                });
                
                System.Diagnostics.Debug.WriteLine($"Созданы текстовые элементы по умолчанию: {AppSettings.TextElements.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при создании текстовых элементов по умолчанию: {ex.Message}");
                AppSettings.TextElements = new List<TextElement>();
            }
        }
        
        // Обновляет состояние элементов управления текстовыми надписями
        private void UpdateTextElementControls()
        {
            // Пустой метод, оставлен для совместимости
        }
        
        // Загружаем настройки в интерфейс
        private void LoadSettings()
        {
            try
            {
                AppSettings = SettingsManager.LoadSettings();
                System.Diagnostics.Debug.WriteLine($"Настройки загружены успешно. Путь: {SettingsManager.GetSettingsFilePath()}");
                
                // Инициализируем текстовые элементы, если их нет
                if (AppSettings.TextElements == null || AppSettings.TextElements.Count == 0)
                {
                    InitializeDefaultTextElements();
                }
                
                // Заполняем интерфейс настроек загруженными значениями
                RefreshCameraList();
                
                cbRotation.SelectedValue = AppSettings.RotationMode;
                chkMirrorMode.IsChecked = AppSettings.MirrorMode;
                
                cbPhotoCount.SelectedValue = AppSettings.PhotoCount.ToString();
                cbPhotoCountdownTime.SelectedValue = AppSettings.PhotoCountdownTime.ToString();
                
                txtFrameTemplatePath.Text = AppSettings.FrameTemplatePath;
                
                cbRecordingDuration.SelectedValue = AppSettings.RecordingDuration.ToString();
                cbVideoCountdownTime.SelectedValue = AppSettings.VideoCountdownTime.ToString();
                
                RefreshPrinterList();
                if (!string.IsNullOrEmpty(AppSettings.SelectedPrinter))
                {
                    cbPrinters.SelectedValue = AppSettings.SelectedPrinter;
                }
                
                txtPrintWidth.Text = AppSettings.PrintWidth.ToString();
                txtPrintHeight.Text = AppSettings.PrintHeight.ToString();
                
                cbPhotoProcessingMode.SelectedIndex = (int)AppSettings.PhotoProcessingMode;
                cbPrintProcessingMode.SelectedIndex = (int)AppSettings.PrintProcessingMode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при загрузке настроек: {ex.Message}");
                AppSettings = new AppSettings();
            }
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
        
        // Добавляем список текстовых элементов
        public List<TextElement> TextElements { get; set; } = new List<TextElement>();
        public string SelectedPrinter { get; set; }
        
        // Настройки QR-кода
        public string QrBackgroundColor { get; set; } = "#FFFFFF"; // Белый фон по умолчанию
        public string QrForegroundColor { get; set; } = "#000000"; // Черный цвет QR-кода по умолчанию
        public string QrLogoPath { get; set; } = ""; // Путь к логотипу для центра QR-кода
        public int QrLogoSize { get; set; } = 30; // Размер логотипа в процентах от размера QR-кода (по умолчанию 30%)
        public int QrCodeSize { get; set; } = 300; // Размер QR-кода в пикселях
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