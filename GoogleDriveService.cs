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
        public async Task<UploadResult> UploadPhotoAsync(string filePath, string folderName, string eventFolderId = null, bool isPublic = false, string universalFolderId = null)
        {
            string fileName = Path.GetFileName(filePath);
            
            if (_isOnline)
            {
                try
                {
                    // Попытка загрузки в Google Drive
                    return await UploadFileToGoogleDriveAsync(filePath, fileName, folderName, eventFolderId, isPublic, universalFolderId);
                }
                catch
                {
                    // В случае ошибки переходим в офлайн-режим
                    _isOnline = false;
                }
            }

            // Сохранение файла локально и возврат локального QR-кода
            return await SaveLocalFileAsync(filePath, folderName);
        }

        // Загрузка видео
        public async Task<UploadResult> UploadVideoAsync(string filePath, string folderName, string eventFolderId = null, bool isPublic = false, string universalFolderId = null)
        {
            string fileName = Path.GetFileName(filePath);
            
            if (_isOnline)
            {
                try
                {
                    // Попытка загрузки в Google Drive
                    return await UploadFileToGoogleDriveAsync(filePath, fileName, folderName, eventFolderId, isPublic, universalFolderId);
                }
                catch
                {
                    // В случае ошибки переходим в офлайн-режим
                    _isOnline = false;
                }
            }

            // Сохранение файла локально и возврат локального QR-кода
            return await SaveLocalFileAsync(filePath, folderName);
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
            var barcodeWriter = new BarcodeWriter<Bitmap>
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Height = 300,
                    Width = 300,
                    Margin = 1,
                },
                Renderer = new BitmapRenderer()
            };
            
            barcodeWriter.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.H);
            
            return barcodeWriter.Write(content);
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