using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace UnifiedPhotoBooth
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Установка имени приложения для использования в других частях кода
            AppDomain.CurrentDomain.SetData("AppName", "PhotoboothPro");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Устанавливаем кодировку UTF-8 по умолчанию для корректного отображения русских символов
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding.UTF8.GetEncoder();
            
            // Проверяем и создаем необходимые директории
            CreateRequiredDirectories();
            
            // Инициализация OpenCvSharp
            InitializeOpenCvSharp();
            
            // Настраиваем глобальную обработку необработанных исключений
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                MessageBox.Show($"Произошла непредвиденная ошибка: {exception?.Message}\n\nDetails: {exception?.StackTrace}", 
                               "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            
            Application.Current.DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Произошла непредвиденная ошибка: {args.Exception.Message}\n\nDetails: {args.Exception.StackTrace}", 
                               "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
        
        private void CreateRequiredDirectories()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] requiredDirs = new[]
                {
                    Path.Combine(baseDir, "photos"),
                    Path.Combine(baseDir, "recordings"),
                    Path.Combine(baseDir, "recordings", "temp"),
                    Path.Combine(baseDir, "frames")
                };
                
                foreach (var dir in requiredDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                
                // Проверяем наличие ffmpeg
                string ffmpegPath = Path.Combine(baseDir, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show("Файл ffmpeg.exe не найден в директории приложения. " +
                        "Некоторые функции обработки видео могут работать некорректно.", 
                        "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании рабочих директорий: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void InitializeOpenCvSharp()
        {
            try
            {
                // Проверяем наличие необходимых DLL в каталоге приложения
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string opencvDll = Path.Combine(basePath, "OpenCvSharpExtern.dll");
                
                if (!File.Exists(opencvDll))
                {
                    MessageBox.Show($"Не найдена библиотека OpenCvSharpExtern.dll в каталоге {basePath}.",
                                   "Ошибка инициализации OpenCV", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Тестовая инициализация OpenCV
                using (var mat = new Mat(1, 1, MatType.CV_8UC1))
                {
                    // Если мы дошли до сюда, значит OpenCV успешно инициализирован
                    Console.WriteLine("OpenCV успешно инициализирован.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации OpenCV: {ex.Message}\n\nDetails: {ex.StackTrace}",
                               "Ошибка инициализации OpenCV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 