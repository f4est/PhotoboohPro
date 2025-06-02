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
            
            // Всегда применяем растягивание, независимо от выбранного режима
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
                    
                    // Обрабатываем фото в соответствии с настройками
                    Mat processedFrame = AdjustAspectRatioByMode(frame, 1200.0 / 1800.0, SettingsWindow.AppSettings.PhotoProcessingMode);
                    
                    // Сохраняем фотографию
                    var photo = processedFrame.Clone();
                    _capturedPhotos.Add(photo);
                    
                    if (processedFrame != frame)
                    {
                        processedFrame.Dispose();
                    }
                    
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
            
            // Определяем ориентацию фотографии и целевые размеры
            bool isVerticalOrientation = SettingsWindow.AppSettings.RotationMode == "90° вправо (вертикально)" || 
                                         SettingsWindow.AppSettings.RotationMode == "90° влево (вертикально)";
            
            // Стандартные размеры 1200x1800 (горизонтальный) или 1800x1200 (вертикальный)
            double standardWidth = isVerticalOrientation ? 1800 : 1200;
            double standardHeight = isVerticalOrientation ? 1200 : 1800;
            
            // Получаем размеры исходной фотографии
            double srcWidth = photo.Width;
            double srcHeight = photo.Height;
            double srcAspectRatio = srcWidth / srcHeight;
            
            // Целевое соотношение сторон
            double targetAspectRatio = targetWidth / targetHeight;
            double standardAspectRatio = standardWidth / standardHeight;
            
            // Применяем выбранный режим обработки
            Mat processedPhoto;
            
            switch (SettingsWindow.AppSettings.PhotoProcessingMode)
            {
                case ImageProcessingMode.Stretch:
                    // Режим растягивания - растягиваем до целевого размера
                    processedPhoto = new Mat();
                    Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(targetWidth, targetHeight));
                    break;
                    
                case ImageProcessingMode.Crop:
                    // Режим обрезки - сначала обрезаем до правильных пропорций, затем масштабируем
                    if (Math.Abs(srcAspectRatio - standardAspectRatio) > 0.01)
                    {
                        Mat croppedPhoto;
                        
                        if (srcAspectRatio > standardAspectRatio) // Фото шире, чем нужно
                        {
                            // Обрезаем по бокам
                            int cropWidth = (int)(srcHeight * standardAspectRatio);
                            int xOffset = (int)((srcWidth - cropWidth) / 2);
                            var roi = new Rect(xOffset, 0, cropWidth, (int)srcHeight);
                            croppedPhoto = new Mat(photo, roi);
                        }
                        else // Фото выше, чем нужно
                        {
                            // Обрезаем сверху и снизу
                            int cropHeight = (int)(srcWidth / standardAspectRatio);
                            int yOffset = (int)((srcHeight - cropHeight) / 2);
                            var roi = new Rect(0, yOffset, (int)srcWidth, cropHeight);
                            croppedPhoto = new Mat(photo, roi);
                        }
                        
                        // Теперь масштабируем до целевого размера
                        processedPhoto = new Mat();
                        Cv2.Resize(croppedPhoto, processedPhoto, new OpenCvSharp.Size(targetWidth, targetHeight));
                        croppedPhoto.Dispose();
                    }
                    else
                    {
                        // Если соотношения сторон уже соответствуют, просто масштабируем
                        processedPhoto = new Mat();
                        Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(targetWidth, targetHeight));
                    }
                    break;
                    
                case ImageProcessingMode.Scale:
                    // Режим масштабирования - увеличиваем фото до нужных пропорций, затем масштабируем
                    double scaleX = standardWidth / srcWidth;
                    double scaleY = standardHeight / srcHeight;
                    double scale = Math.Min(scaleX, scaleY); // Берем минимальный масштаб для сохранения пропорций
                    
                    // Масштабируем изображение до стандартных пропорций
                    int scaledWidth = (int)(srcWidth * scale);
                    int scaledHeight = (int)(srcHeight * scale);
                    
                    Mat scaledPhoto = new Mat();
                    Cv2.Resize(photo, scaledPhoto, new OpenCvSharp.Size(scaledWidth, scaledHeight));
                    
                    // Теперь масштабируем до целевого размера с заполнением
                    scaleX = targetWidth / scaledWidth;
                    scaleY = targetHeight / scaledHeight;
                    scale = Math.Max(scaleX, scaleY); // Берем максимальный масштаб для заполнения области
                    
                    int finalWidth = (int)(scaledWidth * scale);
                    int finalHeight = (int)(scaledHeight * scale);
                    
                    Mat enlargedPhoto = new Mat();
                    Cv2.Resize(scaledPhoto, enlargedPhoto, new OpenCvSharp.Size(finalWidth, finalHeight));
                    scaledPhoto.Dispose();
                    
                    // Если размер больше целевого, обрезаем до целевого размера
                    if (finalWidth > targetWidth || finalHeight > targetHeight)
                    {
                        int xOffset = (finalWidth - (int)targetWidth) / 2;
                        int yOffset = (finalHeight - (int)targetHeight) / 2;
                        
                        var roi = new Rect(xOffset, yOffset, (int)targetWidth, (int)targetHeight);
                        processedPhoto = new Mat(enlargedPhoto, roi);
                        enlargedPhoto.Dispose();
                    }
                    else
                    {
                        // Если изображение меньше целевого (что маловероятно), используем его
                        processedPhoto = enlargedPhoto;
                    }
                    break;
                    
                default:
                    // По умолчанию используем режим растягивания
                    processedPhoto = new Mat();
                    Cv2.Resize(photo, processedPhoto, new OpenCvSharp.Size(targetWidth, targetHeight));
                    break;
            }
            
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
            // Загружаем изображение
            BitmapImage bitmap = new BitmapImage(new Uri(imagePath));
            
            // Получаем размеры страницы принтера
            double printWidth = SettingsWindow.AppSettings.PrintWidth;
            double printHeight = SettingsWindow.AppSettings.PrintHeight;
            
            // Создаем контейнер для изображения
            System.Windows.Controls.Canvas canvas = new System.Windows.Controls.Canvas();
            canvas.Width = ConvertCmToPixels(printWidth);
            canvas.Height = ConvertCmToPixels(printHeight);
            canvas.Background = System.Windows.Media.Brushes.White;
            
            // Создаем изображение для печати
            System.Windows.Controls.Image printImage = new System.Windows.Controls.Image();
            printImage.Source = bitmap;
            
            // Определяем ориентацию изображения
            bool isImageVertical = bitmap.PixelHeight > bitmap.PixelWidth;
            
            // Применяем корректную ориентацию
            if (isImageVertical)
            {
                // Вертикальное изображение
                printImage.Width = canvas.Width;
                printImage.Height = canvas.Height;
                printImage.Stretch = Stretch.Uniform;
            }
            else
            {
                // Горизонтальное изображение
                printImage.Width = canvas.Width;
                printImage.Height = canvas.Height;
                printImage.Stretch = Stretch.Uniform;
            }
            
            canvas.Children.Add(printImage);
            
            // Печать
            printDialog.PrintVisual(canvas, "PhotoboothPro - Печать");
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
                
                // Определяем ориентацию на основе настроек
                bool isVerticalOrientation = SettingsWindow.AppSettings.RotationMode == "90° вправо (вертикально)" || 
                                             SettingsWindow.AppSettings.RotationMode == "90° влево (вертикально)";
                
                // Стандартные размеры 1200x1800 (горизонтальный) или 1800x1200 (вертикальный)
                int finalWidth = isVerticalOrientation ? 1800 : 1200;
                int finalHeight = isVerticalOrientation ? 1200 : 1800;
                
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
                    
                    // Если позиций меньше, чем фотографий, используем только доступные позиции
                    int photoCount = Math.Min(_capturedPhotos.Count, positions.Count);
                    
                    for (int i = 0; i < photoCount; i++)
                    {
                        if (i >= _capturedPhotos.Count) break;
                        
                        var photo = _capturedPhotos[i].Clone(); // Клонируем для безопасности операций
                        var pos = positions[i];
                        
                        // Обрабатываем фото в соответствии с режимом обработки
                        Mat processedPhoto = ProcessPhoto(photo, pos.Width, pos.Height);
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
                    double photoWidth, photoHeight;
                    
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
                        
                        // Обрабатываем фото в соответствии с режимом обработки
                        Mat processedPhoto = ProcessPhoto(photo, photoWidth, photoHeight);
                        photo.Dispose();
                        
                        var resultRoi = new Mat(result, new OpenCvSharp.Rect(
                            (int)(col * photoWidth), 
                            (int)(row * photoHeight), 
                            (int)photoWidth, 
                            (int)photoHeight));
                        
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
                bool isVerticalOrientation = SettingsWindow.AppSettings.RotationMode == "90° вправо (вертикально)" || 
                                             SettingsWindow.AppSettings.RotationMode == "90° влево (вертикально)";
                int width = isVerticalOrientation ? 1800 : 1200;
                int height = isVerticalOrientation ? 1200 : 1800;
                return new Mat(height, width, MatType.CV_8UC3, Scalar.White);
            }
        }
    }
} 