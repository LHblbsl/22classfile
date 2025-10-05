using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
using System.Linq;

namespace testdemo
{
    // 添加AlarmSettings类定义（与AlarmDebugWindow中相同）
    public class AlarmSettings
    {
        public string AlarmTitle { get; set; } = "休息";
        public int StartHour { get; set; } = 9;
        public int StartMinute { get; set; } = 0;
        public int StartSecond { get; set; } = 0;
        public int EndHour { get; set; } = 17;
        public int EndMinute { get; set; } = 0;
        public int EndSecond { get; set; } = 0;
        public int PrepareTime { get; set; } = 300;
        public int Volume { get; set; } = 50;
        public List<string> MusicFiles { get; set; } = new List<string>();
        public bool IsEnabled { get; set; } = false;
    }
   

    public partial class AlarmDebugWindow : Window
    {
        private AlarmSettings _alarmSettings = new AlarmSettings();
        private DispatcherTimer _alarmCheckTimer;
        private bool _isAlarmActive = false;
        public ObservableCollection<string> MusicFiles { get; set; }
        private DateTime _lastSkippedDate = DateTime.MinValue;
        private DateTime _lastCheckDate = DateTime.MinValue;

        // 音乐文件存储目录
        private string _musicDirectory;

        public AlarmDebugWindow()
        {
            InitializeComponent();
            MusicFiles = new ObservableCollection<string>();
            MusicFilesList.ItemsSource = MusicFiles;

            // 初始化音乐目录
            InitializeMusicDirectory();

            // 加载保存的设置
            LoadAlarmSettings();

            // 初始化闹钟检查定时器
            _alarmCheckTimer = new DispatcherTimer();
            _alarmCheckTimer.Interval = TimeSpan.FromSeconds(1);
            _alarmCheckTimer.Tick += AlarmCheckTimer_Tick;
            _alarmCheckTimer.Start();

            // 设置触屏滚动事件
            if (MainScrollViewer.Template.FindName("DragThumb", MainScrollViewer) is Thumb dragThumb)
            {
                dragThumb.DragDelta += DragThumb_DragDelta;
            }
        }

        // 初始化音乐目录
        private void InitializeMusicDirectory()
        {
            // 创建程序目录下的Music文件夹
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _musicDirectory = Path.Combine(appDirectory, "Music");

            if (!Directory.Exists(_musicDirectory))
            {
                Directory.CreateDirectory(_musicDirectory);
            }
        }

        // 重新读取音乐目录中的文件
        private void ReloadMusicFiles()
        {
            try
            {
                MusicFiles.Clear();

                // 获取音乐目录中的所有wav文件
                var musicFiles = Directory.GetFiles(_musicDirectory, "*.wav");
                foreach (var file in musicFiles)
                {
                    // 只显示文件名，不显示完整路径
                    MusicFiles.Add(Path.GetFileName(file));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取音乐文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportMusicButton_Click(object sender, RoutedEventArgs e)
        {
            // 创建文件选择对话框
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "WAV音频文件 (*.wav)|*.wav";
            openFileDialog.Multiselect = true; // 允许选择多个文件
            openFileDialog.Title = "选择音乐文件";

            if (openFileDialog.ShowDialog() == true)
            {
                int successCount = 0;
                int errorCount = 0;

                foreach (string selectedFile in openFileDialog.FileNames)
                {
                    try
                    {
                        string fileName = Path.GetFileName(selectedFile);
                        string destinationPath = Path.Combine(_musicDirectory, fileName);

                        // 如果文件已存在，询问是否覆盖
                        if (File.Exists(destinationPath))
                        {
                            var result = MessageBox.Show($"文件 {fileName} 已存在，是否覆盖？",
                                "文件已存在",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.No)
                            {
                                // 不覆盖，跳过这个文件
                                continue;
                            }
                        }

                        // 复制文件到音乐目录
                        File.Copy(selectedFile, destinationPath, true);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        Console.WriteLine($"复制文件失败: {ex.Message}");
                    }
                }

                // 重新读取音乐文件
                ReloadMusicFiles();

                // 显示操作结果
                string message = $"成功导入 {successCount} 个文件";
                if (errorCount > 0)
                {
                    message += $"，{errorCount} 个文件导入失败";
                }

                MessageBox.Show(message, "导入完成", MessageBoxButton.OK,
                    errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (MusicFiles.Count > 0)
            {
                var result = MessageBox.Show("确定要清空所有音乐文件吗？这将删除音乐目录中的所有文件！", "确认清空",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // 删除音乐目录中的所有文件
                        var files = Directory.GetFiles(_musicDirectory);
                        foreach (var file in files)
                        {
                            File.Delete(file);
                        }

                        MusicFiles.Clear();
                        MessageBox.Show("已清空所有音乐文件", "清空完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"清空文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("没有可清空的音乐文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // 框架功能：验证输入并保存设置
            if (ValidateInputs())
            {
                SaveAlarmSettings();
                MessageBox.Show("闹钟设置已保存", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TestPlayButton_Click(object sender, RoutedEventArgs e)
        {
            // 框架功能：测试播放
            if (MusicFiles.Count == 0)
            {
                MessageBox.Show("请先导入音乐文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ValidateInputs())
            {
                MessageBox.Show("测试播放功能（使用目录中的音乐文件）", "测试播放", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool ValidateInputs()
        {
            // 验证时间输入
            if (!int.TryParse(StartHourInput.Text, out int startHour) || startHour < 0 || startHour > 23)
            {
                MessageBox.Show("请输入有效的开始小时 (0-23)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(StartMinuteInput.Text, out int startMinute) || startMinute < 0 || startMinute > 59)
            {
                MessageBox.Show("请输入有效的开始分钟 (0-59)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(StartSecondInput.Text, out int startSecond) || startSecond < 0 || startSecond > 59)
            {
                MessageBox.Show("请输入有效的开始秒钟 (0-59)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(EndHourInput.Text, out int endHour) || endHour < 0 || endHour > 23)
            {
                MessageBox.Show("请输入有效的关闭小时 (0-23)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(EndMinuteInput.Text, out int endMinute) || endMinute < 0 || endMinute > 59)
            {
                MessageBox.Show("请输入有效的关闭分钟 (0-59)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(EndSecondInput.Text, out int endSecond) || endSecond < 0 || endSecond > 59)
            {
                MessageBox.Show("请输入有效的关闭秒钟 (0-59)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(PrepareTimeInput.Text, out int prepareTime) || prepareTime < 0)
            {
                MessageBox.Show("请输入有效的预备时间 (≥0)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!int.TryParse(VolumeInput.Text, out int volume) || volume < 0 || volume > 100)
            {
                MessageBox.Show("请输入有效的音量 (0-100)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        // 触屏滚动支持
        private void DragThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.VerticalChange);
        }

        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 可以添加滚动相关的逻辑
        }

        // 应用配置按钮点击事件
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInputs())
            {
                // 更新设置
                _alarmSettings.AlarmTitle = AlarmTitleInput.Text;
                _alarmSettings.StartHour = int.Parse(StartHourInput.Text);
                _alarmSettings.StartMinute = int.Parse(StartMinuteInput.Text);
                _alarmSettings.StartSecond = int.Parse(StartSecondInput.Text);
                _alarmSettings.EndHour = int.Parse(EndHourInput.Text);
                _alarmSettings.EndMinute = int.Parse(EndMinuteInput.Text);
                _alarmSettings.EndSecond = int.Parse(EndSecondInput.Text);
                _alarmSettings.PrepareTime = int.Parse(PrepareTimeInput.Text);
                _alarmSettings.Volume = int.Parse(VolumeInput.Text);
                _alarmSettings.MusicFiles = new List<string>(MusicFiles);
                _alarmSettings.IsEnabled = true;

                // 保存到独立配置文件
                SaveAlarmSettings();

                MessageBox.Show("闹钟配置已应用", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AlarmCheckTimer_Tick(object sender, EventArgs e)
        {
            if (!_alarmSettings.IsEnabled) return;

            DateTime now = DateTime.Now;
            TimeSpan currentTime = now.TimeOfDay;

            // 检查是否是新的一天（程序持续运行到第二天）
            if (_lastCheckDate.Date < now.Date)
            {
                // 新的一天，重置跳过状态
                _lastSkippedDate = DateTime.MinValue;
                _lastCheckDate = now;
                Console.WriteLine($"新的一天，重置闹钟跳过状态");
            }

            // 检查禁止时间段：上午8点~12点，下午14:30~22点
            if ((currentTime >= new TimeSpan(8, 0, 0) && currentTime < new TimeSpan(12, 0, 0)) ||
                (currentTime >= new TimeSpan(14, 30, 0) && currentTime < new TimeSpan(22, 0, 0)))
            {
                return; // 在禁止时间段内，不执行提醒
            }

            TimeSpan startTime = new TimeSpan(_alarmSettings.StartHour, _alarmSettings.StartMinute, _alarmSettings.StartSecond);
            TimeSpan endTime = new TimeSpan(_alarmSettings.EndHour, _alarmSettings.EndMinute, _alarmSettings.EndSecond);

            // 检查是否在同一天已经跳过闹钟（仅运行时检查）
            if (_lastSkippedDate.Date == now.Date)
            {
                // 如果今天已经跳过闹钟，不再触发
                if (_isAlarmActive)
                {
                    _isAlarmActive = false;
                }
                return;
            }

            // 检查是否在提醒时间段内
            if (currentTime >= startTime && currentTime <= endTime && !_isAlarmActive)
            {
                _isAlarmActive = true;
                ShowAlarmNotification();
                Console.WriteLine("事件触发！！");
            }
            else if (currentTime > endTime)
            {
                _isAlarmActive = false;
            }
        }

        // 显示提醒窗口
        private void ShowAlarmNotification()
        {
            // 在主线程中创建提醒窗口
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DateTime now = DateTime.Now;
                TimeSpan startTime = new TimeSpan(_alarmSettings.StartHour, _alarmSettings.StartMinute, _alarmSettings.StartSecond);
                TimeSpan endTime = new TimeSpan(_alarmSettings.EndHour, _alarmSettings.EndMinute, _alarmSettings.EndSecond);

                DateTime alarmStartTime;
                bool isInAlarmPeriod = false;

                // 判断当前是否在闹钟时间段内
                if (now.TimeOfDay >= startTime && now.TimeOfDay <= endTime)
                {
                    // 情况1：已经在闹钟时间段内
                    alarmStartTime = now;
                    isInAlarmPeriod = true;
                    Console.WriteLine($"已在闹钟区间内，从当前时间开始10秒倒计时");
                }
                else if (now.TimeOfDay < startTime)
                {
                    // 情况2：今天还没到开始时间
                    alarmStartTime = now.Date + startTime;
                    isInAlarmPeriod = false;
                    Console.WriteLine($"今天 {startTime} 开始，距离开始还有 {(alarmStartTime - now).TotalSeconds} 秒");
                }
                else
                {
                    // 情况3：今天已经过了结束时间，使用明天的开始时间
                    alarmStartTime = now.Date.AddDays(1) + startTime;
                    isInAlarmPeriod = false;
                    Console.WriteLine($"明天 {startTime} 开始，距离开始还有 {(alarmStartTime - now).TotalSeconds} 秒");
                }

                var alarmWindow = new AlarmNotificationWindow(alarmStartTime, _alarmSettings.PrepareTime, _alarmSettings.AlarmTitle, isInAlarmPeriod);

                // 订阅关闭事件，记录跳过状态（仅运行时）
                alarmWindow.Closed += (s, e) =>
                {
                    // 如果用户关闭了窗口，记录跳过状态（仅内存中）
                    if (isInAlarmPeriod)
                    {
                        _lastSkippedDate = DateTime.Now;
                        _isAlarmActive = false; // 重置状态，等待明天
                        Console.WriteLine($"闹钟已跳过，今天不再提醒（程序重启后重置）");
                    }
                };

                alarmWindow.Show();
            }));
        }

        // 保存和加载闹钟设置
        private void SaveAlarmSettings()
        {
            try
            {
                // 保存相对路径到设置中
                _alarmSettings.MusicFiles = new List<string>();
                foreach (var file in MusicFiles)
                {
                    _alarmSettings.MusicFiles.Add(file); // 只保存文件名
                }

                string settingsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "testdemo", "alarm_settings.json");

                string json = System.Text.Json.JsonSerializer.Serialize(_alarmSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存闹钟设置失败: {ex.Message}");
            }
        }

        private void LoadAlarmSettings()
        {
            try
            {
                string settingsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "testdemo", "alarm_settings.json");

                if (System.IO.File.Exists(settingsPath))
                {
                    string json = System.IO.File.ReadAllText(settingsPath);
                    _alarmSettings = System.Text.Json.JsonSerializer.Deserialize<AlarmSettings>(json);

                    // 更新UI
                    AlarmTitleInput.Text = _alarmSettings.AlarmTitle;
                    StartHourInput.Text = _alarmSettings.StartHour.ToString();
                    StartMinuteInput.Text = _alarmSettings.StartMinute.ToString();
                    StartSecondInput.Text = _alarmSettings.StartSecond.ToString();
                    EndHourInput.Text = _alarmSettings.EndHour.ToString();
                    EndMinuteInput.Text = _alarmSettings.EndMinute.ToString();
                    EndSecondInput.Text = _alarmSettings.EndSecond.ToString();
                    PrepareTimeInput.Text = _alarmSettings.PrepareTime.ToString();
                    VolumeInput.Text = _alarmSettings.Volume.ToString();

                    // 先重新读取实际目录中的文件
                    ReloadMusicFiles();

                    // 如果设置中有文件但目录中没有，尝试从设置恢复（可选）
                    if (_alarmSettings.MusicFiles.Count > 0 && MusicFiles.Count == 0)
                    {
                        // 这里可以添加从备份恢复的逻辑（如果需要）
                        Console.WriteLine("设置中有音乐文件记录，但目录中不存在");
                    }
                }
                else
                {
                    // 如果文件不存在，使用默认值设置UI
                    SetDefaultUIValues();

                    // 读取音乐目录中的现有文件
                    ReloadMusicFiles();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载闹钟设置失败: {ex.Message}");
                SetDefaultUIValues();
                ReloadMusicFiles(); // 仍然尝试读取现有音乐文件
            }
        }

        private void SetDefaultUIValues()
        {
            AlarmTitleInput.Text = _alarmSettings.AlarmTitle;
            StartHourInput.Text = _alarmSettings.StartHour.ToString();
            StartMinuteInput.Text = _alarmSettings.StartMinute.ToString();
            StartSecondInput.Text = _alarmSettings.StartSecond.ToString();
            EndHourInput.Text = _alarmSettings.EndHour.ToString();
            EndMinuteInput.Text = _alarmSettings.EndMinute.ToString();
            EndSecondInput.Text = _alarmSettings.EndSecond.ToString();
            PrepareTimeInput.Text = _alarmSettings.PrepareTime.ToString();
            VolumeInput.Text = _alarmSettings.Volume.ToString();
        }

        // 获取完整音乐文件路径的方法（供播放器使用）
        public string GetMusicFilePath(string fileName)
        {
            return Path.Combine(_musicDirectory, fileName);
        }
    }
}