using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Threading.Tasks;

namespace UnifiedPhotoBooth
{
    public partial class GalleryPage : Page
    {
        private const string PhotosDir = "photos";
        private const string RecordingsDir = "recordings";
        private const int ThumbnailSize = 200;
        
        public GalleryPage()
        {
            InitializeComponent();
            Loaded += GalleryPage_Loaded;
        }
        
        private void GalleryPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadGalleryItems();
        }
        
        private void LoadGalleryItems()
        {
            try
            {
                galleryPanel.Children.Clear();
                
                // Загружаем фотографии
                LoadMediaFiles(PhotosDir, ".jpg", "Фото");
                
                // Загружаем видео
                LoadMediaFiles(RecordingsDir, ".mp4", "Видео");
                
                // Если нет элементов, показываем сообщение
                if (galleryPanel.Children.Count == 0)
                {
                    TextBlock noItemsText = new TextBlock
                    {
                        Text = "Нет доступных фотографий или видео",
                        FontSize = 18,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(20)
                    };
                    galleryPanel.Children.Add(noItemsText);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке галереи: {ex.Message}", "Ошибка", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadMediaFiles(string directory, string extension, string typeLabel)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            // Получаем файлы и сортируем их по дате создания (сначала новые)
            var files = Directory.GetFiles(directory, $"*{extension}")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();
            
            foreach (var file in files)
            {
                // Проверяем, существует ли связанный QR-код
                string qrFilePath = Path.Combine(
                    file.DirectoryName, 
                    Path.GetFileNameWithoutExtension(file.Name) + "_qr.png");
                
                bool hasQrCode = File.Exists(qrFilePath);
                
                // Создаем элемент галереи
                Grid itemGrid = new Grid
                {
                    Width = ThumbnailSize,
                    Height = ThumbnailSize + 50, // Дополнительное пространство для метки
                    Margin = new Thickness(10)
                };
                
                // Контейнер для миниатюры
                Border thumbnailBorder = new Border
                {
                    Width = ThumbnailSize,
                    Height = ThumbnailSize,
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Top
                };
                
                // Изображение миниатюры
                Image thumbnailImage = new Image
                {
                    Stretch = Stretch.UniformToFill
                };
                
                // Загружаем миниатюру
                if (extension == ".jpg")
                {
                    try
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(file.FullName);
                        bitmap.DecodePixelWidth = ThumbnailSize;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        thumbnailImage.Source = bitmap;
                    }
                    catch
                    {
                        // В случае ошибки загрузки изображения используем заглушку
                        thumbnailImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/no_image.png", UriKind.Absolute));
                    }
                }
                else
                {
                    // Для видео используем значок видео
                    thumbnailImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/video_icon.png", UriKind.Absolute));
                }
                
                thumbnailBorder.Child = thumbnailImage;
                
                // Метка с типом и датой
                TextBlock infoText = new TextBlock
                {
                    Text = $"{typeLabel} - {file.CreationTime.ToString("dd.MM.yyyy HH:mm")}",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, ThumbnailSize + 5, 0, 0)
                };
                
                // Добавляем элементы в сетку
                itemGrid.Children.Add(thumbnailBorder);
                itemGrid.Children.Add(infoText);
                
                // Создаем объект с информацией о медиафайле и QR-коде
                var mediaInfo = new MediaInfo
                {
                    FilePath = file.FullName,
                    QrCodePath = hasQrCode ? qrFilePath : null
                };
                
                // Обработчик нажатия
                itemGrid.Tag = mediaInfo;
                itemGrid.MouseLeftButtonDown += GalleryItem_Click;
                itemGrid.Cursor = System.Windows.Input.Cursors.Hand;
                
                // Добавляем элемент в галерею
                galleryPanel.Children.Add(itemGrid);
            }
        }
        
        private class MediaInfo
        {
            public string FilePath { get; set; }
            public string QrCodePath { get; set; }
        }
        
        private void GalleryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is MediaInfo mediaInfo)
            {
                string filePath = mediaInfo.FilePath;
                string qrCodePath = mediaInfo.QrCodePath;
                
                // Определяем тип файла
                bool isVideo = filePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                
                // Открываем страницу с результатом
                if (isVideo)
                {
                    // Для видео
                    NavigationService.Navigate(new VideoResultPage(filePath, qrCodePath));
                }
                else
                {
                    // Для фото
                    NavigationService.Navigate(new PhotoResultPage(filePath, qrCodePath));
                }
            }
        }
        
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Возвращаемся на главную страницу
            NavigationService.GoBack();
        }
    }
    
    // Вспомогательный класс для отображения результата фото
    public class PhotoResultPage : Page
    {
        private string _imagePath;
        private string _qrCodePath;
        
        public PhotoResultPage(string imagePath, string qrCodePath = null)
        {
            _imagePath = imagePath;
            _qrCodePath = qrCodePath;
            
            Grid grid = new Grid();
            
            // Изображение
            Image image = new Image
            {
                Source = new BitmapImage(new Uri(imagePath, UriKind.Absolute)),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(20)
            };
            
            // Кнопка "Назад"
            Button btnBack = new Button
            {
                Content = "Назад",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnBack.Click += BtnBack_Click;
            
            // Кнопка "Поделиться"
            Button btnShare = new Button
            {
                Content = "Поделиться QR",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5),
                Visibility = string.IsNullOrEmpty(_qrCodePath) ? Visibility.Collapsed : Visibility.Visible
            };
            btnShare.Click += BtnShare_Click;
            
            // Кнопка "Печать"
            Button btnPrint = new Button
            {
                Content = "Печать",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnPrint.Click += BtnPrint_Click;
            
            // Добавляем элементы в сетку
            grid.Children.Add(image);
            grid.Children.Add(btnBack);
            grid.Children.Add(btnShare);
            grid.Children.Add(btnPrint);
            
            // Назначаем сетку содержимым страницы
            Content = grid;
        }
        
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.GoBack();
        }
        
        private void BtnShare_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_qrCodePath) && File.Exists(_qrCodePath))
            {
                // Показываем страницу с QR-кодом, используя сохраненный файл QR-кода
                NavigationService.Navigate(new QRCodePage(_qrCodePath, false, true));
            }
            else
            {
                MessageBox.Show("QR-код не найден для этого файла.", "Информация", 
                               MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Открываем диалог печати
                PrintDialog printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    // Отправляем изображение на печать
                    PrintImage(printDialog, _imagePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при печати: {ex.Message}", "Ошибка", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void PrintImage(PrintDialog printDialog, string imagePath)
        {
            try
            {
                // Загружаем изображение
                BitmapImage bitmapImage = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
                
                // Создаем элемент Image для печати
                Image printImage = new Image();
                printImage.Source = bitmapImage;
                
                // Получаем размеры принтера в пикселях
                System.Windows.Size printSize = new System.Windows.Size(
                    printDialog.PrintableAreaWidth,
                    printDialog.PrintableAreaHeight);
                
                // Масштабируем изображение для печати
                printImage.Measure(printSize);
                printImage.Arrange(new Rect(new System.Windows.Point(0, 0), printSize));
                
                // Отправляем на печать
                printDialog.PrintVisual(printImage, "Печать фотографии");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при подготовке изображения к печати: {ex.Message}", ex);
            }
        }
    }
    
    // Вспомогательный класс для отображения результата видео
    public class VideoResultPage : Page
    {
        private string _videoPath;
        private string _qrCodePath;
        private MediaElement _mediaElement;
        
        public VideoResultPage(string videoPath, string qrCodePath = null)
        {
            _videoPath = videoPath;
            _qrCodePath = qrCodePath;
            
            Grid grid = new Grid();
            
            // Видеоплеер
            _mediaElement = new MediaElement
            {
                Source = new Uri(videoPath, UriKind.Absolute),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(20)
            };
            
            // Обработчики событий видео
            _mediaElement.Loaded += MediaElement_Loaded;
            _mediaElement.MediaEnded += MediaElement_MediaEnded;
            
            // Кнопка "Назад"
            Button btnBack = new Button
            {
                Content = "Назад",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnBack.Click += BtnBack_Click;
            
            // Кнопка "Поделиться"
            Button btnShare = new Button
            {
                Content = "Поделиться QR",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnShare.Click += BtnShare_Click;
            
            // Кнопка "Воспроизведение/Пауза"
            Button btnPlayPause = new Button
            {
                Content = "▶/⏸",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnPlayPause.Click += BtnPlayPause_Click;
            
            // Добавляем элементы в сетку
            grid.Children.Add(_mediaElement);
            grid.Children.Add(btnBack);
            grid.Children.Add(btnShare);
            grid.Children.Add(btnPlayPause);
            
            // Назначаем сетку содержимым страницы
            Content = grid;
        }
        
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            _mediaElement.Play();
        }
        
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Перезапускаем видео с начала
            _mediaElement.Position = TimeSpan.Zero;
            _mediaElement.Play();
        }
        
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Останавливаем видео и возвращаемся назад
            _mediaElement.Stop();
            NavigationService.GoBack();
        }
        
        private void BtnShare_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_qrCodePath) && File.Exists(_qrCodePath))
            {
                // Показываем страницу с QR-кодом, используя сохраненный файл QR-кода
                NavigationService.Navigate(new QRCodePage(_qrCodePath, true, true));
            }
            else
            {
                MessageBox.Show("QR-код не найден для этого файла.", "Информация", 
                               MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем воспроизведение/паузу
            if (_mediaElement.Position >= _mediaElement.NaturalDuration.TimeSpan)
            {
                _mediaElement.Position = TimeSpan.Zero;
            }
            
            if (_mediaElement.IsLoaded)
            {
                if (_mediaElement.CanPause)
                {
                    _mediaElement.Pause();
                }
                else
                {
                    _mediaElement.Play();
                }
            }
        }
    }
    
    // Страница с QR-кодом для доступа к фото/видео
    public class QRCodePage : Page
    {
        private string _mediaPath;
        private bool _isVideo;
        private bool _showSavedQR;
        
        public QRCodePage(string mediaPath, bool isVideo, bool showSavedQR = false)
        {
            _mediaPath = mediaPath;
            _isVideo = isVideo;
            _showSavedQR = showSavedQR;
            
            Grid grid = new Grid();
            
            // Заголовок
            TextBlock title = new TextBlock
            {
                Text = "Сканируйте QR-код для доступа к файлу",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 20,
                Margin = new Thickness(0, 20, 0, 20)
            };
            
            // QR-код
            Image qrImage = new Image
            {
                Width = 300,
                Height = 300,
                Margin = new Thickness(20)
            };
            
            // Если нужно показать сохраненный QR-код
            if (_showSavedQR)
            {
                try
                {
                    // Загружаем QR-код из файла
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_mediaPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    qrImage.Source = bitmap;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке QR-кода: {ex.Message}", "Ошибка", 
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Создаем новый QR-код
                GoogleDriveService driveService = new GoogleDriveService();
                string folderName = $"{(_isVideo ? "VideoBooth" : "PhotoBooth")}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                
                try
                {
                    // Создаем задачу для загрузки файла
                    Task<UploadResult> uploadTask;
                    if (_isVideo)
                    {
                        uploadTask = driveService.UploadVideoAsync(_mediaPath, folderName, null);
                    }
                    else
                    {
                        uploadTask = driveService.UploadPhotoAsync(_mediaPath, folderName, null);
                    }
                    
                    // Запускаем и ожидаем завершения
                    uploadTask.Wait();
                    
                    // Получаем результат
                    var result = uploadTask.Result;
                    
                    // Отображаем QR-код
                    if (result != null && result.QrCode != null)
                    {
                        // Конвертируем QR-код в формат для отображения
                        BitmapImage qrBitmap = new BitmapImage();
                        using (MemoryStream ms = new MemoryStream())
                        {
                            result.QrCode.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;
                            qrBitmap.BeginInit();
                            qrBitmap.CacheOption = BitmapCacheOption.OnLoad;
                            qrBitmap.StreamSource = ms;
                            qrBitmap.EndInit();
                        }
                        
                        qrImage.Source = qrBitmap;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при создании QR-кода: {ex.Message}", "Ошибка", 
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            // Кнопка "Назад"
            Button btnBack = new Button
            {
                Content = "Назад",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(20),
                Padding = new Thickness(10, 5, 10, 5)
            };
            btnBack.Click += BtnBack_Click;
            
            // Добавляем элементы в сетку
            grid.Children.Add(title);
            
            // Создаем StackPanel для центрирования QR-кода
            StackPanel centerPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            centerPanel.Children.Add(qrImage);
            
            grid.Children.Add(centerPanel);
            grid.Children.Add(btnBack);
            
            // Назначаем сетку содержимым страницы
            Content = grid;
        }
        
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.GoBack();
        }
    }
} 