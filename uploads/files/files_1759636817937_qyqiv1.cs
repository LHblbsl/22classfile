using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization; // 添加这个命名空间
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data; // 添加这个命名空间
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace testdemo
{
    public class UpStudentBorderThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUpStudent && isUpStudent)
            {
                return new Thickness(5); // UP学生边框加粗到5像素
            }
            return new Thickness(2); // 普通学生2像素边框
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class UpStudentBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUpStudent && isUpStudent)
            {
                return new SolidColorBrush(Colors.Gold); // UP学生金色边框
            }
            return new SolidColorBrush(Colors.White); // 普通学生白色边框
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class UpStudentForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUpStudent && isUpStudent)
            {
                return new SolidColorBrush(Colors.Gold); // UP学生金色文字
            }
            return new SolidColorBrush(Colors.White); // 普通学生白色文字
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class NumberSettingsWindow : Window
    {
        private bool _isAnimating = false;
        public SolidColorBrush DarkerThemeBrush { get; private set; }
        private SolidColorBrush _originalButtonBrush;
        private SecondWindow _secondWindow;
        private bool _isProcessingClick = false; // 添加防重复点
        // 添加数据模型和集合
       

        public ObservableCollection<StudentCard> StudentCards { get; private set; }

        public NumberSettingsWindow()
        {
            InitializeComponent();

            // 初始化集合
            StudentCards = new ObservableCollection<StudentCard>();
            this.DataContext = this;

            DarkerThemeBrush = new SolidColorBrush(Colors.DarkBlue);
            _originalButtonBrush = new SolidColorBrush(Colors.DarkBlue);

            // 设置初始高度和宽度为0
            this.Height = 0;
            settingsCapsuleBorder.Height = 0;
            settingsCapsuleBorder.Width = 0;
            settingsCapsuleBorder.Visibility = Visibility.Collapsed;

            // 获取SecondWindow实例
            _secondWindow = Application.Current.Windows.OfType<SecondWindow>().FirstOrDefault();

            // 事件订阅 - 确保只订阅一次
            closeImage.MouseDown += CloseImage_MouseDown;
            applyButton.MouseEnter += ApplyButton_MouseEnter;
            applyButton.MouseLeave += ApplyButton_MouseLeave;
            applyButton.MouseDown += ApplyButton_MouseDown;
            applyButton.Click += ApplyButton_Click; // 只保留这一个

            // 在Loaded事件中只播放动画，不重复订阅事件
            this.Loaded += (s, e) =>
            {
                settingsCapsuleBorder.Visibility = Visibility.Visible;
                PlayExpandAnimation();
            };

            // 订阅集合变化事件
            StudentCards.CollectionChanged += StudentCards_CollectionChanged;

            // 设置触屏滚动支持
            InitializeTouchScrolling();
        }

        private void InitializeTouchScrolling()
        {
            // 为触屏设备启用滚动支持
            resultsScrollViewer.PanningMode = PanningMode.VerticalOnly;
            resultsScrollViewer.PanningDeceleration = 0.001;
        }

        private void StudentCards_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // 延迟播放动画，确保UI元素完全创建
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (StudentCard item in e.NewItems)
                    {
                        // 使用更可靠的方法获取容器
                        var itemContainer = resultsItemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                        if (itemContainer != null)
                        {
                            PlayCardAppearAnimation(itemContainer);
                        }
                        else
                        {
                            // 如果容器还未生成，等待布局更新
                            resultsItemsControl.UpdateLayout();
                            itemContainer = resultsItemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                            if (itemContainer != null)
                            {
                                PlayCardAppearAnimation(itemContainer);
                            }
                        }
                    }
                }), DispatcherPriority.Loaded);
            }
        }

        private void PlayCardAppearAnimation(FrameworkElement cardElement)
        {
            // 直接从资源创建新的动画实例，避免资源共享冲突
            var storyboard = new Storyboard();

            // 缩放X动画
            var scaleXAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 3 }
            };
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("RenderTransform.ScaleX"));

            // 缩放Y动画
            var scaleYAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 3 }
            };
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("RenderTransform.ScaleY"));

            // 透明度动画
            var opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4)
            };
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Children.Add(opacityAnimation);

            // 确保目标元素的 RenderTransform 是 ScaleTransform
            if (cardElement.RenderTransform is not ScaleTransform)
            {
                cardElement.RenderTransform = new ScaleTransform(0, 0);
            }

            Storyboard.SetTarget(storyboard, cardElement);
            storyboard.Begin();
        }

        // 删除重复的 ClearResults 方法，只保留一个
        public async void ClearResults()
        {
            StudentCards.Clear();

            // 使用动画重置高度
            await AnimateHeightChange(120);

            resultsScrollViewer.Visibility = Visibility.Collapsed;
            Console.WriteLine("已清除所有结果，重置高度");
        }

        // 修改 ApplyButton_Click 方法 - 修复重复添加问题
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // 防止重复点击
            if (_isProcessingClick)
            {
                Console.WriteLine("点击正在处理中，跳过重复点击");
                return;
            }

            _isProcessingClick = true;
            Guid callId = Guid.NewGuid();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 开始多人抽选流程 ===");
            Console.WriteLine($"调用ID: {callId}");
            Console.ResetColor();

            try
            {
                if (int.TryParse(numberTextBox.Text, out int count) && count > 0)
                {
                    if (_secondWindow != null)
                    {
                        try
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"开始抽选 {count} 人...");
                            Console.ResetColor();

                            applyButton.IsEnabled = false;

                            // 只在新一轮抽取前清空
                            StudentCards.Clear();

                            List<int> allResults = new List<int>();
                            List<int> upResults = new List<int>(); // 记录UP学生

                            if (count <= 3)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.WriteLine($"--- 第{i + 1}次抽选 ---");
                                    Console.ResetColor();

                                    _secondWindow.SetState(SecondWindow.WindowState.Start);
                                    await _secondWindow.DrawSingleStudentWithAnimationAsync();

                                    int selectedStudentId = _secondWindow.GetSelectedStudentId();
                                    if (selectedStudentId != -1 && !allResults.Contains(selectedStudentId))
                                    {
                                        allResults.Add(selectedStudentId);

                                        // 检查是否是UP学生
                                        if (_secondWindow.IsStudentInCurrentUpPool(selectedStudentId))
                                        {
                                            upResults.Add(selectedStudentId);
                                            Console.ForegroundColor = ConsoleColor.Magenta;
                                            Console.WriteLine($"🎉 UP学生: {_secondWindow.GetStudentName(selectedStudentId)}({selectedStudentId})");
                                            Console.ResetColor();
                                        }
                                        else
                                        {
                                            Console.ForegroundColor = ConsoleColor.Green;
                                            Console.WriteLine($"普通学生: {_secondWindow.GetStudentName(selectedStudentId)}({selectedStudentId})");
                                            Console.ResetColor();
                                        }
                                    }


                                    if (i < count - 1)
                                    {
                                        await Task.Delay(800);
                                    }
                                }
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine($"批量抽选 {count} 人...");
                                Console.ResetColor();

                                _secondWindow.SetState(SecondWindow.WindowState.Middle);

                                // 使用带UP机制的批量抽选方法
                                var results = await _secondWindow.DrawMultipleStudentsWithUpAsync(count);

                                // 分离UP学生和普通学生
                                foreach (var studentId in results)
                                {
                                    if (_secondWindow.IsStudentInCurrentUpPool(studentId))
                                    {
                                        upResults.Add(studentId);
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.WriteLine($"🎉 UP学生: {_secondWindow.GetStudentName(studentId)}({studentId})");
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"普通学生: {_secondWindow.GetStudentName(studentId)}({studentId})");
                                        Console.ResetColor();
                                    }
                                }

                                allResults = results;

                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"批量抽选完成，最终结果数量: {allResults.Count}");
                                Console.ResetColor();
                            }

                            // 输出UP统计结果
                            if (upResults.Count > 0)
                            {
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.WriteLine("=== UP学生统计 ===");
                                Console.WriteLine($"本次抽选共获得 {upResults.Count} 名UP学生:");
                                foreach (var upId in upResults)
                                {
                                    Console.WriteLine($"🎉 {_secondWindow.GetStudentName(upId)}({upId})");
                                }
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("本次抽选未获得UP学生");
                                Console.ResetColor();
                            }

                            // 在 ApplyButton_Click 方法中找到添加卡片的代码
                            foreach (var studentId in allResults)
                            {
                                // 检查是否为UP学生
                                bool isUp = upResults.Contains(studentId);
                                AddStudentCard(studentId, isUp);
                                await Task.Delay(100); // 动画间隔
                            }

                            // 动态调整高度
                            if (StudentCards.Count > 0)
                            {
                                int rows = (StudentCards.Count + 3) / 4;
                                int newHeight = 120 + (rows * 200) + 20;
                                newHeight = Math.Min(newHeight, 600);
                                await AnimateHeightChange(newHeight);
                            }

                            resultsScrollViewer.Visibility = Visibility.Visible;
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"抽取过程中出现错误: {ex.Message}");
                            Console.ResetColor();
                            MessageBox.Show($"抽取过程中出现错误: {ex.Message}");
                        }
                        finally
                        {
                            applyButton.IsEnabled = true;
                        }
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("请输入有效的正整数值");
                    Console.ResetColor();
                    MessageBox.Show("请输入有效的正整数值");
                }
            }
            finally
            {
                _isProcessingClick = false;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"=== 多人抽选流程结束 ===");
                Console.WriteLine($"调用ID: {callId}");
                Console.ResetColor();
            }
        }

        private async void AddStudentCard(int studentId, bool isUpStudent = false)
        {
            if (_secondWindow.GetAllStudents().TryGetValue(studentId, out string studentName))
            {
                // 检查是否已存在该学生的卡片
                if (StudentCards.Any(card => card.StudentId == studentId))
                {
                    Console.WriteLine($"学生 {studentId} 已存在，跳过添加");
                    return;
                }

                var studentCard = new StudentCard
                {
                    StudentId = studentId,
                    StudentName = studentName,
                    Avatar = SecondWindow.GetAvatarFromCacheStatic(studentId),
                    IsUpStudent = isUpStudent // 设置UP状态
                };

                StudentCards.Add(studentCard);
                Console.WriteLine($"添加学生卡片: {studentId} - {studentName} {(isUpStudent ? "🎉 UP学生" : "")}");

                // 等待UI更新完成
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // 获取容器并播放动画
                var itemContainer = resultsItemsControl.ItemContainerGenerator.ContainerFromItem(studentCard) as FrameworkElement;
                if (itemContainer != null)
                {
                    PlayCardAppearAnimation(itemContainer);
                }
                else
                {
                    // 如果容器还未生成，等待布局更新
                    resultsItemsControl.UpdateLayout();
                    await Task.Delay(50);
                    itemContainer = resultsItemsControl.ItemContainerGenerator.ContainerFromItem(studentCard) as FrameworkElement;
                    if (itemContainer != null)
                    {
                        PlayCardAppearAnimation(itemContainer);
                    }
                }

                // 调试输出
                Console.WriteLine($"当前卡片数量: {StudentCards.Count}");
            }
        }

        private void PlayCloseAnimation()
        {
            _isAnimating = true;
            var storyboard = (Storyboard)FindResource("CloseWindowAnimation");
            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
                this.Close();
            };
            storyboard.Begin();
        }

        private void CloseImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating) return;

            var storyboard = (Storyboard)FindResource("CloseImageClickAnimation");
            storyboard.Completed += CloseButton_AnimationCompleted;
            Storyboard.SetTarget(storyboard, closeImage);
            storyboard.Begin();
        }

        private void CloseButton_AnimationCompleted(object sender, EventArgs e)
        {
            PlayCloseAnimation();
        }

        public void PositionBelowSecondWindow(SecondWindow secondWindow)
        {
            double secondWindowLeft = secondWindow.Left;
            double secondWindowTop = secondWindow.Top;
            double secondWindowWidth = secondWindow.Width;
            double secondWindowHeight = secondWindow.ActualHeight;

            double newLeft = secondWindowLeft + (secondWindowWidth - this.Width) / 2;
            double newTop = secondWindowTop + secondWindowHeight + 20;

            this.Left = newLeft;
            this.Top = newTop;
        }

        public void UpdateThemeColor(Color color)
        {
            color.A = 128;
            settingsCapsuleBorder.Background = new SolidColorBrush(color);

            Color darkerColor = Color.FromRgb(
                (byte)(color.R * 0.7),
                (byte)(color.G * 0.7),
                (byte)(color.B * 0.7)
            );

            DarkerThemeBrush = new SolidColorBrush(darkerColor);
            _originalButtonBrush = new SolidColorBrush(darkerColor);

            applyButton.Background = _originalButtonBrush;
            inputBorder.Background = _originalButtonBrush;
        }

        private void PlayExpandAnimation()
        {
            _isAnimating = true;

            // 确保窗口初始状态正确
            this.Height = 0;
            settingsCapsuleBorder.Height = 0;
            settingsCapsuleBorder.Width = 0;
            settingsCapsuleBorder.Visibility = Visibility.Visible;

            // 初始裁剪区域设置为0
            UpdateCapsuleClip(0);

            // 创建新的展开动画（不使用资源中的，因为资源中的目标值不对）
            var storyboard = new Storyboard();

            // 宽度动画
            var widthAnimation = new DoubleAnimation
            {
                From = 0,
                To = 800,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 3 }
            };
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath("Width"));
            Storyboard.SetTarget(widthAnimation, settingsCapsuleBorder);

            // 高度动画（展开到120的基础高度）
            var heightAnimation = new DoubleAnimation
            {
                From = 0,
                To = 120,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 3 }
            };
            Storyboard.SetTargetProperty(heightAnimation, new PropertyPath("Height"));
            Storyboard.SetTarget(heightAnimation, settingsCapsuleBorder);

            // 窗口高度动画
            var windowHeightAnimation = new DoubleAnimation
            {
                From = 0,
                To = 120,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 3 }
            };
            Storyboard.SetTargetProperty(windowHeightAnimation, new PropertyPath("Height"));
            Storyboard.SetTarget(windowHeightAnimation, this);

            storyboard.Children.Add(widthAnimation);
            storyboard.Children.Add(heightAnimation);
            storyboard.Children.Add(windowHeightAnimation);

            // 在动画过程中实时更新裁剪区域
            storyboard.CurrentTimeInvalidated += (s, e) =>
            {
                var currentHeight = settingsCapsuleBorder.Height;
                UpdateCapsuleClip(currentHeight);
            };

            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
                // 确保最终状态正确
                UpdateCapsuleClip(120);
            };
            storyboard.Begin();
        }

        private void UpdateCapsuleClip(double height)
        {
            double actualHeight = Math.Max(height, 0); // 确保高度不为负
            double actualWidth = settingsCapsuleBorder.ActualWidth > 0 ? settingsCapsuleBorder.ActualWidth : settingsCapsuleBorder.Width;

            // 创建新的裁剪区域
            var clipGeometry = new RectangleGeometry
            {
                RadiusX = 45,
                RadiusY = 45,
                Rect = new Rect(0, 0, actualWidth, actualHeight)
            };

            settingsCapsuleBorder.Clip = clipGeometry;
        }

        // 鼠标事件处理
        private void ApplyButton_MouseEnter(object sender, MouseEventArgs e)
        {
            applyButton.Background = DarkerThemeBrush;
        }

        private void ApplyButton_MouseLeave(object sender, MouseEventArgs e)
        {
            applyButton.Background = _originalButtonBrush;
        }

        private void ApplyButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            applyButton.Opacity = 0.8;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        private async Task AnimateHeightChange(double targetHeight)
        {
            if (_isAnimating) return;

            _isAnimating = true;

            // 创建高度动画
            var heightAnimation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // 同时动画窗口和边框的高度
            var storyboard = new Storyboard();
            storyboard.Children.Add(heightAnimation);

            // 动画窗口高度
            Storyboard.SetTarget(heightAnimation, this);
            Storyboard.SetTargetProperty(heightAnimation, new PropertyPath(HeightProperty));

            // 动画边框高度
            var borderHeightAnimation = heightAnimation.Clone();
            Storyboard.SetTarget(borderHeightAnimation, settingsCapsuleBorder);
            Storyboard.SetTargetProperty(borderHeightAnimation, new PropertyPath(HeightProperty));
            storyboard.Children.Add(borderHeightAnimation);

            // 动画裁剪区域（实时更新）
            storyboard.CurrentTimeInvalidated += (s, e) =>
            {
                var currentHeight = settingsCapsuleBorder.Height;
                UpdateCapsuleClip(currentHeight);
            };

            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
                // 确保最终状态正确
                UpdateCapsuleClip(targetHeight);
            };

            storyboard.Begin();

            // 等待动画完成
            await Task.Delay(heightAnimation.Duration.TimeSpan);
        }

        // 在 NumberSettingsWindow 类中添加转换器
      

        public class StudentCard
        {
            public int StudentId { get; set; }
            public string StudentName { get; set; }
            public ImageSource Avatar { get; set; }
            public bool IsUpStudent { get; set; } // 新增属性，标识是否为UP学生
            public string DisplayText => $"{StudentName}\n{StudentId}";
        }
    }
}