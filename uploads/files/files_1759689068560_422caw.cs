using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace testdemo
{
    public partial class App : Application
    {
        // 将字段声明为可为null
        private MainWindow? _mainWindow;
        private SecondWindow? _secondWindow;
        public static AppSettings CurrentSettings { get; private set; }
        private Settings? _settingsWindow;
        private NumberSettingsWindow? _numberSettingsWindow;
        // 添加一个公共属性来暴露 SecondWindow 实例
        public SecondWindow? SecondWindowInstance => _secondWindow;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 检查是否已经有testdemo进程在运行
            if (IsAnotherInstanceRunning())
            {
                Shutdown();
                return;
            }

            // 直接初始化主应用
            InitializeMainApplication();

            // 创建并显示作业窗口
            var homeworkWindow = new HomeworkWindow();
            homeworkWindow.Show();
            homeworkWindow.EnsureVisible(); // 确保窗口可见

            // 创建并显示早晚读窗口
            var readingWindow = new ReadingWindow();
            readingWindow.Show();
            readingWindow.EnsureVisible(); // 确保窗口可见

            // 注册全局事件处理器来修复ScrollViewer问题
            EventManager.RegisterClassHandler(typeof(ScrollViewer),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnScrollViewerLoaded));
        }

        private bool IsAnotherInstanceRunning()
        {
            try
            {
                // 获取当前进程
                Process currentProcess = Process.GetCurrentProcess();
                string currentProcessName = currentProcess.ProcessName;

                // 获取所有同名进程
                Process[] processes = Process.GetProcessesByName(currentProcessName);

                // 如果找到超过1个进程（包括当前进程），说明已经有其他实例在运行
                return processes.Length > 1;
            }
            catch (Exception ex)
            {
                // 如果检查失败，默认允许启动
                Console.WriteLine($"进程检查失败: {ex.Message}");
                return false;
            }
        }

        private void InitializeMainApplication()
        {
            InitializeClassManagerWindow();
            // 初始化闹钟调试窗口（后台运行）
            var alarmDebugWindow = new AlarmDebugWindow();
            alarmDebugWindow.WindowState = WindowState.Minimized;
            alarmDebugWindow.ShowInTaskbar = false;
            alarmDebugWindow.Visibility = Visibility.Hidden;
            alarmDebugWindow.Show();

            // 加载设置
            var settings = DataService.LoadSettings();
            App.CurrentSettings = settings;

            // 确保StudentList不为null
            if (settings.StudentList == null)
            {
                settings.StudentList = new Dictionary<int, StudentInfo>();
            }

            // 初始化主窗口
            _mainWindow = new MainWindow();

            // 应用主题颜色到主窗口
            _mainWindow.UpdateThemeColor(settings.ThemeColor.ToMediaColor());

            // 初始化第二个窗口，并立即加载学生数据
            _secondWindow = new SecondWindow(_mainWindow);
            _secondWindow.UpdateThemeColor(settings.ThemeColor.ToMediaColor());
            _secondWindow.LoadStudentData(settings.StudentList);

            // 历史记录已经在 DrawHistoryManager 构造函数中自动加载
            _secondWindow.LoadStudentData(settings.StudentList);

            // 初始化设置窗口
            _settingsWindow = new Settings();
            _settingsWindow.LoadStudentDataFromSettings();

            // 事件订阅
            if (_mainWindow != null)
            {
                _mainWindow.OnStartClicked -= ShowSecondWindow;
                _mainWindow.OnStartClicked += ShowSecondWindow;

                _mainWindow.OnSettingsClicked -= ShowSettingsWindow;
                _mainWindow.OnSettingsClicked += ShowSettingsWindow;
            }

            // 直接显示主窗口
            _mainWindow?.Show();
        }

        private void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer &&
                scrollViewer.Name == "historyScrollViewer")
            {
                // 延迟执行，确保模板已加载
                scrollViewer.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FixScrollViewerTemplate(scrollViewer);
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        private void FixScrollViewerTemplate(ScrollViewer scrollViewer)
        {
            try
            {
                // 获取垂直滚动条
                var scrollBar = scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
                if (scrollBar != null)
                {
                    // 获取主题颜色
                    var themeColor = CurrentSettings?.ThemeColor.ToMediaColor() ?? Colors.Blue;
                    var darkerColor = Color.FromArgb(150, themeColor.R, themeColor.G, themeColor.B);

                    // 找到Thumb并设置颜色
                    scrollBar.ApplyTemplate();
                    var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
                    if (track?.Thumb != null)
                    {
                        track.Thumb.Background = new SolidColorBrush(darkerColor);
                    }
                }
            }
            catch
            {
                // 静默处理异常，避免影响主程序运行
            }
        }

        public static void SaveAppSettings()
        {
            DataService.SaveSettings(CurrentSettings);
        }

        private void ShowSecondWindow()
        {
            if (_secondWindow != null)
            {
                // 直接使用已经创建的实例，不要重新创建
                _secondWindow.SetState(SecondWindow.WindowState.Start);
            }
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow == null || !_settingsWindow.IsVisible)
            {
                _settingsWindow = new Settings();
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _secondWindow?.Close();
            _settingsWindow?.Close();
            _numberSettingsWindow?.Close();
            base.OnExit(e);
        }

        private void InitializeClassManagerWindow()
        {
            try
            {
                // 创建班级管理窗口
                var classManagerWindow = new ClassManagerWindow();

                // 可以根据需要设置窗口属性
                // classManagerWindow.WindowState = WindowState.Normal; // 正常显示
                // 或者隐藏：classManagerWindow.Hide();

                // 显示窗口
                classManagerWindow.Show();

                Console.WriteLine("班级管理系统窗口启动成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"班级管理系统窗口启动失败: {ex.Message}");
            }
        }
    }
}