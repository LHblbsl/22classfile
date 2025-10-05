using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace testdemo
{
    public partial class HistoryWindow : Window
    {
        public class HistoryItem
        {
            public string DisplayText { get; set; }
            public ImageSource Avatar { get; set; }
            public bool IsUpStudent { get; set; } // 新增：标识是否为UP学生
        }

        private Point dragStartPoint;
        private bool isClosing = false;
        private bool isDragging = false;
        private ScrollBar customScrollBar;

        public HistoryWindow()
        {
            InitializeComponent();
            CreateCustomScrollBar();
            InitializeButtonAnimations();
            UpdateThemeColor(Settings.ThemeColor);

        }

        private void CreateCustomScrollBar()
        {
            // 创建自定义滚动条 - 使用更简单的方法
            customScrollBar = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Width = 10,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Visibility = Visibility.Hidden
            };

            // 使用XAML字符串动态创建模板
            string xamlTemplate = @"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' 
                                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                                TargetType='{x:Type ScrollBar}'>
                    <Grid SnapsToDevicePixels='True'>
                        <Border Background='#20000000' CornerRadius='5' Margin='2,0'/>
                        <Track x:Name='PART_Track' IsDirectionReversed='True'>
                            <Track.Thumb>
                                <Thumb x:Name='Thumb' Background='#60000000' BorderBrush='Transparent'>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='{x:Type Thumb}'>
                                            <Border CornerRadius='5' Background='{TemplateBinding Background}' Margin='2,0'/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>";

            // 加载XAML模板
            var template = System.Windows.Markup.XamlReader.Parse(xamlTemplate) as ControlTemplate;
            customScrollBar.Template = template;

            // 添加到界面
            if (historyScrollViewer.Parent is Grid gridParent)
            {
                gridParent.Children.Add(customScrollBar);
                Grid.SetRow(customScrollBar, 1);
                customScrollBar.HorizontalAlignment = HorizontalAlignment.Right;
                customScrollBar.VerticalAlignment = VerticalAlignment.Stretch;
                customScrollBar.Margin = new Thickness(0, 0, 20, 20);
            }

            // 绑定事件
            customScrollBar.Scroll += OnCustomScrollBarScroll;
            historyScrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }

        protected override void OnContentRendered(System.EventArgs e)
        {
            base.OnContentRendered(e);
            PlayOpenAnimation();

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                UpdateScrollBarVisibility();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void UpdateScrollBarVisibility()
        {
            if (customScrollBar != null && historyScrollViewer != null)
            {
                if (historyScrollViewer.ExtentHeight > historyScrollViewer.ViewportHeight)
                {
                    customScrollBar.Visibility = Visibility.Visible;
                    customScrollBar.Maximum = historyScrollViewer.ExtentHeight - historyScrollViewer.ViewportHeight;
                    customScrollBar.ViewportSize = historyScrollViewer.ViewportHeight;
                }
                else
                {
                    customScrollBar.Visibility = Visibility.Hidden;
                }
            }
        }

        private void OnCustomScrollBarScroll(object sender, ScrollEventArgs e)
        {
            historyScrollViewer.ScrollToVerticalOffset(e.NewValue);
        }

        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (customScrollBar != null)
            {
                customScrollBar.Value = e.VerticalOffset;

                if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                {
                    UpdateScrollBarVisibility();
                }
            }
        }

        public void UpdateThemeColor(Color color)
        {
            color.A = 128;
            historyCapsuleBorder.Background = new SolidColorBrush(color);

            // 更新清空按钮颜色 - 使用比主题色深一点的颜色
            var darkerColor = Color.FromArgb(255, color.R, color.G, color.B);
            darkerColor.R = (byte)(darkerColor.R * 0.7);
            darkerColor.G = (byte)(darkerColor.G * 0.7);
            darkerColor.B = (byte)(darkerColor.B * 0.7);

            if (clearButtonBorder != null)
            {
                clearButtonBorder.Background = new SolidColorBrush(darkerColor);

                // 边框颜色再深一点
                var borderColor = Color.FromArgb(255,
                    (byte)(darkerColor.R * 0.8),
                    (byte)(darkerColor.G * 0.8),
                    (byte)(darkerColor.B * 0.8));
                clearButtonBorder.BorderBrush = new SolidColorBrush(borderColor);
            }

            // 更新滚动条颜色
            var scrollColor = Color.FromArgb(150, color.R, color.G, color.B);
            if (customScrollBar != null)
            {
                if (customScrollBar.Template?.FindName("Thumb", customScrollBar) is Thumb thumb)
                {
                    thumb.Background = new SolidColorBrush(scrollColor);
                }
            }
        }

        public void LoadHistory(List<int> history, Dictionary<int, string> studentData, List<int> upStudents = null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== 加载历史记录到历史窗口 ===");
            Console.WriteLine($"历史记录数量: {history?.Count ?? 0}");
            Console.WriteLine($"UP学生数量: {upStudents?.Count ?? 0}");

            if (upStudents != null && upStudents.Count > 0)
            {
                Console.WriteLine($"UP学生列表: {string.Join(", ", upStudents.Take(10))}" +
                                 (upStudents.Count > 10 ? "..." : ""));
            }
            Console.ResetColor();

            var historyItems = new List<HistoryItem>();

            foreach (int studentId in history)
            {
                if (studentData.ContainsKey(studentId))
                {
                    string studentName = studentData[studentId];
                    bool isUpStudent = upStudents?.Contains(studentId) ?? false;

                    // 调试日志：标记为UP的学生
                    if (isUpStudent)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"标记为UP: {studentName} ({studentId})");
                        Console.ResetColor();
                    }

                    var item = new HistoryItem
                    {
                        DisplayText = isUpStudent ? $"{studentName} ({studentId}) ★UP" : $"{studentName} ({studentId})",
                        Avatar = SecondWindow.GetAvatarFromCacheStatic(studentId),
                        IsUpStudent = isUpStudent
                    };
                    historyItems.Add(item);
                }
            }

            historyItemsControl.ItemsSource = historyItems;

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                UpdateScrollBarVisibility();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // 其他方法保持不变...
        public void PositionBelowSecondWindow(Window secondWindow)
        {
            this.Left = secondWindow.Left;
            this.Top = secondWindow.Top + secondWindow.Height + 20;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !isClosing)
            {
                StartDrag(e);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !isClosing)
            {
                StartDrag(e);
            }
        }

        private void StartDrag(MouseButtonEventArgs e)
        {
            isDragging = true;
            dragStartPoint = e.GetPosition(this);
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                this.Left += currentPoint.X - dragStartPoint.X;
                this.Top += currentPoint.Y - dragStartPoint.Y;
            }
            else
            {
                isDragging = false;
                ReleaseMouseCapture();
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            ReleaseMouseCapture();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;

            if (customScrollBar != null)
            {
                customScrollBar.Value = scrollViewer.VerticalOffset;
            }
        }

        protected override void OnManipulationBoundaryFeedback(ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isClosing)
            {
                var clickAnimation = (Storyboard)FindResource("CloseButtonClickAnimation");
                clickAnimation.Begin(closeButton);

                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = System.TimeSpan.FromMilliseconds(100);
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    PlayCloseAnimation();
                };
                timer.Start();
            }
        }

        private void PlayOpenAnimation()
        {
            var openAnimation = (Storyboard)FindResource("OpenAnimation");
            openAnimation.Begin(historyCapsuleBorder);
        }

        private void PlayCloseAnimation()
        {
            isClosing = true;
            var closeAnimation = (Storyboard)FindResource("CloseAnimation");
            closeAnimation.Begin(historyCapsuleBorder);
        }

        private void CloseAnimation_Completed(object sender, System.EventArgs e)
        {
            this.Close();
        }
        // 在类中添加清空按钮点击事件处理方法
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // 播放按钮点击动画
            var clickAnimation = (Storyboard)FindResource("CloseButtonClickAnimation");
            clickAnimation.Begin(clearButtonBorder); // 改为对Border应用动画

            // 确认对话框
            var result = MessageBox.Show("确定要清空所有历史记录吗？此操作不可撤销！",
                                        "确认清空",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // 调用SecondWindow的清空历史记录方法
                var mainApp = Application.Current as App;
                if (mainApp?.SecondWindowInstance != null)
                {
                    mainApp.SecondWindowInstance.ClearDrawHistory();

                    // 重新加载空的历史记录
                    LoadHistory(new List<int>(), new Dictionary<int, string>());

                    // 显示提示信息
                    MessageBox.Show("历史记录已清空", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // 在CreateCustomScrollBar方法后添加按钮动画资源
        private void InitializeButtonAnimations()
        {
            // 清空按钮点击动画
            var clearButtonClickAnimation = new Storyboard();
            var scaleXAnimation = new DoubleAnimationUsingKeyFrames();
            scaleXAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.1))));
            scaleXAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));

            var scaleYAnimation = new DoubleAnimationUsingKeyFrames();
            scaleYAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.1))));
            scaleYAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));

            clearButtonClickAnimation.Children.Add(scaleXAnimation);
            clearButtonClickAnimation.Children.Add(scaleYAnimation);

            this.Resources.Add("ClearButtonClickAnimation", clearButtonClickAnimation);
        }
    }
}