using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Linq;

namespace testdemo
{
    public partial class ClassManagerWindow : Window
    {
        private DispatcherTimer _positionTimer;
        // 删除重复的字段声明
        // private ClassManagerWindow _classManagerWindow; // 这行要删除

        // 添加位置变化事件
        public event EventHandler PositionChanged;
        // 添加刷新事件
        public event EventHandler RefreshRequested;

        public ClassManagerWindow()
        {
            InitializeComponent();

            // 设置初始状态
            this.Opacity = 0;
            this.Width = 820;
            this.Height = 150;

            // 设置窗口在屏幕中上位置
            SetWindowPosition();

            // 延迟1秒后启动动画
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1.0);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                PlayEntranceAnimation();

                // 启动位置跟踪
                StartPositionTracking();
            };
            timer.Start();
        }

        // 修改位置追踪方法 - 添加具体实现
        private void UpdatePositionTracking()
        {
            try
            {
                // 触发位置变化事件，通知其他窗口
                PositionChanged?.Invoke(this, EventArgs.Empty);

                // 强制更新所有子窗口位置
                UpdateChildWindowsPosition();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"位置追踪错误: {ex.Message}");
            }
        }

        // 更新所有子窗口位置
        private void UpdateChildWindowsPosition()
        {
            // 更新作业窗口位置
            var homeworkWindows = Application.Current.Windows.OfType<HomeworkWindow>();
            foreach (var homeworkWindow in homeworkWindows)
            {
                try
                {
                    // 调用作业窗口的同步位置方法
                    homeworkWindow.SyncWithClassManager(this);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"更新作业窗口位置错误: {ex.Message}");
                }
            }

            // 更新早晚读窗口位置
            var readingWindows = Application.Current.Windows.OfType<ReadingWindow>();
            foreach (var readingWindow in readingWindows)
            {
                try
                {
                    // 触发早晚读窗口的位置更新
                    readingWindow.UpdatePositionFromClassManager(this);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"更新早晚读窗口位置错误: {ex.Message}");
                }
            }
        }

        // 刷新按钮点击事件
        private void RefreshImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 先播放点击动画
            PlayRefreshClickAnimation();

            // 延迟触发刷新事件
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.2);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                // 触发刷新事件（会同时刷新HomeworkWindow和ReadingWindow）
                RefreshRequested?.Invoke(this, EventArgs.Empty);

                // 同时直接调用ReadingWindow的刷新方法
                RefreshAllReadingWindows();
            };
            timer.Start();
        }

        // 新增方法：刷新所有ReadingWindow
        private void RefreshAllReadingWindows()
        {
            var readingWindows = Application.Current.Windows.OfType<ReadingWindow>();
            foreach (var readingWindow in readingWindows)
            {
                try
                {
                    readingWindow.RefreshReadingData();
                    Console.WriteLine("已触发早晚读窗口刷新");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"触发早晚读刷新失败: {ex.Message}");
                }
            }
        }

        // 播放刷新按钮点击动画
        private void PlayRefreshClickAnimation()
        {
            Storyboard refreshAnimation = (Storyboard)this.FindResource("RefreshClickAnimation");
            if (refreshAnimation != null)
            {
                refreshAnimation.Begin(this);
            }
        }

        // 修改现有的位置追踪方法
        private void StartPositionTracking()
        {
            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50); // 20fps 实时刷新
            _positionTimer.Tick += (s, e) => UpdatePositionTracking();
            _positionTimer.Start();
        }

        private void SetWindowPosition()
        {
            // 获取屏幕宽度
            double screenWidth = SystemParameters.PrimaryScreenWidth;

            // 计算窗口左侧位置使其居中
            double left = (screenWidth - this.Width) / 2;
            this.Left = left;
            this.Top = -100; // 初始位置在屏幕上方外部
        }

        private void PlayEntranceAnimation()
        {
            Storyboard entranceAnimation = (Storyboard)this.FindResource("WindowEntranceAnimation");
            if (entranceAnimation != null)
            {
                entranceAnimation.Begin(this);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _positionTimer?.Stop();
            base.OnClosed(e);
        }

        private void SetImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PlaySetClickAnimation();
        }

        private void PlaySetClickAnimation()
        {
            Storyboard setAnimation = (Storyboard)this.FindResource("SetClickAnimation");
            if (setAnimation != null)
            {
                setAnimation.Begin(this);
            }
        }

        private bool isWindowMinimized = false;

        private void MinimizeImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 先播放点击动画
            PlayMinimizeClickAnimation();

            // 延迟播放收缩/展开动画
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.2);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                if (isWindowMinimized)
                {
                    // 展开窗口
                    PlayWindowExpandAnimation();
                    isWindowMinimized = false;
                }
                else
                {
                    // 收缩窗口
                    PlayWindowShrinkAnimation();
                    isWindowMinimized = true;
                }
            };
            timer.Start();
        }

        private void PlayMinimizeClickAnimation()
        {
            Storyboard minimizeAnimation = (Storyboard)this.FindResource("MinimizeClickAnimation");
            if (minimizeAnimation != null)
            {
                minimizeAnimation.Begin(this);
            }
        }

        private void MinimizeImage1_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PlayMinimizeClickAnimation1();
        }

        private void MinimizeImage2_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PlayMinimizeClickAnimation2();
        }

        private void PlayMinimizeClickAnimation1()
        {
            Storyboard minimizeAnimation = (Storyboard)this.FindResource("MinimizeClickAnimation1");
            if (minimizeAnimation != null)
            {
                minimizeAnimation.Begin(this);
            }
        }

        private void PlayMinimizeClickAnimation2()
        {
            Storyboard minimizeAnimation = (Storyboard)this.FindResource("MinimizeClickAnimation2");
            if (minimizeAnimation != null)
            {
                minimizeAnimation.Begin(this);
            }
        }

        private void CloseOpenImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 先播放点击动画
            PlayCloseOpenClickAnimation();

            // 延迟播放收缩/展开动画
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.2);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                if (isWindowMinimized)
                {
                    // 展开窗口，切换为关闭图标
                    PlayWindowExpandAnimation();
                    closeOpenImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Clziyuan/guanbi.png"));
                    isWindowMinimized = false;
                }
                else
                {
                    // 收缩窗口，切换为打开图标
                    PlayWindowShrinkAnimation();
                    closeOpenImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Clziyuan/dakai.png"));
                    isWindowMinimized = true;
                }
            };
            timer.Start();
        }

        private void PlayCloseOpenClickAnimation()
        {
            Storyboard closeOpenAnimation = (Storyboard)this.FindResource("CloseOpenClickAnimation");
            if (closeOpenAnimation != null)
            {
                closeOpenAnimation.Begin(this);
            }
        }

        private void PlayWindowShrinkAnimation()
        {
            Storyboard shrinkAnimation = (Storyboard)this.FindResource("WindowShrinkAnimation");
            if (shrinkAnimation != null)
            {
                shrinkAnimation.Begin(this);
            }
        }

        private void PlayWindowExpandAnimation()
        {
            Storyboard expandAnimation = (Storyboard)this.FindResource("WindowExpandAnimation");
            if (expandAnimation != null)
            {
                expandAnimation.Begin(this);
            }
        }

        // 实现窗口拖动功能
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();

            // 拖动时立即触发位置变化事件
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        // 添加窗口位置变化的事件处理 - 修复名称冲突
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            // 使用自定义的事件名称，避免与基类的LocationChanged冲突
            PositionChanged?.Invoke(this, e);
        }

        // 重写 OnRenderSizeChanged 方法
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        // 公开方法：获取窗口位置信息
        public (double Left, double Top, double Width, double Height) GetWindowInfo()
        {
            return (this.Left, this.Top, this.ActualWidth, this.ActualHeight);
        }
    }
}