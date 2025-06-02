using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace UnifiedPhotoBooth
{
    public static class SettingsManager
    {
        // Путь к файлу настроек в папке AppData
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnifiedPhotoBooth");
            
        private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "settings.json");
        
        // Метод для сохранения настроек
        public static bool SaveSettings(AppSettings settings)
        {
            try
            {
                // Создаем директорию, если она не существует
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }
                
                // Настройки сериализации JSON
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                
                // Сериализуем настройки в JSON
                string json = JsonSerializer.Serialize(settings, options);
                
                // Используем атомарную запись через временный файл
                string tempPath = SettingsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                
                // Проверяем что временный файл создан успешно
                if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                {
                    // Заменяем старый файл новым
                    if (File.Exists(SettingsFilePath))
                        File.Delete(SettingsFilePath);
                    
                    File.Move(tempPath, SettingsFilePath);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении настроек: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        // Метод для загрузки настроек
        public static AppSettings LoadSettings()
        {
            try
            {
                // Проверяем существование файла настроек
                if (!File.Exists(SettingsFilePath))
                {
                    // Если файла нет, создаем настройки по умолчанию
                    var defaultSettings = new AppSettings();
                    SaveSettings(defaultSettings); // Сохраняем настройки по умолчанию
                    return defaultSettings;
                }
                
                // Читаем JSON из файла
                string json = File.ReadAllText(SettingsFilePath);
                
                // Проверяем, что JSON не пустой
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppSettings();
                }
                
                // Настройки десериализации
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNameCaseInsensitive = true
                };
                
                // Десериализуем JSON в объект настроек
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                
                // Проверяем результат десериализации
                if (settings == null)
                {
                    return new AppSettings();
                }
                
                // Валидируем настройки
                ValidateSettings(settings);
                
                return settings;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке настроек: {ex.Message}. Будут использованы настройки по умолчанию.", 
                    "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new AppSettings();
            }
        }
        
        // Метод для валидации настроек
        private static void ValidateSettings(AppSettings settings)
        {
            // Проверяем наличие списка позиций
            if (settings.PhotoPositions == null)
            {
                settings.PhotoPositions = new System.Collections.Generic.List<PhotoPosition>();
            }
            
            // Проверяем корректность числовых параметров
            if (settings.PhotoCount <= 0) settings.PhotoCount = 4;
            if (settings.PhotoCountdownTime <= 0) settings.PhotoCountdownTime = 3;
            if (settings.VideoCountdownTime <= 0) settings.VideoCountdownTime = 3;
            if (settings.RecordingDuration <= 0) settings.RecordingDuration = 15;
            
            // Проверяем существование файлов
            if (!string.IsNullOrEmpty(settings.FrameTemplatePath) && !File.Exists(settings.FrameTemplatePath))
            {
                settings.FrameTemplatePath = null;
            }
            
            if (!string.IsNullOrEmpty(settings.OverlayImagePath) && !File.Exists(settings.OverlayImagePath))
            {
                settings.OverlayImagePath = null;
            }
        }
        
        // Метод для получения пути к файлу настроек (для диагностики)
        public static string GetSettingsFilePath()
        {
            return SettingsFilePath;
        }
    }
} 