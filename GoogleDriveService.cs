using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using System.Drawing;
using QRCoder;
using Google.Apis.Util.Store;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace UnifiedPhotoBooth
{
    public class GoogleDriveService
    {
        private const string SERVICE_ACCOUNT_FILE = "photoboothproject-459010-c725b2899f7f.json";
        private const string EVENTS_FOLDER_ID = "1oHDqcrZnRcnNCDwGsifDSGVQZjYqtf1S";
        private const string UNIVERSAL_FOLDER_ID = "1FR92J38OPdLZoCaKKZ6lW7EucdGtG624";

        private DriveService _driveService;
        private bool _isOnline;
        private const string _offlineEventsFile = "offline_events.txt";
        
        // Публичное свойство для проверки наличия интернета
        public bool IsOnline => _isOnline;

        public GoogleDriveService()
        {
            InitializeService();
        }

        private void InitializeService()
        {
            try
            {
                // Проверяем наличие интернета
                _isOnline = CheckForInternetConnection();

                if (_isOnline)
                {
                    string[] scopes = { DriveService.Scope.Drive };
                    string applicationName = "PhotoBooth Application";
                    string credentialsPath = "photoboothproject-459010-c725b2899f7f.json";

                    // Проверяем, существует ли файл с учетными данными
                    if (!System.IO.File.Exists(credentialsPath))
                    {
                        // Работаем в автономном режиме, если нет файла credentials
                        _isOnline = false;
                        return;
                    }

                    // Аутентификация с использованием учетных данных службы
                    GoogleCredential credential;
                    using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                    {
                        credential = GoogleCredential.FromStream(stream).CreateScoped(scopes);
                    }

                    // Создание службы Drive API
                    _driveService = new DriveService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = applicationName,
                    });
                }
            }
            catch
            {
                // В случае ошибки установки соединения переходим в офлайн-режим
                _isOnline = false;
            }
        }

        // Проверка наличия интернет-соединения
        private bool CheckForInternetConnection()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 2000);
                    return reply?.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        // Получение списка событий
        public Dictionary<string, string> ListEvents()
        {
            var events = new Dictionary<string, string>();

            try
            {
                if (_isOnline)
                {
                    // Пытаемся получить только события из папки Events в Google Drive
                    var request = _driveService.Files.List();
                    request.Q = $"'{EVENTS_FOLDER_ID}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false";
                    request.Fields = "files(id, name)";

                    var result = request.Execute();
                    if (result.Files != null)
                    {
                        foreach (var file in result.Files)
                        {
                            events.Add(file.Name, file.Id);
                        }
                    }
                }
                else
                {
                    // В офлайн режиме загружаем события из локального файла
                    LoadOfflineEvents(events);
                }
            }
            catch
            {
                // В случае ошибки переходим в офлайн-режим
                _isOnline = false;
                LoadOfflineEvents(events);
            }

            return events;
        }

        // Загрузка списка событий из локального файла
        private void LoadOfflineEvents(Dictionary<string, string> events)
        {
            try
            {
                if (System.IO.File.Exists(_offlineEventsFile))
                {
                    var lines = System.IO.File.ReadAllLines(_offlineEventsFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length == 2)
                        {
                            events.Add(parts[0], parts[1]);
                        }
                    }
                }

                // Если нет событий, добавляем одно локальное
                if (events.Count == 0)
                {
                    string localEventId = Guid.NewGuid().ToString();
                    events.Add("Локальное событие", localEventId);
                    SaveOfflineEvent("Локальное событие", localEventId);
                }
            }
            catch
            {
                // В случае ошибки добавляем базовое локальное событие
                string localEventId = Guid.NewGuid().ToString();
                events.Add("Локальное событие", localEventId);
            }
        }

        // Сохранение события в локальный файл
        private void SaveOfflineEvent(string eventName, string eventId)
        {
            try
            {
                System.IO.File.AppendAllText(_offlineEventsFile, $"{eventName}|{eventId}\n");
            }
            catch
            {
                // Игнорируем ошибки записи
            }
        }

        // Создание нового события
        public string CreateEvent(string eventName)
        {
            if (_isOnline)
            {
                try
                {
                    // Создаем папку в Google Drive
                    var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = eventName,
                        MimeType = "application/vnd.google-apps.folder"
                    };

                    var request = _driveService.Files.Create(fileMetadata);
                    request.Fields = "id";
                    var file = request.Execute();
                    
                    return file.Id;
                }
                catch
                {
                    // В случае ошибки переходим в офлайн-режим
                    _isOnline = false;
                }
            }

            // Создаем локальное событие
            string localEventId = Guid.NewGuid().ToString();
            SaveOfflineEvent(eventName, localEventId);
            return localEventId;
        }

        // Загрузка фото
        public async Task<UploadResult> UploadPhotoAsync(string filePath, string folderName, string eventFolderId, bool useLastFolder = false, string lastUniversalFolderId = null)
        {
            if (!_isOnline)
            {
                return await SaveLocalFileAsync(filePath, folderName);
            }
            
            try
            {
                string fileName = Path.GetFileName(filePath);
                
                // Создаем уникальную индивидуальную папку для фото
                var individualFolderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = $"{Path.GetFileNameWithoutExtension(fileName)}_{System.DateTime.Now:yyyyMMdd_HHmmss}",
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { UNIVERSAL_FOLDER_ID }
                };
                
                var folderRequest = _driveService.Files.Create(individualFolderMetadata);
                folderRequest.Fields = "id";
                var folder = await Task.Run(() => folderRequest.Execute());
                string folderId = folder.Id;
                
                // Загружаем файл в индивидуальную папку
                var fileMetadataUpload = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = fileName,
                    Parents = new List<string> { folderId }
                };
                
                string universalFileId;
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var uploadRequest = _driveService.Files.Create(fileMetadataUpload, stream, "");
                    uploadRequest.Fields = "id, webContentLink";
                    await Task.Run(() => uploadRequest.Upload());
                    
                    var uploadedFile = uploadRequest.ResponseBody;
                    universalFileId = uploadedFile.Id;
                    
                    // Устанавливаем публичные разрешения на файл
                    var permissionRequest = _driveService.Permissions.Create(
                        new Google.Apis.Drive.v3.Data.Permission()
                        {
                            Type = "anyone",
                            Role = "reader"
                        },
                        uploadedFile.Id);
                    await Task.Run(() => permissionRequest.Execute());
                    
                    // Устанавливаем публичные разрешения на папку
                    var folderPermissionRequest = _driveService.Permissions.Create(
                        new Google.Apis.Drive.v3.Data.Permission()
                        {
                            Type = "anyone",
                            Role = "reader"
                        },
                        folderId);
                    await Task.Run(() => folderPermissionRequest.Execute());
                }
                
                // Также копируем файл в общую папку события, если она задана
                if (!string.IsNullOrEmpty(eventFolderId))
                {
                    var eventFileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = fileName,
                        Parents = new List<string> { eventFolderId }
                    };
                    
                    using (var stream = new FileStream(filePath, FileMode.Open))
                    {
                        var eventUploadRequest = _driveService.Files.Create(eventFileMetadata, stream, "");
                        await Task.Run(() => eventUploadRequest.Upload());
                    }
                }
                
                // Создаем QR-код для папки, а не для отдельного файла
                string qrUrl = $"https://drive.google.com/drive/folders/{folderId}";
                Bitmap qrCode = GenerateQrCode(qrUrl);
                
                // Сохраняем QR-код в локальный файл
                string qrFilePath = SaveQrCodeToFile(qrCode, filePath, fileName);
                
                // Загружаем QR-код в ту же папку Google Drive
                var qrFileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(qrFilePath),
                    Parents = new List<string> { folderId }
                };
                
                using (var stream = new FileStream(qrFilePath, FileMode.Open))
                {
                    var qrUploadRequest = _driveService.Files.Create(qrFileMetadata, stream, "image/png");
                    await Task.Run(() => qrUploadRequest.Upload());
                }
                
                return new UploadResult
                {
                    FileId = universalFileId,
                    FolderId = folderId,
                    QrCode = qrCode,
                    Url = qrUrl
                };
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                Console.WriteLine($"Ошибка при загрузке фото: {ex.Message}");
                
                // В случае ошибки создаем QR-код с сообщением об ошибке
                Bitmap qrCode = GenerateQrCode("Ошибка загрузки: " + ex.Message.Substring(0, Math.Min(50, ex.Message.Length)));
                
                return new UploadResult
                {
                    FileId = null,
                    FolderId = null,
                    QrCode = qrCode,
                    Url = "Ошибка загрузки"
                };
            }
        }

        // Загрузка видео
        public async Task<UploadResult> UploadVideoAsync(string filePath, string folderName, string eventFolderId, bool useLastFolder = false, string lastUniversalFolderId = null)
        {
            if (!_isOnline)
            {
                return await SaveLocalFileAsync(filePath, folderName);
            }
            
            try
            {
                string fileName = Path.GetFileName(filePath);
                
                // Создаем уникальную индивидуальную папку для видео
                var individualFolderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = $"{Path.GetFileNameWithoutExtension(fileName)}_{System.DateTime.Now:yyyyMMdd_HHmmss}",
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { UNIVERSAL_FOLDER_ID }
                };
                
                var folderRequest = _driveService.Files.Create(individualFolderMetadata);
                folderRequest.Fields = "id";
                var folder = await Task.Run(() => folderRequest.Execute());
                string folderId = folder.Id;
                
                // Загружаем файл в индивидуальную папку
                var fileMetadataUpload = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = fileName,
                    Parents = new List<string> { folderId }
                };
                
                string universalFileId;
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var uploadRequest = _driveService.Files.Create(fileMetadataUpload, stream, "video/mp4");
                    uploadRequest.Fields = "id, webContentLink";
                    await Task.Run(() => uploadRequest.Upload());
                    
                    var uploadedFile = uploadRequest.ResponseBody;
                    universalFileId = uploadedFile.Id;
                    
                    // Устанавливаем публичные разрешения на файл
                    var permissionRequest = _driveService.Permissions.Create(
                        new Google.Apis.Drive.v3.Data.Permission()
                        {
                            Type = "anyone",
                            Role = "reader"
                        },
                        uploadedFile.Id);
                    await Task.Run(() => permissionRequest.Execute());
                    
                    // Устанавливаем публичные разрешения на папку
                    var folderPermissionRequest = _driveService.Permissions.Create(
                        new Google.Apis.Drive.v3.Data.Permission()
                        {
                            Type = "anyone",
                            Role = "reader"
                        },
                        folderId);
                    await Task.Run(() => folderPermissionRequest.Execute());
                }
                
                // Также копируем файл в общую папку события, если она задана
                if (!string.IsNullOrEmpty(eventFolderId))
                {
                    var eventFileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = fileName,
                        Parents = new List<string> { eventFolderId }
                    };
                    
                    using (var stream = new FileStream(filePath, FileMode.Open))
                    {
                        var eventUploadRequest = _driveService.Files.Create(eventFileMetadata, stream, "video/mp4");
                        await Task.Run(() => eventUploadRequest.Upload());
                    }
                }
                
                // Создаем QR-код для папки, а не для отдельного файла
                string qrUrl = $"https://drive.google.com/drive/folders/{folderId}";
                Bitmap qrCode = GenerateQrCode(qrUrl);
                
                // Сохраняем QR-код в локальный файл
                string qrFilePath = SaveQrCodeToFile(qrCode, filePath, fileName);
                
                // Загружаем QR-код в ту же папку Google Drive
                var qrFileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(qrFilePath),
                    Parents = new List<string> { folderId }
                };
                
                using (var stream = new FileStream(qrFilePath, FileMode.Open))
                {
                    var qrUploadRequest = _driveService.Files.Create(qrFileMetadata, stream, "image/png");
                    await Task.Run(() => qrUploadRequest.Upload());
                }
                
                return new UploadResult
                {
                    FileId = universalFileId,
                    FolderId = folderId,
                    QrCode = qrCode,
                    Url = qrUrl
                };
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                Console.WriteLine($"Ошибка при загрузке видео: {ex.Message}");
                
                // В случае ошибки создаем QR-код с сообщением об ошибке
                Bitmap qrCode = GenerateQrCode("Ошибка загрузки: " + ex.Message.Substring(0, Math.Min(50, ex.Message.Length)));
                
                return new UploadResult
                {
                    FileId = null,
                    FolderId = null,
                    QrCode = qrCode,
                    Url = "Ошибка загрузки"
                };
            }
        }

        // Загрузка файла в Google Drive
        private async Task<UploadResult> UploadFileToGoogleDriveAsync(string filePath, string fileName, string folderName, string eventFolderId, bool isPublic, string universalFolderId)
        {
            // Создаем идентификатор файла для отслеживания успешной загрузки
            string fileId = null;
            string folderId = null;
            
            try {
                // Загрузка в папку события, если eventFolderId указан
                if (!string.IsNullOrEmpty(eventFolderId))
                {
                    var eventFileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = fileName,
                        Parents = new List<string> { eventFolderId }
                    };
                    
                    using (var stream = new FileStream(filePath, FileMode.Open))
                    {
                        var eventUploadRequest = _driveService.Files.Create(eventFileMetadata, stream, "");
                        eventUploadRequest.Fields = "id, webContentLink";
                        await Task.Run(() => eventUploadRequest.Upload());
                        
                        // Сохраняем идентификатор файла для возможного удаления при ошибке
                        fileId = eventUploadRequest.ResponseBody.Id;
                    }
                }
                
                // Создаем индивидуальную папку в Universal Videos для этого файла
                var individualFolderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = folderName,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { UNIVERSAL_FOLDER_ID }
                };
                
                var folderRequest = _driveService.Files.Create(individualFolderMetadata);
                folderRequest.Fields = "id";
                var folder = await Task.Run(() => folderRequest.Execute());
                folderId = folder.Id;
                
                // Загружаем файл в индивидуальную папку
                var fileMetadataUpload = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = fileName,
                    Parents = new List<string> { folderId }
                };
                
                string universalFileId;
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var uploadRequest = _driveService.Files.Create(fileMetadataUpload, stream, "");
                    uploadRequest.Fields = "id, webContentLink";
                    await Task.Run(() => uploadRequest.Upload());
                    
                    var uploadedFile = uploadRequest.ResponseBody;
                    universalFileId = uploadedFile.Id;
                    
                    // Если файл должен быть публичным, установим соответствующие разрешения
                    if (isPublic)
                    {
                        var permissionRequest = _driveService.Permissions.Create(
                            new Google.Apis.Drive.v3.Data.Permission()
                            {
                                Type = "anyone",
                                Role = "reader"
                            },
                            uploadedFile.Id);
                        await Task.Run(() => permissionRequest.Execute());
                    }
                    
                    // Создаем ссылку для QR-кода
                    string qrUrl = $"https://drive.google.com/file/d/{uploadedFile.Id}/view";
                    Bitmap qrCode = GenerateQrCode(qrUrl);
                    
                    // Сохранение QR-кода в файл
                    string qrFilePath = SaveQrCodeToFile(qrCode, filePath, fileName);
                    
                    // Возвращаем результат загрузки
                    return new UploadResult
                    {
                        FileId = uploadedFile.Id,
                        FolderId = folderId,
                        QrCode = qrCode,
                        Url = qrUrl
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при загрузке в Google Drive: {ex.Message}");
                
                // Попытка очистить ресурсы при ошибке
                if (!string.IsNullOrEmpty(fileId))
                {
                    try
                    {
                        await Task.Run(() => _driveService.Files.Delete(fileId).Execute());
                    }
                    catch { /* Игнорируем ошибки при очистке */ }
                }
                
                if (!string.IsNullOrEmpty(folderId))
                {
                    try
                    {
                        await Task.Run(() => _driveService.Files.Delete(folderId).Execute());
                    }
                    catch { /* Игнорируем ошибки при очистке */ }
                }
                
                throw; // Пробрасываем исключение дальше
            }
        }

        // Сохранение файла локально
        private async Task<UploadResult> SaveLocalFileAsync(string filePath, string folderName)
        {
            try
            {
                // Создаем директорию для локального хранения, если она еще не существует
                string localStorageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LocalStorage");
                string eventDir = Path.Combine(localStorageDir, folderName);
                
                if (!Directory.Exists(localStorageDir))
                    Directory.CreateDirectory(localStorageDir);
                
                if (!Directory.Exists(eventDir))
                    Directory.CreateDirectory(eventDir);
                
                // Копируем файл в локальное хранилище
                string fileName = Path.GetFileName(filePath);
                string targetPath = Path.Combine(eventDir, fileName);
                
                await Task.Run(() => System.IO.File.Copy(filePath, targetPath, true));
                
                // Создаем QR-код с локальным путем
                string localUrl = $"file:///{targetPath.Replace('\\', '/')}";
                Bitmap qrCode = GenerateQrCode(localUrl);
                
                // Сохранение QR-кода в файл
                string qrFilePath = SaveQrCodeToFile(qrCode, filePath, fileName);
                
                // Возвращаем результат с локальным путем
                return new UploadResult
                {
                    FileId = Guid.NewGuid().ToString(),
                    FolderId = folderName,
                    QrCode = qrCode,
                    Url = localUrl
                };
            }
            catch
            {
                // В случае ошибки создаем пустой QR-код
                Bitmap qrCode = GenerateQrCode("Локальное сохранение не удалось");
                
                return new UploadResult
                {
                    FileId = Guid.NewGuid().ToString(),
                    FolderId = folderName,
                    QrCode = qrCode,
                    Url = "Файл сохранен локально"
                };
            }
        }

        // Генерация QR-кода
        private Bitmap GenerateQrCode(string content)
        {
            // Получаем настройки QR-кода из SettingsWindow
            var appSettings = SettingsWindow.AppSettings;
            
            // Создаем генератор QR-кода
            QRCoder.QRCodeGenerator qrGenerator = new QRCoder.QRCodeGenerator();
            QRCoder.QRCodeData qrCodeData = qrGenerator.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.H);
            QRCoder.QRCode qrCode = new QRCoder.QRCode(qrCodeData);
            
            // Преобразуем цвета из строк в объекты цветов
            System.Drawing.Color qrForeColor = System.Drawing.ColorTranslator.FromHtml(appSettings.QrForegroundColor);
            System.Drawing.Color qrBackColor = System.Drawing.ColorTranslator.FromHtml(appSettings.QrBackgroundColor);
            
            // Создаем QR-код с настраиваемыми цветами
            Bitmap qrBitmap = qrCode.GetGraphic(20, qrForeColor, qrBackColor, false);
            
            // Изменяем размер QR-кода до настраиваемого
            int size = appSettings.QrCodeSize;
            Bitmap resizedQrBitmap = new Bitmap(size, size);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(resizedQrBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(qrBitmap, new System.Drawing.Rectangle(0, 0, size, size));
            }
            
            // Если указан путь к логотипу и файл существует, добавляем логотип в центр QR-кода
            string logoPath = appSettings.QrLogoPath;
            if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
            {
                try
                {
                    // Загружаем логотип
                    Bitmap logo = new Bitmap(logoPath);
                    
                    // Определяем размер логотипа в процентах от размера QR-кода
                    int logoSizePercent = appSettings.QrLogoSize;
                    int logoWidth = (int)(size * logoSizePercent / 100.0);
                    int logoHeight = (int)(size * logoSizePercent / 100.0);
                    
                    // Масштабируем логотип до нужного размера
                    Bitmap resizedLogo = new Bitmap(logo, new System.Drawing.Size(logoWidth, logoHeight));
                    
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
                    Console.WriteLine($"Ошибка при добавлении логотипа: {ex.Message}");
                }
            }
            
            return resizedQrBitmap;
        }
        
        // Сохранение QR-кода в файл
        private string SaveQrCodeToFile(Bitmap qrCode, string basePath, string fileName)
        {
            // Создаем путь для QR-кода в той же директории, что и оригинальный файл
            string qrFilePath = Path.Combine(Path.GetDirectoryName(basePath), 
                                           Path.GetFileNameWithoutExtension(fileName) + "_qr.png");
            
            // Сохраняем QR-код в файл
            qrCode.Save(qrFilePath, System.Drawing.Imaging.ImageFormat.Png);
            
            return qrFilePath;
        }
    }

    public class UploadResult
    {
        public string FileId { get; set; }
        public string FolderId { get; set; }
        public Bitmap QrCode { get; set; }
        public string Url { get; set; }
    }
} 