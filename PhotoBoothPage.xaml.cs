using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Windows.Media;
using System.Diagnostics;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace UnifiedPhotoBooth
{
    public partial class PhotoBoothPage : Page
    {
        private GoogleDriveService _driveService;
        private string _eventFolderId;
        private string _eventName;
        
        private VideoCapture _capture;
        private DispatcherTimer _previewTimer;
        private bool _previewRunning;
        
        private int _currentPhotoIndex = 0;
        private List<Mat> _capturedPhotos;
        private string _finalImagePath;
        private string _lastUniversalFolderId;
        
        private DispatcherTimer _countdownTimer;
        private int _countdownValue;
        private Action _onCountdownComplete;
        
        public PhotoBoothPage(GoogleDriveService driveService, string eventFolderId = null)
        {
            InitializeComponent();
            
            _driveService = driveService;
            _eventFolderId = eventFolderId;
            _capturedPhotos = new List<Mat>();
            
            if (!string.IsNullOrEmpty(_eventFolderId))
            {
                _eventName = GetEventNameById(_eventFolderId);
                txtEventName.Text = _eventName;
            }
            
            // Инициализация таймеров
            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 fps
            };
            _previewTimer.Tick += PreviewTimer_Tick;
            
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
            
            Loaded += PhotoBoothPage_Loaded;
            Unloaded += PhotoBoothPage_Unloaded;
        }
        
        private string GetEventNameById(string eventId)
        {
            try
            {
                var events = _driveService.ListEvents();
                foreach (var evt in events)
                {
                    if (evt.Value == eventId)
                    {
                        return evt.Key;
                    }
                }
            }
            catch { }
            
            return "Неизвестное событие";
        }
        
        private void PhotoBoothPage_Loaded(object sender, RoutedEventArgs e)
        {
            StartPreview();
        }
        
        private void PhotoBoothPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPreview();
            
            // Освобождаем ресурсы
            if (_capturedPhotos != null)
            {
                foreach (var photo in _capturedPhotos)
                {
                    photo?.Dispose();
                }
                _capturedPhotos.Clear();
            }
        }
        
        private void StartPreview()
        {
            try
            {
                // Инициализация камеры
                _capture = new VideoCapture(SettingsWindow.AppSettings.CameraIndex);
                if (!_capture.IsOpened())
                {
                    ShowError("Не удалось открыть камеру. Проверьте настройки.");
                    return;
                }
                
                _previewRunning = true;
                _previewTimer.Start();
                
                ShowStatus("Готов к съемке", "Нажмите 'Начать', чтобы сделать фотографии");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при запуске предпросмотра: {ex.Message}");
            }
        }
        
        private void StopPreview()
        {
            _previewRunning = false;
            _previewTimer.Stop();
            
            _capture?.Dispose();
            _capture = null;
        }
        
        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            if (!_previewRunning || _capture == null || !_capture.IsOpened())
                return;
            
            using (var frame = new Mat())
            {
                if (_capture.Read(frame))
                {
                    // Применяем поворот, если необходимо
                    ApplyRotation(frame);
                    
                    // Применяем зеркальное отображение, если включено
                    if (SettingsWindow.AppSettings.MirrorMode)
                    {
                        Cv2.Flip(frame, frame, FlipMode.Y);
                    }
                    
                    // Обрабатываем фото в соответствии с настройками
                    Mat processedFrame = AdjustAspectRatioByMode(frame, 1200.0 / 1800.0, SettingsWindow.AppSettings.PhotoProcessingMode);
                    
                    // Отображаем кадр
                    imgPreview.Source = BitmapSourceConverter.ToBitmapSource(processedFrame);
                    
                    // Освобождаем ресурсы
                    if (processedFrame != frame)
                    {
                        processedFrame.Dispose();
                    }
                }
            }
        }
        
        private Mat AdjustAspectRatioByMode(Mat frame, double targetAspectRatio, ImageProcessingMode mode)
        {
            if (frame == null)
                return null;
            
            // Всегда просто клонируем кадр без какой-либо обработки
            return frame.Clone();
        }
        
        private void ApplyRotation(Mat frame)
        {
            OpenCvSharp.RotateFlags? rotateMode = null;
            
            switch (SettingsWindow.AppSettings.RotationMode)
            {
                case "90° вправо (вертикально)":
                    rotateMode = OpenCvSharp.RotateFlags.Rotate90Clockwise;
                    break;
                case "90° влево (вертикально)":
                    rotateMode = OpenCvSharp.RotateFlags.Rotate90Counterclockwise;
                    break;
                case "180°":
                    rotateMode = OpenCvSharp.RotateFlags.Rotate180;
                    break;
            }
            
            if (rotateMode.HasValue)
            {
                Cv2.Rotate(frame, frame, rotateMode.Value);
            }
        }
        
        private void StartCountdown(int seconds, Action onComplete)
        {
            _countdownValue = seconds;
            txtCountdown.Text = _countdownValue.ToString();
            txtCountdown.Visibility = Visibility.Visible;
            
            // Устанавливаем делегат для обратного вызова по завершении отсчета
            _onCountdownComplete = onComplete;
            
            // Запускаем таймер обратного отсчета
            _countdownTimer.Start();
        }
        
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Убираем проверку на выбор события
            // Очищаем предыдущие фотографии
            foreach (var photo in _capturedPhotos)
            {
                photo?.Dispose();
            }
            _capturedPhotos.Clear();
            
            // Сбрасываем индекс текущей фотографии
            _currentPhotoIndex = 0;
            
            // Скрываем результат, если он был показан
            imgResult.Visibility = Visibility.Collapsed;
            imgQrCode.Visibility = Visibility.Collapsed;
            txtQrCodeUrl.Visibility = Visibility.Collapsed;
            
            // Показываем превью
            imgPreview.Visibility = Visibility.Visible;
            
            // Обновляем интерфейс
            btnStart.Visibility = Visibility.Collapsed;
            btnReset.Visibility = Visibility.Visible;
            btnShare.Visibility = Visibility.Collapsed;
            btnPrint.Visibility = Visibility.Collapsed;
            
            ShowStatus("Подготовка к съемке", "Позируйте для фотографии");
            
            // Запускаем процесс съемки
            StartPhotoProcess();
        }
        
        private void StartPhotoProcess()
        {
            // Запускаем отсчет
            StartCountdown(SettingsWindow.AppSettings.PhotoCountdownTime, CapturePhoto);
        }
        
        private void CapturePhoto()
        {
            if (!_previewRunning || _capture == null)
                return;
            
            using (var frame = new Mat())
            {
                if (_capture.Read(frame))
                {
                    // Применяем поворот и зеркальное отображение
                    ApplyRotation(frame);
                    
                    if (SettingsWindow.AppSettings.MirrorMode)
                    {
                        Cv2.Flip(frame, frame, FlipMode.Y);
                    }
                    
                    // Просто клонируем кадр без обработки
                    var photo = frame.Clone();
                    _capturedPhotos.Add(photo);
                    
                    ShowStatus($"Фото {_currentPhotoIndex + 1} из {SettingsWindow.AppSettings.PhotoCount}", "Фотография сделана!");
                    
                    _currentPhotoIndex++;
                    
                    // Проверяем, нужно ли сделать еще фотографии
                    if (_currentPhotoIndex < SettingsWindow.AppSettings.PhotoCount)
                    {
                        // Даем паузу между фотографиями
                        Task.Delay(1000).ContinueWith(t => 
                        {
                            Dispatcher.Invoke(() => 
                            {
                                ShowStatus($"Подготовка к фото {_currentPhotoIndex + 1}", "Позируйте для следующей фотографии");
                                StartPhotoProcess();
                            });
                        });
                    }
                    else
                    {
                        // Все фотографии сделаны, создаем коллаж
                        ShowStatus("Создание коллажа", "Пожалуйста, подождите...");
                        
                        Task.Run(() => 
                        {
                            var finalImage = CreateCollage();
                            
                            Dispatcher.Invoke(() => 
                            {
                                // Показываем результат
                                imgPreview.Visibility = Visibility.Collapsed;
                                imgResult.Source = BitmapSourceConverter.ToBitmapSource(finalImage);
                                imgResult.Visibility = Visibility.Visible;
                                
                                ShowStatus("Коллаж готов", "Ваши фотографии готовы!");
                                
                                // Обновляем кнопки
                                btnReset.Visibility = Visibility.Visible;
                                UpdateShareButtonVisibility();
                                btnPrint.Visibility = Visibility.Visible;
                            });
                        });
                    }
                }
            }
        }
        
        private Mat ProcessPhoto(Mat photo, double targetWidth, double targetHeight)
        {
            if (photo == null)
                return null;
            
            // Просто изменяем размер фото без дополнительной обработки
            Mat processedPhoto = new Mat();
            Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(targetWidth, targetHeight));
            return processedPhoto;
        }
        
        private Mat AdjustAspectRatio(Mat frame, double targetAspectRatio)
        {
            // Используем обрезку как режим по умолчанию для обратной совместимости
            return AdjustAspectRatioByMode(frame, targetAspectRatio, ImageProcessingMode.Crop);
        }
        
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            // Сбрасываем состояние
            _currentPhotoIndex = 0;
            
            foreach (var photo in _capturedPhotos)
            {
                photo?.Dispose();
            }
            _capturedPhotos.Clear();
            
            // Обновляем интерфейс
            imgResult.Visibility = Visibility.Collapsed;
            imgQrCode.Visibility = Visibility.Collapsed;
            txtQrCodeUrl.Visibility = Visibility.Collapsed;
            imgPreview.Visibility = Visibility.Visible;
            
            btnStart.Visibility = Visibility.Visible;
            btnReset.Visibility = Visibility.Collapsed;
            btnShare.Visibility = Visibility.Collapsed;
            btnPrint.Visibility = Visibility.Collapsed;
            
            ShowStatus("Готов к съемке", "Нажмите 'Начать', чтобы сделать фотографии");
        }
        
        private async void BtnShare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_finalImagePath) || !File.Exists(_finalImagePath))
            {
                ShowError("Файл коллажа не найден.");
                return;
            }
            
            try
            {
                ShowStatus("Загрузка фото", "Пожалуйста, подождите...");
                
                // Отключаем кнопки на время загрузки
                btnReset.IsEnabled = false;
                btnShare.IsEnabled = false;
                btnPrint.IsEnabled = false;
                
                // Загружаем фото в Google Drive
                string folderName = $"PhotoBooth_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                var result = await _driveService.UploadPhotoAsync(_finalImagePath, folderName, _eventFolderId, false, _lastUniversalFolderId);
                
                // Сохраняем ID папки для возможного повторного использования
                _lastUniversalFolderId = result.FolderId;
                
                // Конвертируем QR-код в формат для отображения
                BitmapImage qrImage = new BitmapImage();
                using (MemoryStream ms = new MemoryStream())
                {
                    result.QrCode.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    qrImage.BeginInit();
                    qrImage.CacheOption = BitmapCacheOption.OnLoad;
                    qrImage.StreamSource = ms;
                    qrImage.EndInit();
                }
                
                // Отображаем только QR-код без ссылки
                imgQrCode.Source = qrImage;
                imgQrCode.Visibility = Visibility.Visible;
                
                // Скрываем текстовое поле ссылки
                txtQrCodeUrl.Visibility = Visibility.Collapsed;
                
                ShowStatus("Фото загружено", "Отсканируйте QR-код для доступа к фотографии");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при загрузке фото: {ex.Message}");
            }
            finally
            {
                // Включаем кнопки
                btnReset.IsEnabled = true;
                btnShare.IsEnabled = true;
                btnPrint.IsEnabled = true;
            }
        }
        
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_finalImagePath) || !File.Exists(_finalImagePath))
            {
                ShowError("Файл коллажа не найден.");
                return;
            }
            
            try
            {
                // Проверяем, есть ли выбранный принтер в настройках
                if (string.IsNullOrEmpty(SettingsWindow.AppSettings.PrinterName))
                {
                    System.Windows.Controls.PrintDialog printDialog = new System.Windows.Controls.PrintDialog();
                    if (printDialog.ShowDialog() == true)
                    {
                        // Создаем и настраиваем изображение для печати с учетом выбранного режима обработки
                        PrintImage(printDialog, _finalImagePath);
                    }
                }
                else
                {
                    // Используем выбранный принтер
                    System.Windows.Controls.PrintDialog printDialog = new System.Windows.Controls.PrintDialog();
                    printDialog.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), SettingsWindow.AppSettings.PrinterName);
                    
                    // Создаем и настраиваем изображение для печати с учетом выбранного режима обработки
                    PrintImage(printDialog, _finalImagePath);
                    
                    ShowStatus("Печать", "Задание отправлено на печать");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при печати: {ex.Message}");
            }
        }
        
        private void PrintImage(System.Windows.Controls.PrintDialog printDialog, string imagePath)
        {
            try
            {
                // Загружаем изображение
                BitmapImage bitmap = new BitmapImage(new Uri(imagePath));
                
                // Определяем ориентацию изображения
                bool isImageVertical = bitmap.PixelHeight > bitmap.PixelWidth;
                
                // Получаем размеры страницы принтера
                double printWidth = SettingsWindow.AppSettings.PrintWidth;
                double printHeight = SettingsWindow.AppSettings.PrintHeight;
                
                // Создаем контейнер для изображения с учетом ориентации
                System.Windows.Controls.Canvas canvas = new System.Windows.Controls.Canvas();
                
                // Задаем размеры для холста в зависимости от ориентации изображения
                if (isImageVertical)
                {
                    // Если изображение вертикальное, используем вертикальную ориентацию бумаги
                    canvas.Width = ConvertCmToPixels(Math.Min(printWidth, printHeight));
                    canvas.Height = ConvertCmToPixels(Math.Max(printWidth, printHeight));
                }
                else
                {
                    // Если изображение горизонтальное, используем горизонтальную ориентацию бумаги
                    canvas.Width = ConvertCmToPixels(Math.Max(printWidth, printHeight));
                    canvas.Height = ConvertCmToPixels(Math.Min(printWidth, printHeight));
                }
                
                canvas.Background = System.Windows.Media.Brushes.White;
                
                // Создаем изображение для печати
                System.Windows.Controls.Image printImage = new System.Windows.Controls.Image();
                printImage.Source = bitmap;
                printImage.Width = canvas.Width;
                printImage.Height = canvas.Height;
                printImage.Stretch = Stretch.Uniform;
                
                // Добавляем изображение на холст
                canvas.Children.Add(printImage);
                
                // Печать
                printDialog.PrintVisual(canvas, "PhotoboothPro - Печать");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при подготовке изображения для печати: {ex.Message}");
            }
        }
        
        private double ConvertCmToPixels(double cm)
        {
            // Разрешение экрана приблизительно 96 DPI
            const double INCH_TO_CM = 2.54;
            const double DPI = 96;
            return cm * DPI / INCH_TO_CM;
        }
        
        private void ShowStatus(string status, string info)
        {
            txtStatus.Text = status;
            txtInfo.Text = info;
        }
        
        private void ShowError(string message)
        {
            ShowStatus("Ошибка", message);
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _countdownValue--;
            
            if (_countdownValue > 0)
            {
                txtCountdown.Text = _countdownValue.ToString();
            }
            else
            {
                _countdownTimer.Stop();
                txtCountdown.Visibility = Visibility.Collapsed;
                
                // Делаем снимок после завершения отсчета
                _onCountdownComplete?.Invoke();
            }
        }
        
        private void UpdateShareButtonVisibility()
        {
            // Показываем кнопку "Поделиться" только если есть интернет
            btnShare.Visibility = _driveService.IsOnline ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private Mat CreateCollage()
        {
            try
            {
                // Создаем коллаж на основе шаблона, если он есть
                string frameTemplatePath = SettingsWindow.AppSettings.FrameTemplatePath;
                Mat result;
                
                // Всегда используем фиксированные размеры: 1200x1800, независимо от ориентации
                int finalWidth = 1200;
                int finalHeight = 1800;
                
                if (!string.IsNullOrEmpty(frameTemplatePath) && File.Exists(frameTemplatePath))
                {
                    // Загружаем шаблон
                    result = Cv2.ImRead(frameTemplatePath);
                    
                    // Масштабируем шаблон до нужного размера, если необходимо
                    if (result.Width != finalWidth || result.Height != finalHeight)
                    {
                        Cv2.Resize(result, result, new OpenCvSharp.Size(finalWidth, finalHeight));
                    }
                    
                    // Вставляем фотографии в позиции на шаблоне
                    var positions = SettingsWindow.AppSettings.PhotoPositions;
                    
                    // Логируем информацию о позициях и фотографиях
                    System.Diagnostics.Debug.WriteLine($"Создание коллажа: найдено {positions.Count} позиций и {_capturedPhotos.Count} фотографий");
                    for (int i = 0; i < positions.Count; i++)
                    {
                        var pos = positions[i];
                        System.Diagnostics.Debug.WriteLine($"Позиция {i+1}: X={pos.X}, Y={pos.Y}, Width={pos.Width}, Height={pos.Height}");
                    }
                    
                    // Если позиций меньше, чем фотографий, используем только доступные позиции
                    int photoCount = Math.Min(_capturedPhotos.Count, positions.Count);
                    System.Diagnostics.Debug.WriteLine($"Будет размещено {photoCount} фотографий");
                    
                    for (int i = 0; i < photoCount; i++)
                    {
                        if (i >= _capturedPhotos.Count) break;
                        
                        var photo = _capturedPhotos[i].Clone(); // Клонируем для безопасности операций
                        var pos = positions[i];
                        
                        // Изменяем размер фото для соответствия позиции в шаблоне
                        Mat processedPhoto = new Mat();
                        Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(pos.Width, pos.Height));
                        photo.Dispose();
                        
                        // Проверяем границы перед созданием ROI
                        int x = (int)Math.Max(0, Math.Min(pos.X, result.Width - 1));
                        int y = (int)Math.Max(0, Math.Min(pos.Y, result.Height - 1));
                        int w = (int)Math.Min(pos.Width, result.Width - x);
                        int h = (int)Math.Min(pos.Height, result.Height - y);
                        
                        // Проверяем, что размеры не равны нулю
                        if (w <= 0 || h <= 0) continue;
                        
                        // Создаем ROI (Region of Interest) для вставки
                        try
                        {
                            // Проверяем, что размеры фото соответствуют размерам ROI
                            if (processedPhoto.Width != w || processedPhoto.Height != h)
                            {
                                var temp = new Mat();
                                Cv2.Resize(processedPhoto, temp, new OpenCvSharp.Size(w, h));
                                processedPhoto.Dispose();
                                processedPhoto = temp;
                            }
                            
                            var roi = new Mat(result, new OpenCvSharp.Rect(x, y, w, h));
                            processedPhoto.CopyTo(roi);
                            processedPhoto.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ShowError($"Ошибка при вставке фото {i+1}: {ex.Message}");
                            });
                        }
                    }
                }
                else
                {
                    // Если шаблона нет, создаем простой коллаж из фотографий
                    // Создаем пустое изображение для коллажа с соблюдением пропорций
                    result = new Mat(finalHeight, finalWidth, MatType.CV_8UC3, Scalar.White);
                    
                    // Определяем размер и расположение областей в зависимости от количества фотографий
                    int cols, rows;
                    int photoWidth, photoHeight;
                    
                    if (_capturedPhotos.Count == 1) {
                        cols = 1;
                        rows = 1;
                        photoWidth = finalWidth;
                        photoHeight = finalHeight;
                    }
                    else if (_capturedPhotos.Count == 2) {
                        cols = 1;
                        rows = 2;
                        photoWidth = finalWidth;
                        photoHeight = finalHeight / 2;
                    }
                    else if (_capturedPhotos.Count == 3) {
                        cols = 1;
                        rows = 3;
                        photoWidth = finalWidth;
                        photoHeight = finalHeight / 3;
                    }
                    else { // 4 и более фотографий
                        cols = 2;
                        rows = 2;
                        photoWidth = finalWidth / 2;
                        photoHeight = finalHeight / 2;
                    }
                    
                    // Добавляем фотографии в коллаж
                    for (int i = 0; i < Math.Min(_capturedPhotos.Count, cols * rows); i++) {
                        int row = i / cols;
                        int col = i % cols;
                        
                        var photo = _capturedPhotos[i].Clone();
                        
                        // Масштабируем фото до нужного размера
                        Mat processedPhoto = new Mat();
                        Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(photoWidth, photoHeight));
                        photo.Dispose();
                        
                        var resultRoi = new Mat(result, new OpenCvSharp.Rect(
                            col * photoWidth, 
                            row * photoHeight, 
                            photoWidth, 
                            photoHeight));
                        
                        processedPhoto.CopyTo(resultRoi);
                        processedPhoto.Dispose();
                    }
                }
                
                // Добавляем дату и название события
                string dateTime = System.DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                Cv2.PutText(result, dateTime, new OpenCvSharp.Point(20, result.Height - 20), 
                            HersheyFonts.HersheyComplexSmall, 1, Scalar.Black, 2);
                
                if (!string.IsNullOrEmpty(_eventName))
                {
                    Cv2.PutText(result, _eventName, new OpenCvSharp.Point(20, 40), 
                                HersheyFonts.HersheyComplexSmall, 1, Scalar.Black, 2);
                }
                
                // Сохраняем результат
                string fileName = $"PhotoBooth_{System.DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                _finalImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "photos", fileName);
                
                Directory.CreateDirectory(Path.GetDirectoryName(_finalImagePath));
                Cv2.ImWrite(_finalImagePath, result);
                
                return result;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowError($"Ошибка при создании коллажа: {ex.Message}");
                });
                
                // В случае ошибки возвращаем пустое изображение
                // Всегда используем фиксированные размеры итогового изображения
                int width = 1200;
                int height = 1800;
                return new Mat(height, width, MatType.CV_8UC3, Scalar.White);
            }
        }
    }
} 