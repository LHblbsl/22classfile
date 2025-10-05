using NAudio.Wave;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace testdemo
{
    public class ReadingContainerData
    {
        public Canvas DeleteButton { get; set; }
        public bool IsSlidOut { get; set; }
    }

    public class ReadingFileDownloadInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public string? LocalPath { get; set; }
        public bool IsDownloading { get; set; }
        public bool IsCompleted { get; set; }
        public Border? ProgressBorder { get; set; }
    }

    public partial class ReadingWindow : Window
    {
        private double _lastHomeworkHeight = 0;
        private Dictionary<string, ReadingFileDownloadInfo> _downloadProgressDict = new();
        private DownloadManager _downloadManager;
        private JArray _currentReadings = new JArray();
        private readonly string _imageCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClassManager",
            "ReadingImageCache");
        private const string APP_ID = "qUmcbO6SrqiCqp9iyY4xXFIQ-gzGzoHsz";
        private const string MASTER_KEY = "g5BA3HhRMrKviECp68jNyO72";
        private const string SERVER_URL = "https://qumcbo6s.lc-cn-n1-shared.com";
        private DispatcherTimer _timeRefreshTimer;
        private DispatcherTimer _updateTimer;
        private HomeworkAlertWindow _alertWindow;
        private HomeworkWindow _homeworkWindow;
        private bool _isWindowMinimized = false;
        private DateTime _lastFileClickTime = DateTime.MinValue;
        private bool _isUpdatingPosition = false;
        private DateTime _lastPositionUpdate = DateTime.MinValue;
        private const double READING_POSITION_CHANGE_THRESHOLD = 2.0;
        private const double READING_POSITION_CHANGE_SIGNIFICANT = 20.0;
        private DateTime _lastReadingPositionAnimationTime = DateTime.MinValue;

        public ReadingWindow()
        {
            InitializeComponent();
            _downloadManager = new DownloadManager();
            _alertWindow = HomeworkAlertWindow.Instance;

            // 直接设置窗口属性
            this.Opacity = 0.95;
            this.Width = 830;
            this.Height = 800;
            this.Visibility = Visibility.Visible;
            this.Topmost = true;

            // 立即附加到作业窗口
            AttachToHomeworkWindow();
            _currentReadings = new JArray();

            // 立即执行初始化
            PlayEntranceAnimation();
            LoadReadingData();
            StartTimeRefresh(); // 添加这行
            StartAutoRefresh();
        }

        // 数据加载（只查 type=reading）
        private async void LoadReadingData()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // 添加调试输出
                    Console.WriteLine("开始加载早晚读数据...");

                    // 修改查询条件，尝试更宽松的查询
                    var url = $"{SERVER_URL}/1.1/classes/Notice?where={{\"type\":\"reading\"}}&order=-createdAt&limit=20";
                    Console.WriteLine($"请求URL: {url}");

                    // 添加 LeanCloud 头信息
                    client.DefaultRequestHeaders.Add("X-LC-Id", APP_ID);
                    client.DefaultRequestHeaders.Add("X-LC-Key", $"{MASTER_KEY},master");

                    var response = await client.GetAsync(url);
                    Console.WriteLine($"响应状态: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"响应数据: {json}");

                        var result = JObject.Parse(json);
                        var newReadings = result["results"] as JArray;

                        Dispatcher.Invoke(() =>
                        {
                            Console.WriteLine($"获取到 {newReadings?.Count ?? 0} 条早晚读数据");

                            bool hasNew = HasNewReading(newReadings);
                            if (hasNew || _currentReadings.Count == 0)
                            {
                                PlayEntranceAnimation();
                                UpdateReadingList(newReadings);
                                _currentReadings = newReadings ?? new JArray();
                                ReadingLastUpdateText.Text = DateTime.Now.ToString("HH:mm:ss");
                            }
                            else
                            {
                                UpdateDeadlineTimersOnly();
                            }

                            // 如果没有数据，显示提示
                            if (newReadings == null || newReadings.Count == 0)
                            {
                                ReadingLoadingText.Text = "暂无早晚读数据";
                                ReadingLoadingText.Visibility = Visibility.Visible;
                            }
                        });
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"请求失败: {response.StatusCode}, {errorContent}");

                        Dispatcher.Invoke(() =>
                        {
                            ReadingLoadingText.Text = $"加载失败: {response.StatusCode}";
                            ReadingLoadingText.Visibility = Visibility.Visible;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载早晚读数据异常: {ex}");
                Dispatcher.Invoke(() =>
                {
                    ReadingLoadingText.Text = $"加载失败: {ex.Message}";
                    ReadingLoadingText.Visibility = Visibility.Visible;
                });
            }
        }

        // 自动刷新
        private void StartAutoRefresh()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMinutes(7);
            _updateTimer.Tick += (s, e) => LoadReadingData();
            _updateTimer.Start();
        }

        // 位置联动
        // 修改 AttachToHomeworkWindow 方法
        private void AttachToHomeworkWindow()
        {
            _homeworkWindow = Application.Current.Windows.OfType<HomeworkWindow>().FirstOrDefault();

            if (_homeworkWindow == null)
            {
                Console.WriteLine("警告：未找到作业窗口，使用默认位置");
                // 如果找不到 HomeworkWindow，使用默认位置
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                this.Left = (screenWidth - this.Width) / 2;
                this.Top = 100;
                return;
            }

            Console.WriteLine("找到作业窗口，开始位置联动");

            // 订阅作业窗口的位置变化事件
            _homeworkWindow.PositionChangedForReading += OnHomeworkWindowPositionChangedForReading;

            // 立即强制更新一次位置
            Dispatcher.BeginInvoke(() =>
            {
                // 获取作业窗口的当前状态并强制更新
                var contentHeight = _homeworkWindow.GetHomeworkListActualHeight();
                double targetTop = _homeworkWindow.Top + contentHeight + 5;

                this.Left = _homeworkWindow.Left;
                this.Top = targetTop;
                this.Width = _homeworkWindow.Width;

                Console.WriteLine($"早晚读窗口初始位置设置: Left={this.Left}, Top={this.Top}");
            }, DispatcherPriority.Loaded);
        }

        // 新的事件处理方法
        // 在 ReadingWindow 中修复事件处理方法
        private void OnHomeworkWindowPositionChangedForReading(object sender, HomeworkWindow.PositionChangedEventArgs e)
        {
            // 直接使用作业窗口计算好的位置，不要自己重新计算
            UpdateReadingWindowPositionDirect(e.Left, e.Top, e.Width);
        }

        // 新的直接位置更新方法
        private void UpdateReadingWindowPositionDirect(double left, double top, double width)
        {
            if (_isUpdatingPosition) return;

            var now = DateTime.Now;
            if ((now - _lastPositionUpdate).TotalMilliseconds < 150) return;

            _isUpdatingPosition = true;

            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // 计算位置差异
                        double leftDiff = Math.Abs(this.Left - left);
                        double topDiff = Math.Abs(this.Top - top);
                        double widthDiff = Math.Abs(this.Width - width);

                        // 只有位置变化超过阈值才更新
                        bool needsUpdate = leftDiff > 2 || topDiff > 2 || widthDiff > 2;

                        if (needsUpdate)
                        {
                            Console.WriteLine($"早晚读直接位置更新: Left={left}, Top={top}, 当前Top={this.Top}, 差值={topDiff}");

                            // 使用动画更新到指定位置
                            if (topDiff > 20)
                            {
                                AnimateReadingWindowPositionSmooth(left, top, width);
                            }
                            else
                            {
                                AnimateReadingWindowPositionQuick(left, top, width);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"直接位置更新错误: {ex.Message}");
                    }
                    finally
                    {
                        _isUpdatingPosition = false;
                        _lastPositionUpdate = DateTime.Now;
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"直接位置更新异常: {ex.Message}");
                _isUpdatingPosition = false;
            }
        }
        // 新的位置更新方法
        // 修改位置更新方法，使用固定计算逻辑
        // 修改位置更新方法，使用动画
        private void UpdateReadingWindowPositionFromHomework(double? left = null, double? top = null, double? width = null, double? contentHeight = null)
        {
            if (_isUpdatingPosition) return;

            var now = DateTime.Now;
            if ((now - _lastPositionUpdate).TotalMilliseconds < 150) return;

            _isUpdatingPosition = true;

            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (_homeworkWindow != null && _homeworkWindow.IsVisible)
                        {
                            double homeworkLeft = left ?? _homeworkWindow.Left;
                            double homeworkTop = top ?? _homeworkWindow.Top;
                            double homeworkWidth = width ?? _homeworkWindow.Width;
                            double homeworkContentHeight = contentHeight ?? GetActualHomeworkContentHeight();

                            // 计算早晚读窗口的目标位置（在作业内容下方）
                            double targetLeft = homeworkLeft;
                            double targetTop = homeworkTop + homeworkContentHeight + 5;
                            double targetWidth = homeworkWidth;

                            // 计算位置差异
                            double leftDiff = Math.Abs(this.Left - targetLeft);
                            double topDiff = Math.Abs(this.Top - targetTop);
                            double widthDiff = Math.Abs(this.Width - targetWidth);

                            // 防抖检查
                            bool needsUpdate = leftDiff > READING_POSITION_CHANGE_THRESHOLD ||
                                             topDiff > READING_POSITION_CHANGE_THRESHOLD ||
                                             widthDiff > READING_POSITION_CHANGE_THRESHOLD;

                            if (needsUpdate)
                            {
                                Console.WriteLine($"早晚读窗口位置更新: Top差={topDiff:F1}, 作业高度={homeworkContentHeight:F1}");

                                // 根据变化幅度选择动画方式
                                if (topDiff > READING_POSITION_CHANGE_SIGNIFICANT)
                                {
                                    AnimateReadingWindowPositionSmooth(targetLeft, targetTop, targetWidth);
                                }
                                else
                                {
                                    AnimateReadingWindowPositionQuick(targetLeft, targetTop, targetWidth);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"更新早晚读窗口位置错误: {ex.Message}");
                        _isUpdatingPosition = false;
                    }
                    finally
                    {
                        _lastPositionUpdate = DateTime.Now;
                        _isUpdatingPosition = false;
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"位置更新异常: {ex.Message}");
                _isUpdatingPosition = false;
            }
        }

        // 平滑动画
        private void AnimateReadingWindowPositionSmooth(double targetLeft, double targetTop, double targetWidth)
        {
            if ((DateTime.Now - _lastReadingPositionAnimationTime).TotalMilliseconds < 100)
                return;

            _lastReadingPositionAnimationTime = DateTime.Now;

            var animation = new Storyboard();

            var leftAnim = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(600));
            leftAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(leftAnim, this);
            Storyboard.SetTargetProperty(leftAnim, new PropertyPath(LeftProperty));
            animation.Children.Add(leftAnim);

            var topAnim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(600));
            topAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(topAnim, this);
            Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));
            animation.Children.Add(topAnim);

            var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(600));
            widthAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(widthAnim, this);
            Storyboard.SetTargetProperty(widthAnim, new PropertyPath(WidthProperty));
            animation.Children.Add(widthAnim);

            animation.Begin();
        }

        // 快速动画
        private void AnimateReadingWindowPositionQuick(double targetLeft, double targetTop, double targetWidth)
        {
            var animation = new Storyboard();

            var leftAnim = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(250));
            leftAnim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(leftAnim, this);
            Storyboard.SetTargetProperty(leftAnim, new PropertyPath(LeftProperty));
            animation.Children.Add(leftAnim);

            var topAnim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(250));
            topAnim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(topAnim, this);
            Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));
            animation.Children.Add(topAnim);

            var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(250));
            widthAnim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(widthAnim, this);
            Storyboard.SetTargetProperty(widthAnim, new PropertyPath(WidthProperty));
            animation.Children.Add(widthAnim);

            animation.Begin();
        }
        private void UpdateReadingWindowPosition()
        {
            // 防止频繁更新导致的闪烁
            if (_isUpdatingPosition) return;

            var now = DateTime.Now;
            if ((now - _lastPositionUpdate).TotalMilliseconds < 50) return;

            _isUpdatingPosition = true;

            try
            {
                if (_homeworkWindow != null && _homeworkWindow.IsVisible)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            // 获取作业窗口的位置和内容高度
                            double homeworkContentHeight = GetActualHomeworkContentHeight();

                            // 设置早晚读窗口位置（在作业内容下方）
                            this.Left = _homeworkWindow.Left;
                            this.Top = _homeworkWindow.Top + homeworkContentHeight + 5; // 加5像素间距
                            this.Width = _homeworkWindow.Width;

                            Console.WriteLine($"早晚读窗口位置更新: Top={this.Top}, 作业内容高度={homeworkContentHeight}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"更新早晚读窗口位置错误: {ex.Message}");
                        }
                        finally
                        {
                            _isUpdatingPosition = false;
                            _lastPositionUpdate = DateTime.Now;
                        }
                    }, DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"位置更新异常: {ex.Message}");
                _isUpdatingPosition = false;
            }
        }
        private double GetActualHomeworkContentHeight()
        {
            try
            {
                if (_homeworkWindow == null) return 0;

                // 使用反射获取作业窗口的实际内容高度
                var homeworkListPanel = GetHomeworkListPanel();
                if (homeworkListPanel != null)
                {
                    // 计算所有可见子元素的总高度
                    double totalHeight = 0;
                    foreach (var child in homeworkListPanel.Children)
                    {
                        if (child is FrameworkElement element &&
                            element.Visibility == Visibility.Visible &&
                            element.ActualHeight > 0)
                        {
                            totalHeight += element.ActualHeight + element.Margin.Top + element.Margin.Bottom;
                        }
                    }
                    return totalHeight;
                }

                // 备用方案：使用作业窗口的公开方法
                return _homeworkWindow.GetContentHeight();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"计算作业内容高度错误: {ex.Message}");
                return 400; // 默认高度
            }
        }
       
        // 入场动画
        private void PlayEntranceAnimation()
        {
            // 确保窗口可见
            this.Visibility = Visibility.Visible;

            Storyboard entranceAnimation = (Storyboard)this.FindResource("ReadingWindowEntranceAnimation");
            if (entranceAnimation != null)
            {
                // 重置动画状态
                this.Opacity = 0;
                readingMainTransform.ScaleX = 0.5;
                readingMainTransform.ScaleY = 0.5;

                entranceAnimation.Completed += (s, e) =>
                {
                    this.Opacity = 0.95; // 动画结束后确保可见
                };
                entranceAnimation.Begin(this);
            }
            else
            {
                // 如果没有动画资源，直接显示
                this.Opacity = 0.95;
                readingMainTransform.ScaleX = 1;
                readingMainTransform.ScaleY = 1;
            }
        }

        // 列表刷新
        private void UpdateReadingList(JArray readings)
        {
            ReadingListPanel.Children.Clear();

            if (readings == null || readings.Count == 0)
            {
                ReadingLoadingText.Text = "暂无早晚读";
                ReadingListPanel.Children.Add(ReadingLoadingText);
                return;
            }

            ReadingListPanel.Children.Remove(ReadingLoadingText);

            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                var container = CreateReadingContainer(reading);

                container.Opacity = 0;
                container.RenderTransform = new TranslateTransform(0, -15);

                ReadingListPanel.Children.Add(container);

                StartItemEntranceAnimation(container, i * 0.4);
            }
        }

        // 创建早晚读条目容器
        // 创建早晚读条目容器
        private Border CreateReadingContainer(JToken reading)
        {
            var container = new Border
            {
                Style = (Style)FindResource("ReadingContainerStyle"),
                Cursor = Cursors.Hand,
                RenderTransform = new TranslateTransform(0, 0)
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            var contentStackPanel = new StackPanel { Margin = new Thickness(0) };

            // 早晚读时间信息 - 修复字段名
            var startTimeStr = reading["readingStartTime"]?.ToString(); // 改为 readingStartTime
            var endTimeStr = reading["readingEndTime"]?.ToString();     // 改为 readingEndTime
            var durationStr = reading["readingDuration"]?.ToString();   // 改为 readingDuration
            var readingType = reading["readingType"]?.ToString() ?? "custom"; // 读取 readingType

            var startTime = ParseLeanCloudTime(startTimeStr);
            var endTime = ParseLeanCloudTime(endTimeStr);

            var title = reading["title"]?.ToString() ?? "无标题";
            var author = reading["author"]?.ToString() ?? "未知";
            var createdAt = reading["createdAt"]?.ToString();
            var publishTime = FormatTime(createdAt);

            // 判断早晚读类型 - 优先使用数据中的 readingType
            if (string.IsNullOrEmpty(readingType) || readingType == "custom")
            {
                readingType = DetermineReadingType(startTime, title);
            }
            var readingIcon = GetReadingIcon(readingType);
            var typePrefix = GetReadingTypeName(readingType).Replace("【", "").Replace("】", "");

            // 计算当前状态和剩余时间
            var (statusText, statusColor, timeLeftText) = CalculateReadingStatus(startTime, endTime, durationStr, readingType);

            // 如果标题不包含类型前缀，则添加
            if (!title.Contains("【") && !title.Contains("】"))
            {
                title = $"{GetReadingTypeName(readingType)}{title}";
            }

            var mainContainer = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };

            // 修改标题区域，添加状态信息
            var titleBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xBF, 0xBF)), // 青蓝色
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 8, 8),
                VerticalAlignment = VerticalAlignment.Center
            };

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 主标题行
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 20,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 状态标签
            var statusBorder = new Border
            {
                Background = statusColor,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var statusTextBlock = new TextBlock
            {
                Text = statusText,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            statusBorder.Child = statusTextBlock;

            titleRow.Children.Add(titleText);
            titleRow.Children.Add(statusBorder);

            // 时间信息行
            var timeInfoRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var timeIcon = new TextBlock
            {
                Text = "⏰",
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var timeTextBlock = new TextBlock
            {
                Text = timeLeftText,
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };

            timeInfoRow.Children.Add(timeIcon);
            timeInfoRow.Children.Add(timeTextBlock);

            titlePanel.Children.Add(titleRow);
            titlePanel.Children.Add(timeInfoRow);

            // 元信息行（作者和发布时间）
            var metaInfoRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var authorText = new TextBlock
            {
                Text = $"发布人: {author}",
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var timeSeparator = new TextBlock
            {
                Text = " | ",
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            var publishTimeText = new TextBlock
            {
                Text = $"发布时间: {publishTime}",
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            metaInfoRow.Children.Add(authorText);
            metaInfoRow.Children.Add(timeSeparator);
            metaInfoRow.Children.Add(publishTimeText);

            titlePanel.Children.Add(metaInfoRow);
            titleBorder.Child = titlePanel;
            mainContainer.Children.Add(titleBorder);

            // 内容区域 - 修改为半透明浅蓝色
            var content = reading["content"]?.ToString() ?? "";
            var lines = content.Split('\n');

            if (lines.Length > 0)
            {
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var itemBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x66, 0x87, 0xCE, 0xEB)), // 半透明浅蓝色
                        CornerRadius = new CornerRadius(15),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 0, 6, 6),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var itemText = new TextBlock
                    {
                        Text = trimmed,
                        FontSize = 18,
                        Foreground = Brushes.Black,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap,
                        FontWeight = FontWeights.SemiBold
                    };

                    itemBorder.Child = itemText;
                    mainContainer.Children.Add(itemBorder);
                }
            }

            contentStackPanel.Children.Add(mainContainer);

            // 文件下载区域
            var fileUrls = reading["fileUrls"] as JArray;
            var fileNames = reading["fileNames"] as JArray;
            var fileUniqueNames = reading["fileUniqueNames"] as JArray;

            if (fileUrls != null && fileNames != null && fileUrls.Count > 0)
            {
                var filesTitle = new TextBlock
                {
                    Text = $"{readingIcon} 相关文件:",
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 8)
                };
                contentStackPanel.Children.Add(filesTitle);

                var filesContainer = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                for (int i = 0; i < fileUrls.Count; i++)
                {
                    var fileUrl = fileUrls[i]?.ToString();
                    var fileName = fileNames[i]?.ToString();
                    var fileUniqueName = fileUniqueNames?[i]?.ToString();

                    if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(fileName))
                    {
                        var actualFileName = !string.IsNullOrEmpty(fileUniqueName) ? fileUniqueName : fileName;

                        var fileItemContainer = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 6)
                        };

                        // 文件区域也使用浅蓝色
                        var fileNameBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x87, 0xCE, 0xEB)),
                            CornerRadius = new CornerRadius(15),
                            Padding = new Thickness(12, 8, 12, 8),
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Cursor = Cursors.Hand,
                            ToolTip = "点击下载文件"
                        };

                        var fileNameText = new TextBlock
                        {
                            Text = fileName,
                            FontSize = 14,
                            Foreground = Brushes.Black,
                            FontWeight = FontWeights.Medium,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.NoWrap
                        };
                        fileNameBorder.Child = fileNameText;

                        var actionPanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            VerticalAlignment = VerticalAlignment.Center,
                            Opacity = 0,
                            RenderTransform = new TransformGroup
                            {
                                Children = new TransformCollection
                        {
                            new ScaleTransform(0.8, 0.8),
                            new TranslateTransform(0, 10)
                        }
                            }
                        };

                        var progressBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x95, 0xa5, 0xa6)),
                            CornerRadius = new CornerRadius(15),
                            Padding = new Thickness(12, 8, 12, 8),
                            VerticalAlignment = VerticalAlignment.Center,
                            MinWidth = 120,
                            Visibility = Visibility.Visible
                        };

                        var progressText = new TextBlock
                        {
                            Text = "等待下载...",
                            FontSize = 12,
                            Foreground = Brushes.Black,
                            FontWeight = FontWeights.Normal,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        progressBorder.Child = progressText;

                        var downloadInfo = new ReadingFileDownloadInfo
                        {
                            FileName = actualFileName,
                            FileUrl = fileUrl,
                            ProgressBorder = progressBorder
                        };

                        var openButton = CreateCircleActionButton("open.png", "打开文件");
                        openButton.MouseLeftButtonDown += (s, e) => OpenDownloadedFile(actualFileName);

                        var locateButton = CreateCircleActionButton("folder.png", "定位文件");
                        locateButton.MouseLeftButtonDown += (s, e) => OpenFileInExplorer(actualFileName);

                        var redownloadButton = CreateCircleActionButton("refresh.png", "重新下载");
                        redownloadButton.MouseLeftButtonDown += (s, e) =>
                        {
                            downloadInfo.IsCompleted = false;
                            downloadInfo.IsDownloading = false;
                            downloadInfo.LocalPath = null;
                            PlayActionPanelHideAnimation(actionPanel);
                            progressBorder.Visibility = Visibility.Visible;
                            progressText.Text = "开始下载...";
                            StartFileDownload(downloadInfo, progressText, progressBorder, actionPanel);
                        };

                        actionPanel.Children.Add(openButton);
                        actionPanel.Children.Add(locateButton);
                        actionPanel.Children.Add(redownloadButton);

                        var rightPanelContainer = new Grid();
                        rightPanelContainer.Children.Add(progressBorder);
                        rightPanelContainer.Children.Add(actionPanel);

                        fileItemContainer.Children.Add(fileNameBorder);
                        fileItemContainer.Children.Add(rightPanelContainer);

                        filesContainer.Children.Add(fileItemContainer);

                        if (_downloadManager.FileExists(actualFileName))
                        {
                            progressText.Text = "已下载 ✓";
                            progressBorder.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0xb8, 0x94));
                            downloadInfo.IsCompleted = true;
                            downloadInfo.LocalPath = _downloadManager.GetFilePath(actualFileName);

                            fileNameBorder.MouseLeftButtonDown += (s, e) =>
                            {
                                OpenDownloadedFile(actualFileName);
                            };
                            fileNameBorder.ToolTip = "点击打开文件";

                            progressBorder.Visibility = Visibility.Collapsed;
                            PlayActionPanelShowAnimation(actionPanel);
                        }
                        else
                        {
                            progressText.Text = "开始下载...";
                            StartFileDownload(downloadInfo, progressText, progressBorder, actionPanel);
                        }

                        string downloadKey = $"{reading["objectId"]}_{actualFileName}";
                        _downloadProgressDict[downloadKey] = downloadInfo;
                    }
                }

                contentStackPanel.Children.Add(filesContainer);
            }

            // 图片区域
            var images = reading["images"] as JArray;
            var imageNames = reading["imageNames"] as JArray;
            var imageUniqueNames = reading["imageUniqueNames"] as JArray;

            if (images != null && images.Count > 0)
            {
                var imagesPanel = new WrapPanel
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Orientation = Orientation.Horizontal
                };

                var imagesTitle = new TextBlock
                {
                    Text = $"{readingIcon} 相关图片:",
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 8)
                };

                contentStackPanel.Children.Add(imagesTitle);
                contentStackPanel.Children.Add(imagesPanel);

                for (int i = 0; i < images.Count; i++)
                {
                    var imageUrl = images[i]?.ToString();
                    var imageName = imageNames?[i]?.ToString();
                    var imageUniqueName = imageUniqueNames?[i]?.ToString();

                    if (imageUrl != null)
                    {
                        var imageBorder = new Border
                        {
                            Width = 80,
                            Height = 80,
                            Margin = new Thickness(0, 0, 8, 8),
                            Background = Brushes.Transparent,
                            Cursor = Cursors.Hand,
                            ToolTip = "点击查看图片",
                            CornerRadius = new CornerRadius(8),
                            Clip = new RectangleGeometry(new Rect(0, 0, 80, 80), 8, 8)
                        };

                        var image = new Image
                        {
                            Stretch = Stretch.UniformToFill,
                            RenderTransformOrigin = new Point(0.5, 0.5)
                        };

                        image.RenderTransform = new ScaleTransform(1, 1);

                        var cacheFileName = !string.IsNullOrEmpty(imageUniqueName) ? imageUniqueName :
                                           !string.IsNullOrEmpty(imageName) ? imageName :
                                           Guid.NewGuid().ToString() + ".jpg";

                        LoadImageAsync(image, imageUrl.ToString(), cacheFileName);
                        imageBorder.Child = image;

                        imageBorder.MouseDown += (s, e) =>
                        {
                            var clickAnimation = (Storyboard)FindResource("ReadingImageClickAnimation");
                            if (clickAnimation != null)
                            {
                                var clonedAnimation = clickAnimation.Clone();
                                Storyboard.SetTarget(clonedAnimation, image);
                                clonedAnimation.Begin();
                            }

                            try
                            {
                                var cachePath = Path.Combine(_imageCacheDir, cacheFileName);

                                if (File.Exists(cachePath))
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = cachePath,
                                        UseShellExecute = true
                                    });
                                }
                                else
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = imageUrl.ToString(),
                                        UseShellExecute = true
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"无法打开图片: {ex.Message}");
                            }
                        };

                        imagesPanel.Children.Add(imageBorder);
                    }
                }
            }

            Grid.SetColumn(contentStackPanel, 0);
            mainGrid.Children.Add(contentStackPanel);

            var deleteButtonContainer = CreateDeleteButton(reading, container);
            Grid.SetColumn(deleteButtonContainer, 1);
            mainGrid.Children.Add(deleteButtonContainer);

            // 创建 ReadingContainerData 对象
            var containerData = new ReadingContainerData
            {
                DeleteButton = deleteButtonContainer,
                IsSlidOut = false
            };

            // 修改容器的 Tag 设置，存储时间信息用于刷新
            container.Tag = new Tuple<DateTime, DateTime, ReadingContainerData, string, Border, string>(
                startTime, endTime, containerData, durationStr, titleBorder, readingType
            );

            container.MouseDown += (s, e) =>
            {
                ToggleReadingSlide(container, reading);
            };

            container.Child = mainGrid;
            return container;
        }

        // 判断早晚读类型
        private string DetermineReadingType(DateTime startTime, string title)
        {
            // 根据标题判断（优先）
            if (title.Contains("早读") || title.ToLower().Contains("morning"))
                return "morning";
            if (title.Contains("晚读") || title.ToLower().Contains("evening"))
                return "evening";

            // 根据开始时间判断（早读默认上午，晚读默认下午）
            if (startTime != DateTime.MinValue)
            {
                if (startTime.Hour < 12) // 上午
                    return "morning";
                else // 下午
                    return "evening";
            }

            return "custom"; // 自定义时间
        }

        // 获取早晚读图标
        private string GetReadingIcon(string readingType)
        {
            switch (readingType)
            {
                case "morning":
                    return "🌅"; // 早读图标
                case "evening":
                    return "🌇"; // 晚读图标
                default:
                    return "📖"; // 自定义阅读图标
            }
        }

        // 获取早晚读类型名称
        private string GetReadingTypeName(string readingType)
        {
            switch (readingType)
            {
                case "morning":
                    return "【早读】";
                case "evening":
                    return "【晚读】";
                default:
                    return "【其他】";
            }
        }

        // 格式化详细时间间隔
        private string FormatDetailedTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}天{ts.Hours}小时{ts.Minutes}分钟";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.Minutes}分钟{ts.Seconds}秒";
            return $"{(int)ts.Seconds}秒";
        }

        // 更新早晚读时间显示（核心方法）
        private void UpdateReadingTimeDisplay(TextBlock timeText, DateTime startTime, DateTime endTime, string durationStr, string readingType)
        {
            var now = DateTime.Now;
            var readingTypeName = GetReadingTypeName(readingType);

            if (startTime != DateTime.MinValue && endTime != DateTime.MinValue)
            {
                // 有明确的开始和结束时间
                if (now < startTime)
                {
                    var timeLeft = startTime - now;
                    timeText.Text = $"{readingTypeName} 距离开始还有 {FormatDetailedTimeSpan(timeLeft)}";
                    timeText.Foreground = Brushes.SteelBlue;
                }
                else if (now < endTime)
                {
                    var timeLeft = endTime - now;
                    timeText.Text = $"{readingTypeName} 正在进行，距离结束还有 {FormatDetailedTimeSpan(timeLeft)}";
                    timeText.Foreground = Brushes.Green;
                }
                else
                {
                    timeText.Text = $"{readingTypeName} 已结束";
                    timeText.Foreground = Brushes.Gray;
                }
            }
            else if (!string.IsNullOrEmpty(durationStr) && int.TryParse(durationStr, out int durationMinutes))
            {
                // 只有持续时间（分钟）
                if (startTime != DateTime.MinValue)
                {
                    endTime = startTime.AddMinutes(durationMinutes);
                    if (now < startTime)
                    {
                        var timeLeft = startTime - now;
                        timeText.Text = $"{readingTypeName} 距离开始还有 {FormatDetailedTimeSpan(timeLeft)}";
                        timeText.Foreground = Brushes.SteelBlue;
                    }
                    else if (now < endTime)
                    {
                        var timeLeft = endTime - now;
                        timeText.Text = $"{readingTypeName} 正在进行，距离结束还有 {FormatDetailedTimeSpan(timeLeft)}";
                        timeText.Foreground = Brushes.Green;
                    }
                    else
                    {
                        timeText.Text = $"{readingTypeName} 已结束";
                        timeText.Foreground = Brushes.Gray;
                    }
                }
                else
                {
                    timeText.Text = $"{readingTypeName} 持续时间: {durationMinutes}分钟";
                    timeText.Foreground = Brushes.Purple;
                }
            }
            else if (startTime != DateTime.MinValue)
            {
                // 只有开始时间
                if (now < startTime)
                {
                    var timeLeft = startTime - now;
                    timeText.Text = $"{readingTypeName} 距离开始还有 {FormatDetailedTimeSpan(timeLeft)}";
                    timeText.Foreground = Brushes.SteelBlue;
                }
                else
                {
                    timeText.Text = $"{readingTypeName} 已开始";
                    timeText.Foreground = Brushes.Orange;
                }
            }
            else
            {
                // 没有时间信息
                timeText.Text = $"{readingTypeName}";
                timeText.Foreground = Brushes.DarkGray;
            }
        }
        // 新增方法：判断早晚读类型

        // 新增方法：获取早晚读图标

        // 修改时间显示方法

        // 修改刷新时间显示的方法

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}天{ts.Hours}小时";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}分钟";
            return "不足1分钟";
        }

        private DateTime ParseLeanCloudTime(string timeString)
        {
            try
            {
                if (string.IsNullOrEmpty(timeString))
                    return DateTime.MinValue;
                if (DateTime.TryParse(timeString, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime utcTime))
                {
                    return utcTime.ToLocalTime();
                }
                if (timeString.EndsWith("Z"))
                {
                    timeString = timeString.TrimEnd('Z');
                    if (DateTime.TryParse(timeString, out DateTime parsedTime))
                    {
                        return parsedTime.ToLocalTime();
                    }
                }
                return DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private string FormatTime(string isoTime)
        {
            if (string.IsNullOrEmpty(isoTime)) return "未知时间";
            try
            {
                var date = DateTime.Parse(isoTime).ToLocalTime();
                var now = DateTime.Now;
                var diff = now - date;
                if (diff.TotalMinutes < 1) return "刚刚";
                else if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}分钟前";
                else if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}小时前";
                else if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
                else return date.ToString("MM月dd日 HH:mm");
            }
            catch
            {
                return "未知时间";
            }
        }

        // 删除功能
        // 修复 ToggleReadingSlide 方法
        private void ToggleReadingSlide(Border container, JToken reading)
        {
            // 从 Tag 中获取 containerData
            if (container.Tag is Tuple<TextBlock, DateTime, DateTime, ReadingContainerData, string> tagInfo)
            {
                var containerData = tagInfo.Item4; // 第4个元素是 ReadingContainerData

                // 检查点击位置是否在容器的右半边
                var mousePosition = Mouse.GetPosition(container);
                var containerWidth = container.ActualWidth;

                // 如果点击位置在左半边，不执行滑动操作
                if (mousePosition.X < containerWidth / 2)
                {
                    return;
                }

                if (containerData.IsSlidOut)
                {
                    // 滑回原位，隐藏删除按钮
                    PlaySlideAnimation(container, 0);
                    HideDeleteButton(containerData.DeleteButton);
                    containerData.IsSlidOut = false;
                }
                else
                {
                    // 向左滑动，显示删除按钮
                    PlaySlideAnimation(container, -60);
                    ShowDeleteButton(containerData.DeleteButton);
                    containerData.IsSlidOut = true;
                }
            }
        }

        private void PlaySlideAnimation(Border container, double translateX)
        {
            var slideAnimation = new DoubleAnimation
            {
                To = translateX,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var transform = container.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            }
        }

        private void ShowDeleteButton(Canvas deleteButton)
        {
            deleteButton.Opacity = 1;
            var entranceAnimation = (Storyboard)FindResource("ReadingDeleteButtonEntranceAnimation");
            if (entranceAnimation != null)
            {
                var clonedAnimation = entranceAnimation.Clone();
                Storyboard.SetTarget(clonedAnimation, deleteButton);
                clonedAnimation.Begin();
            }
        }

        private void HideDeleteButton(Canvas deleteButton)
        {
            var fadeOutAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            deleteButton.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        private void PlayDeleteButtonAnimation(Image deleteImage)
        {
            var clickAnimation = (Storyboard)FindResource("ReadingImageClickAnimation");
            if (clickAnimation != null)
            {
                var clonedAnimation = clickAnimation.Clone();
                Storyboard.SetTarget(clonedAnimation, deleteImage);
                clonedAnimation.Begin();
            }
        }

        // 修复 DeleteReading 方法
        private async void DeleteReading(JToken reading, Border container)
        {
            // 从 Tag 中获取 containerData
            if (!(container.Tag is Tuple<TextBlock, DateTime, DateTime, ReadingContainerData, string> tagInfo))
            {
                return;
            }

            var containerData = tagInfo.Item4; // 第4个元素是 ReadingContainerData

            // 检查容器是否处于滑动状态（删除按钮是否可见）
            if (!containerData.IsSlidOut)
            {
                return;
            }

            var readingId = reading["objectId"]?.ToString();
            if (string.IsNullOrEmpty(readingId))
            {
                MessageBox.Show("无法删除：通知ID不存在");
                return;
            }

            // 使用确认对话框
            var dialog = new ConfirmationDialog
            {
                Owner = this
            };

            var dialogResult = dialog.ShowDialog();

            if (dialogResult == true && dialog.Result)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("X-LC-Id", APP_ID);
                        client.DefaultRequestHeaders.Add("X-LC-Key", $"{MASTER_KEY},master");

                        // 构建删除URL - 直接删除指定ID的记录
                        var url = $"{SERVER_URL}/1.1/classes/Notice/{readingId}";

                        var response = await client.DeleteAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            // 从界面移除
                            var slideBackAnimation = new DoubleAnimation
                            {
                                To = 0,
                                Duration = TimeSpan.FromSeconds(0.3),
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            };

                            var transform = container.RenderTransform as TranslateTransform;
                            if (transform != null)
                            {
                                transform.BeginAnimation(TranslateTransform.XProperty, slideBackAnimation);
                            }

                            // 淡出动画
                            var fadeOutAnimation = new DoubleAnimation
                            {
                                To = 0,
                                Duration = TimeSpan.FromSeconds(0.3),
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            };

                            fadeOutAnimation.Completed += (s, e) =>
                            {
                                ReadingListPanel.Children.Remove(container);
                                ShowToast("早晚读删除成功");
                            };

                            container.BeginAnimation(OpacityProperty, fadeOutAnimation);
                        }
                        else
                        {
                            ShowToast("删除失败，请重试");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowToast($"删除失败：{ex.Message}");
                }
            }
            else
            {
                // 用户点击了取消，滑回原位
                PlaySlideAnimation(container, 0);
                HideDeleteButton(containerData.DeleteButton);
                containerData.IsSlidOut = false;
            }
        }
        // 文件下载/打开/定位等功能全部 ReadingWindow 独立实现
        private void OpenDownloadedFile(string fileName)
        {
            try
            {
                _downloadManager.OpenDownloadedFile(fileName);
            }
            catch (Exception ex)
            {
                ShowToast($"无法打开文件: {ex.Message}");
            }
        }

        private void OpenFileInExplorer(string fileName)
        {
            try
            {
                _downloadManager.OpenFileInExplorer(fileName);
            }
            catch (Exception ex)
            {
                ShowToast($"无法定位文件: {ex.Message}");
            }
        }

        private async void StartFileDownload(ReadingFileDownloadInfo downloadInfo, TextBlock progressText, Border progressBorder, StackPanel actionPanel = null)
        {
            if (downloadInfo.IsDownloading || downloadInfo.IsCompleted)
                return;

            downloadInfo.IsDownloading = true;

            var progress = new Progress<DownloadManager.DownloadProgress>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateDownloadProgress(downloadInfo, p, progressText, progressBorder, actionPanel);
                });
            });

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();

                string filePath = await _downloadManager.DownloadFileWithProgressAsync(
                    downloadInfo.FileUrl,
                    downloadInfo.FileName,
                    progress,
                    cancellationTokenSource.Token);

                downloadInfo.LocalPath = filePath;
                downloadInfo.IsCompleted = true;
            }
            catch (Exception ex)
            {
                progressText.Text = $"下载失败: {ex.Message}";
                progressBorder.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0xe7, 0x4c, 0x3c));
                downloadInfo.IsDownloading = false;
            }
        }

        private void UpdateDownloadProgress(ReadingFileDownloadInfo downloadInfo, DownloadManager.DownloadProgress progress,
            TextBlock progressText, Border progressBorder, StackPanel actionPanel = null)
        {
            switch (progress.Status)
            {
                case DownloadManager.DownloadStatus.Started:
                    progressText.Text = "开始下载...";
                    progressBorder.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0xf3, 0x9c, 0x12));
                    break;
                case DownloadManager.DownloadStatus.Downloading:
                    string sizeInfo = progress.TotalBytes > 0 ?
                        $"{FormatFileSize(progress.BytesRead)} / {FormatFileSize(progress.TotalBytes)}" :
                        $"{FormatFileSize(progress.BytesRead)}";
                    progressText.Text = $"{progress.Percentage:F1}% ({sizeInfo})";
                    break;
                case DownloadManager.DownloadStatus.Completed:
                    progressText.Text = "下载完成 ✓";
                    progressBorder.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0xb8, 0x94));
                    downloadInfo.IsDownloading = false;
                    downloadInfo.IsCompleted = true;
                    if (actionPanel != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBorder.Visibility = Visibility.Collapsed;
                            PlayActionPanelShowAnimation(actionPanel);
                        });
                    }
                    break;
                case DownloadManager.DownloadStatus.Failed:
                    progressText.Text = $"下载失败: {progress.ErrorMessage}";
                    progressBorder.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0xe7, 0x4c, 0x3c));
                    downloadInfo.IsDownloading = false;
                    break;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // 图片下载
        private async void LoadImageAsync(Image image, string imageUrl, string cacheFileName = null)
        {
            try
            {
                var fileName = cacheFileName;
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = Guid.NewGuid().ToString() + ".jpg";
                    }
                }

                if (!Directory.Exists(_imageCacheDir))
                {
                    Directory.CreateDirectory(_imageCacheDir);
                }

                var cachePath = Path.Combine(_imageCacheDir, fileName);

                if (File.Exists(cachePath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        var bitmap = new BitmapImage(new Uri(cachePath));
                        image.Source = bitmap;
                    });
                    return;
                }

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(imageUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var imageData = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(cachePath, imageData);

                        Dispatcher.Invoke(() =>
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(cachePath);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            image.Source = bitmap;
                        });
                    }
                }
            }
            catch { }
        }

        // 动画
        private void StartItemEntranceAnimation(FrameworkElement element, double delaySeconds)
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(delaySeconds);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var animation = new Storyboard();
                var opacityAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(opacityAnimation, element);
                Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
                var translateAnimation = new DoubleAnimation
                {
                    From = -15,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(translateAnimation, element.RenderTransform);
                Storyboard.SetTargetProperty(translateAnimation, new PropertyPath(TranslateTransform.YProperty));
                animation.Children.Add(opacityAnimation);
                animation.Children.Add(translateAnimation);
                animation.Begin();
            };
            timer.Start();
        }

        private Canvas CreateDeleteButton(JToken reading, Border container)
        {
            var deleteButtonContainer = new Canvas
            {
                Width = 60,
                Height = 60,
                Background = Brushes.Transparent,
                Opacity = 0
            };

            var backImage = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Clziyuan/back.png")),
                Width = 60,
                Height = 60,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false
            };

            var deleteImage = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Clziyuan/del.png")),
                Width = 48,
                Height = 48,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Cursor = Cursors.Hand,
                IsHitTestVisible = true
            };

            deleteImage.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
        {
            new ScaleTransform { ScaleX = 1, ScaleY = 1 },
            new SkewTransform(),
            new RotateTransform { Angle = 0 },
            new TranslateTransform()
        }
            };

            Canvas.SetLeft(deleteImage, 6);
            Canvas.SetTop(deleteImage, 6);

            deleteImage.MouseDown += (s, e) =>
            {
                PlayDeleteButtonAnimation(deleteImage);
                DeleteReading(reading, container);
            };

            deleteButtonContainer.Children.Add(backImage);
            deleteButtonContainer.Children.Add(deleteImage);

            return deleteButtonContainer;
        }

        private Border CreateCircleActionButton(string iconSource, string tooltip)
        {
            var button = new Border
            {
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x66, 0x7e, 0xea)),
                CornerRadius = new CornerRadius(18),
                Margin = new Thickness(6, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection
                    {
                        new ScaleTransform(1, 1),
                        new TranslateTransform()
                    }
                }
            };

            var iconImage = new Image
            {
                Source = new BitmapImage(new Uri($"pack://application:,,,/Clziyuan/{iconSource}")),
                Width = 20,
                Height = 20,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };

            button.Child = iconImage;

            // 鼠标悬停动画
            button.MouseEnter += (s, e) =>
            {
                var hoverAnimation = new Storyboard();
                var scaleAnimation = new DoubleAnimation(1.15, TimeSpan.FromSeconds(0.2));
                scaleAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                Storyboard.SetTarget(scaleAnimation, button);
                Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("RenderTransform.Children[0].ScaleX"));
                hoverAnimation.Children.Add(scaleAnimation);
                var scaleAnimationY = scaleAnimation.Clone();
                Storyboard.SetTarget(scaleAnimationY, button);
                Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("RenderTransform.Children[0].ScaleY"));
                hoverAnimation.Children.Add(scaleAnimationY);
                var bgAnimation = new ColorAnimation(
                    Color.FromArgb(0xFF, 0x66, 0x7e, 0xea),
                    TimeSpan.FromSeconds(0.2));
                Storyboard.SetTarget(bgAnimation, button);
                Storyboard.SetTargetProperty(bgAnimation, new PropertyPath("Background.Color"));
                hoverAnimation.Children.Add(bgAnimation);
                hoverAnimation.Begin();
            };

            button.MouseLeave += (s, e) =>
            {
                var leaveAnimation = new Storyboard();
                var scaleAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.2));
                scaleAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                Storyboard.SetTarget(scaleAnimation, button);
                Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("RenderTransform.Children[0].ScaleX"));
                leaveAnimation.Children.Add(scaleAnimation);
                var scaleAnimationY = scaleAnimation.Clone();
                Storyboard.SetTarget(scaleAnimationY, button);
                Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("RenderTransform.Children[0].ScaleY"));
                leaveAnimation.Children.Add(scaleAnimationY);
                var bgAnimation = new ColorAnimation(
                    Color.FromArgb(0xCC, 0x66, 0x7e, 0xea),
                    TimeSpan.FromSeconds(0.2));
                Storyboard.SetTarget(bgAnimation, button);
                Storyboard.SetTargetProperty(bgAnimation, new PropertyPath("Background.Color"));
                leaveAnimation.Children.Add(bgAnimation);
                leaveAnimation.Begin();
            };

            button.MouseLeftButtonDown += (s, e) =>
            {
                var clickAnimation = new Storyboard();
                var scaleAnimation = new DoubleAnimation(0.9, TimeSpan.FromSeconds(0.1));
                Storyboard.SetTarget(scaleAnimation, button);
                Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("RenderTransform.Children[0].ScaleX"));
                clickAnimation.Children.Add(scaleAnimation);
                var scaleAnimationY = scaleAnimation.Clone();
                Storyboard.SetTarget(scaleAnimationY, button);
                Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("RenderTransform.Children[0].ScaleY"));
                clickAnimation.Children.Add(scaleAnimationY);
                var iconScaleAnimation = new DoubleAnimation(0.8, TimeSpan.FromSeconds(0.1));
                Storyboard.SetTarget(iconScaleAnimation, iconImage);
                Storyboard.SetTargetProperty(iconScaleAnimation, new PropertyPath("RenderTransform.ScaleX"));
                clickAnimation.Children.Add(iconScaleAnimation);
                var iconScaleAnimationY = iconScaleAnimation.Clone();
                Storyboard.SetTarget(iconScaleAnimationY, iconImage);
                Storyboard.SetTargetProperty(iconScaleAnimationY, new PropertyPath("RenderTransform.ScaleY"));
                clickAnimation.Children.Add(iconScaleAnimationY);
                clickAnimation.Begin();
            };

            button.MouseLeftButtonUp += (s, e) =>
            {
                var releaseAnimation = new Storyboard();
                var scaleAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.1));
                Storyboard.SetTarget(scaleAnimation, button);
                Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("RenderTransform.Children[0].ScaleX"));
                releaseAnimation.Children.Add(scaleAnimation);
                var scaleAnimationY = scaleAnimation.Clone();
                Storyboard.SetTarget(scaleAnimationY, button);
                Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("RenderTransform.Children[0].ScaleY"));
                releaseAnimation.Children.Add(scaleAnimationY);
                var iconScaleAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.1));
                Storyboard.SetTarget(iconScaleAnimation, iconImage);
                Storyboard.SetTargetProperty(iconScaleAnimation, new PropertyPath("RenderTransform.ScaleX"));
                releaseAnimation.Children.Add(iconScaleAnimation);
                var iconScaleAnimationY = iconScaleAnimation.Clone();
                Storyboard.SetTarget(iconScaleAnimationY, iconImage);
                Storyboard.SetTargetProperty(iconScaleAnimationY, new PropertyPath("RenderTransform.ScaleY"));
                releaseAnimation.Children.Add(iconScaleAnimationY);
                releaseAnimation.Begin();
            };

            return button;
        }

        private void PlayActionPanelShowAnimation(StackPanel actionPanel)
        {
            var showAnimation = new Storyboard();
            var opacityAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.4));
            opacityAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(opacityAnimation, actionPanel);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            showAnimation.Children.Add(opacityAnimation);
            var scaleXAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.5));
            scaleXAnimation.EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };
            Storyboard.SetTarget(scaleXAnimation, actionPanel);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("RenderTransform.Children[0].ScaleX"));
            showAnimation.Children.Add(scaleXAnimation);
            var scaleYAnimation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.5));
            scaleYAnimation.EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };
            Storyboard.SetTarget(scaleYAnimation, actionPanel);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("RenderTransform.Children[0].ScaleY"));
            showAnimation.Children.Add(scaleYAnimation);
            var translateAnimation = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
            translateAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(translateAnimation, actionPanel);
            Storyboard.SetTargetProperty(translateAnimation, new PropertyPath("RenderTransform.Children[1].Y"));
            showAnimation.Children.Add(translateAnimation);
            showAnimation.Begin();
        }

        private void PlayActionPanelHideAnimation(StackPanel actionPanel)
        {
            var hideAnimation = new Storyboard();
            var opacityAnimation = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            opacityAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(opacityAnimation, actionPanel);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            hideAnimation.Children.Add(opacityAnimation);
            hideAnimation.Begin();
        }

        // 判断是否有新早晚读
        private bool HasNewReading(JArray newReadings)
        {
            if (newReadings == null) return false;
            if (_currentReadings == null || _currentReadings.Count == 0) return true;
            if (newReadings.Count != _currentReadings.Count) return true;
            for (int i = 0; i < newReadings.Count; i++)
            {
                var newNotice = newReadings[i];
                var currentNotice = _currentReadings[i];
                var newId = newNotice["objectId"]?.ToString();
                var currentId = currentNotice["objectId"]?.ToString();
                if (newId != currentId) return true;
                var newUpdatedAt = newNotice["updatedAt"]?.ToString();
                var currentUpdatedAt = currentNotice["updatedAt"]?.ToString();
                if (newUpdatedAt != currentUpdatedAt) return true;
                var newContent = newNotice["content"]?.ToString();
                var currentContent = currentNotice["content"]?.ToString();
                if (newContent != currentContent) return true;
                var newTitle = newNotice["title"]?.ToString();
                var currentTitle = currentNotice["title"]?.ToString();
                if (newTitle != currentTitle) return true;
            }
            return false;
        }

        // 只更新时间
        private void UpdateDeadlineTimersOnly()
        {
            foreach (var child in ReadingListPanel.Children)
            {
                if (child is Border border && border.Tag is Tuple<TextBlock, DateTime, DateTime, ReadingContainerData, string> info)
                {
                    var (timeText, startTime, endTime, containerData, durationStr) = info;
                    var readingType = DetermineReadingType(startTime, "");
                    UpdateReadingTimeDisplay(timeText, startTime, endTime, durationStr, readingType);
                }
            }
        }

        private void ShowToast(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        protected override void OnClosed(EventArgs e)
        {
           
            _updateTimer?.Stop();
            _timeRefreshTimer?.Stop();
            base.OnClosed(e);
        }
        // 关闭/打开按钮点击事件处理
        private void ReadingCloseOpenImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PlayReadingCloseOpenClickAnimation();

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.2);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                if (_isWindowMinimized)
                {
                    PlayReadingWindowExpandAnimation();
                    ReadingCloseOpenImage.Source = new BitmapImage(new Uri("pack://application:,,,/Clziyuan/guanbi.png"));
                    _isWindowMinimized = false;
                }
                else
                {
                    PlayReadingWindowShrinkAnimation();
                    ReadingCloseOpenImage.Source = new BitmapImage(new Uri("pack://application:,,,/Clziyuan/dakai.png"));
                    _isWindowMinimized = true;
                }
            };
            timer.Start();
        }

        // 播放关闭/打开按钮点击动画
        private void PlayReadingCloseOpenClickAnimation()
        {
            Storyboard closeOpenAnimation = (Storyboard)this.FindResource("ReadingCloseOpenClickAnimation");
            if (closeOpenAnimation != null)
            {
                closeOpenAnimation.Begin(this);
            }
        }

        // 播放窗体收缩动画
        private void PlayReadingWindowShrinkAnimation()
        {
            Storyboard shrinkAnimation = (Storyboard)this.FindResource("ReadingWindowShrinkAnimation");
            if (shrinkAnimation != null)
            {
                shrinkAnimation.Begin(this);
            }
        }

        // 播放窗体展开动画
        private void PlayReadingWindowExpandAnimation()
        {
            Storyboard expandAnimation = (Storyboard)this.FindResource("ReadingWindowExpandAnimation");
            if (expandAnimation != null)
            {
                expandAnimation.Begin(this);
            }
        }
        public void EnsureVisible()
        {
            this.Visibility = Visibility.Visible;
            this.Opacity = 0.95;
            this.Topmost = true;
            this.Activate();

            // 如果窗口被最小化，恢复正常状态
            if (_isWindowMinimized)
            {
                PlayReadingWindowExpandAnimation();
                ReadingCloseOpenImage.Source = new BitmapImage(new Uri("pack://application:,,,/Clziyuan/guanbi.png"));
                _isWindowMinimized = false;
            }
        }

        public void RefreshReadingData()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 手动刷新早晚读数据");
            LoadReadingData();
            ShowToast("早晚读已刷新");
        }


        // 通过反射获取作业窗口的列表面板（需要根据实际结构调整）
        // 通过反射获取作业窗口的列表面板
        private Panel GetHomeworkListPanel()
        {
            try
            {
                if (_homeworkWindow == null) return null;

                // 方法1: 通过名称查找
                var homeworkListPanel = _homeworkWindow.FindName("HomeworkListPanel") as Panel;
                if (homeworkListPanel != null) return homeworkListPanel;

                // 方法2: 遍历可视化树查找
                return FindChild<Panel>(_homeworkWindow, "HomeworkListPanel");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取作业列表面板错误: {ex.Message}");
                return null;
            }
        }

        // 在可视化树中查找子元素
        private T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                T childType = child as T;

                if (childType == null)
                {
                    foundChild = FindChild<T>(child, childName);
                    if (foundChild != null) break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                    else
                    {
                        foundChild = FindChild<T>(child, childName);
                        if (foundChild != null) break;
                    }
                }
                else
                {
                    foundChild = (T)child;
                    break;
                }
            }
            return foundChild;
        }
        // 在 ReadingWindow 类中添加

        // 从ClassManagerWindow更新位置的方法
        public void UpdatePositionFromClassManager(ClassManagerWindow classManagerWindow)
        {
            if (classManagerWindow == null || _homeworkWindow == null) return;

            try
            {
                // 通过作业窗口来同步位置，保持一致性
                UpdateReadingWindowPosition();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从ClassManager更新位置错误: {ex.Message}");
            }
        }
        // 计算作业窗口内容高度（需要访问HomeworkWindow）
        private double CalculateHomeworkContentHeight()
        {
            try
            {
                var homeworkWindow = Application.Current.Windows.OfType<HomeworkWindow>().FirstOrDefault();
                if (homeworkWindow != null)
                {
                    // 使用反射或其他方式获取作业内容高度
                    return homeworkWindow.GetContentHeight();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"计算作业内容高度错误: {ex.Message}");
            }
            return 0;
        }
        // 在 ReadingWindow 类中添加平滑位置动画方法
        private void AnimateReadingWindowPosition(double targetLeft, double targetTop, double targetWidth)
        {
            // 如果正在更新位置或变化很小，不执行动画
            if (_isUpdatingPosition ||
                (Math.Abs(this.Left - targetLeft) < 1 &&
                 Math.Abs(this.Top - targetTop) < 1 &&
                 Math.Abs(this.Width - targetWidth) < 1))
            {
                return;
            }

            _isUpdatingPosition = true;

            var positionAnimation = new Storyboard();

            // 左位置动画
            var leftAnimation = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(350));
            leftAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(leftAnimation, this);
            Storyboard.SetTargetProperty(leftAnimation, new PropertyPath(LeftProperty));
            positionAnimation.Children.Add(leftAnimation);

            // 上位置动画
            var topAnimation = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(350));
            topAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(topAnimation, this);
            Storyboard.SetTargetProperty(topAnimation, new PropertyPath(TopProperty));
            positionAnimation.Children.Add(topAnimation);

            // 宽度动画
            var widthAnimation = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(350));
            widthAnimation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(widthAnimation, this);
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath(WidthProperty));
            positionAnimation.Children.Add(widthAnimation);

            // 动画完成后重置状态
            positionAnimation.Completed += (s, e) =>
            {
                _isUpdatingPosition = false;
                _lastPositionUpdate = DateTime.Now;
            };

            positionAnimation.Begin();
        }

        // 刷新时间显示
        private void RefreshTimeDisplay()
        {
            foreach (var child in ReadingListPanel.Children)
            {
                if (child is Border border && border.Tag is Tuple<DateTime, DateTime, ReadingContainerData, string, Border, string> info)
                {
                    var startTime = info.Item1;
                    var endTime = info.Item2;
                    var containerData = info.Item3;
                    var durationStr = info.Item4;
                    var titleBorder = info.Item5;
                    var readingType = info.Item6;

                    // 重新计算状态
                    var (statusText, statusColor, timeLeftText) = CalculateReadingStatus(startTime, endTime, durationStr, readingType);

                    // 更新标题栏中的状态显示
                    Dispatcher.Invoke(() =>
                    {
                        if (titleBorder.Child is StackPanel titlePanel && titlePanel.Children.Count >= 2)
                        {
                            // 更新第一行（标题和状态）
                            if (titlePanel.Children[0] is StackPanel titleRow && titleRow.Children.Count >= 2)
                            {
                                // 更新状态标签
                                if (titleRow.Children[1] is Border statusBorder && statusBorder.Child is TextBlock statusTextBlock)
                                {
                                    statusTextBlock.Text = statusText;
                                    statusBorder.Background = statusColor;
                                }
                            }

                            // 更新第二行（时间信息）
                            if (titlePanel.Children[1] is StackPanel timeInfoRow && timeInfoRow.Children.Count >= 2)
                            {
                                // 更新时间文本
                                if (timeInfoRow.Children[1] is TextBlock timeTextBlock)
                                {
                                    timeTextBlock.Text = timeLeftText;
                                }
                            }
                        }
                    });
                }
            }
        }

        // 启动时间刷新定时器
        private void StartTimeRefresh()
        {
            _timeRefreshTimer = new DispatcherTimer();
            _timeRefreshTimer.Interval = TimeSpan.FromSeconds(1);
            _timeRefreshTimer.Tick += (s, e) => RefreshTimeDisplay();
            _timeRefreshTimer.Start();
        }
        // 计算早晚读状态
        // 计算早晚读状态
        // 计算早晚读状态
        private (string statusText, SolidColorBrush statusColor, string timeLeftText) CalculateReadingStatus(DateTime startTime, DateTime endTime, string durationStr, string readingType)
        {
            var now = DateTime.Now;
            var readingTypeName = GetReadingTypeDisplayName(readingType);

            // 调试信息
            Console.WriteLine($"计算状态 - 开始时间: {startTime}, 结束时间: {endTime}, 持续时间: {durationStr}, 类型: {readingType}");

            if (startTime != DateTime.MinValue && endTime != DateTime.MinValue)
            {
                // 有明确的开始和结束时间
                if (now < startTime)
                {
                    var timeLeft = startTime - now;
                    return ("等待开始", new SolidColorBrush(Colors.SteelBlue), $"距离开始还有 {FormatDetailedTimeSpan(timeLeft)}");
                }
                else if (now < endTime)
                {
                    var timeLeft = endTime - now;
                    return ("进行中", new SolidColorBrush(Colors.Green), $"距离结束还有 {FormatDetailedTimeSpan(timeLeft)}");
                }
                else
                {
                    return ("已结束", new SolidColorBrush(Colors.Gray), $"{readingTypeName}已结束");
                }
            }
            else if (!string.IsNullOrEmpty(durationStr) && int.TryParse(durationStr, out int durationMinutes))
            {
                // 只有持续时间
                if (startTime != DateTime.MinValue)
                {
                    endTime = startTime.AddMinutes(durationMinutes);
                    if (now < startTime)
                    {
                        var timeLeft = startTime - now;
                        return ("等待开始", new SolidColorBrush(Colors.SteelBlue), $"距离开始还有 {FormatDetailedTimeSpan(timeLeft)}");
                    }
                    else if (now < endTime)
                    {
                        var timeLeft = endTime - now;
                        return ("进行中", new SolidColorBrush(Colors.Green), $"距离结束还有 {FormatDetailedTimeSpan(timeLeft)}");
                    }
                    else
                    {
                        return ("已结束", new SolidColorBrush(Colors.Gray), $"{readingTypeName}已结束");
                    }
                }
                else
                {
                    return ("自定义", new SolidColorBrush(Colors.Purple), $"持续时间: {durationMinutes}分钟");
                }
            }
            else if (startTime != DateTime.MinValue)
            {
                // 只有开始时间
                if (now < startTime)
                {
                    var timeLeft = startTime - now;
                    return ("等待开始", new SolidColorBrush(Colors.SteelBlue), $"距离开始还有 {FormatDetailedTimeSpan(timeLeft)}");
                }
                else
                {
                    return ("已开始", new SolidColorBrush(Colors.Orange), $"{readingTypeName}已开始");
                }
            }
            else
            {
                // 没有时间信息
                Console.WriteLine("没有找到有效的时间信息");
                return ("无时间", new SolidColorBrush(Colors.DarkGray), $"{readingTypeName}");
            }
        }
        // 获取早晚读类型显示名称
        private string GetReadingTypeDisplayName(string readingType)
        {
            switch (readingType)
            {
                case "morning":
                    return "早读";
                case "evening":
                    return "晚读";
                case "custom":
                    return "自定义";
                default:
                    return "阅读";
            }
        }
    }
}