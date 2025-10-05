using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace testdemo
{
    public partial class AlarmNotificationWindow : Window
    {
        private DispatcherTimer _countdownTimer;
        private DateTime _alarmStartTime;
        private int _prepareTime;
        private string _alarmTitle;
        private bool _isInAlarmPeriod;
        private double _lastProgressHeight = 280; // 记录上一次的进度条高度

        public AlarmNotificationWindow(DateTime alarmStartTime, int prepareTime, string alarmTitle, bool isInAlarmPeriod)
        {
            InitializeComponent();
            _alarmStartTime = alarmStartTime;
            _prepareTime = prepareTime;
            _alarmTitle = alarmTitle;
            _isInAlarmPeriod = isInAlarmPeriod;

            Loaded += AlarmNotificationWindow_Loaded;
        }

        private void AlarmNotificationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置标题
            TitleText.Text = $"{_alarmTitle}闹钟即将开始";

            // 加载API名言
            LoadHitokotoQuote();

            // 设置窗口初始位置在屏幕中央上方（刚好在屏幕外）
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            Left = (screenWidth - Width) / 2;
            Top = -Height;

            // 开始下落动画
            StartDropAnimation();

            // 启动倒计时定时器
            StartCountdownTimer();
        }

        private async void LoadHitokotoQuote()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetStringAsync("https://v1.hitokoto.cn/?c=k&c=i");
                    var hitokotoData = JsonSerializer.Deserialize<HitokotoData>(response);
                    QuoteText.Text = $"{hitokotoData.hitokoto} —— {hitokotoData.from}";
                }
            }
            catch (Exception ex)
            {
                QuoteText.Text = "每一天都是新的开始，加油！ —— 励志名言";
            }
        }

        private void StartCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromMilliseconds(100); // 更频繁的更新，实现平滑动画
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();

            // 立即更新一次显示
            UpdateCountdownDisplay();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            UpdateCountdownDisplay();
        }

        private void UpdateCountdownDisplay()
        {
            DateTime now = DateTime.Now;
            TimeSpan timeLeft;

            if (_isInAlarmPeriod)
            {
                // 情况1：已经在闹钟时间段内 - 直接开始10秒倒计时
                timeLeft = TimeSpan.FromSeconds(10) - (now - _alarmStartTime);

                if (timeLeft.TotalSeconds <= 0)
                {
                    _countdownTimer.Stop();
                    CountdownText.Text = "闹钟开始！";
                    Console.WriteLine("闹钟开始！");

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var fullScreenWindow = new FullScreenAlarmWindow();
                        fullScreenWindow.Show();

                        // 关闭当前通知窗口
                        StartExitAnimation();
                    }));
                    // 平滑动画到完全消失
                    AnimateProgressBar(0);

                    // 3秒后自动关闭窗口
                    DispatcherTimer closeTimer = new DispatcherTimer();
                    closeTimer.Interval = TimeSpan.FromSeconds(1);
                    closeTimer.Tick += (s, e) =>
                    {
                        closeTimer.Stop();
                        StartExitAnimation();
                    };
                    closeTimer.Start();
                    return;
                }

                // 更新进度条：10秒内从满到空
                double progressRatio = timeLeft.TotalSeconds / 10;
                double targetHeight = 280 * progressRatio;
                AnimateProgressBar(targetHeight);

                CountdownText.Text = $"{timeLeft.TotalSeconds:F1}秒";
                Console.WriteLine($"闹钟区间内倒计时: {timeLeft.TotalSeconds:F1}秒");
            }
            else
            {
                // 情况2：在闹钟时间段外
                timeLeft = _alarmStartTime - now;

                if (timeLeft.TotalSeconds <= 0)
                {
                    // 如果时间已经过了开始时间但不在闹钟时间段内（重新检查状态）
                    _countdownTimer.Stop();
                    CountdownText.Text = "时间已过";
                    Console.WriteLine("闹钟时间已过");

                    // 平滑动画到完全消失
                    AnimateProgressBar(0);
                    return;
                }
                else if (timeLeft.TotalSeconds > _prepareTime)
                {
                    // 情况3：距离开始时间还超过预备时间，显示预备时间倒计时
                    int remainingPrepareTime = _prepareTime - (int)(_alarmStartTime - now - TimeSpan.FromSeconds(_prepareTime)).TotalSeconds;
                    timeLeft = TimeSpan.FromSeconds(Math.Max(0, remainingPrepareTime));

                    // 在预备时间之前，进度条保持满的
                    AnimateProgressBar(280);

                    CountdownText.Text = $"{timeLeft.TotalSeconds:F1}秒";
                    Console.WriteLine($"预备时间倒计时: {timeLeft.TotalSeconds:F1}秒");
                }
                else
                {
                    // 情况4：在预备时间内，正常显示剩余时间

                    // 更新进度条：预备时间内从满到空
                    double progressRatio = timeLeft.TotalSeconds / _prepareTime;
                    double targetHeight = 280 * progressRatio;
                    AnimateProgressBar(targetHeight);

                    CountdownText.Text = $"{timeLeft.TotalSeconds:F1}秒";
                    Console.WriteLine($"正常倒计时: {timeLeft.TotalSeconds:F1}秒");
                }
            }
        }

        private void AnimateProgressBar(double targetHeight)
        {
            // 如果高度变化很小，直接设置以避免不必要的动画
            if (Math.Abs(_lastProgressHeight - targetHeight) < 0.1)
            {
                ProgressClip.Rect = new Rect(0, 0, 350, targetHeight);
                return;
            }

            // 创建平滑动画
            DoubleAnimation animation = new DoubleAnimation();
            animation.From = _lastProgressHeight;
            animation.To = targetHeight;
            animation.Duration = TimeSpan.FromMilliseconds(500); // 500毫秒的平滑动画
            animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 应用动画
            ProgressClip.BeginAnimation(RectangleGeometry.RectProperty,
                new RectAnimation(
                    new Rect(0, 0, 350, _lastProgressHeight),
                    new Rect(0, 0, 350, targetHeight),
                    new Duration(TimeSpan.FromMilliseconds(500)),
                    FillBehavior.HoldEnd)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            _lastProgressHeight = targetHeight;
        }

        private void StartDropAnimation()
        {
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double targetTop = (screenHeight - Height) / 2;

            // 下落动画
            DoubleAnimation dropAnimation = new DoubleAnimation();
            dropAnimation.From = -Height;
            dropAnimation.To = targetTop;
            dropAnimation.Duration = TimeSpan.FromSeconds(0.8);
            dropAnimation.EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 2,
                Springiness = 2
            };

            // 淡入效果
            DoubleAnimation fadeInAnimation = new DoubleAnimation();
            fadeInAnimation.From = 0;
            fadeInAnimation.To = 1;
            fadeInAnimation.Duration = TimeSpan.FromSeconds(0.5);

            this.BeginAnimation(Window.TopProperty, dropAnimation);
            this.BeginAnimation(OpacityProperty, fadeInAnimation);
        }

        private void StartExitAnimation()
        {
            // 上升动画（与进入相反）
            DoubleAnimation riseAnimation = new DoubleAnimation();
            riseAnimation.From = Top;
            riseAnimation.To = -Height;
            riseAnimation.Duration = TimeSpan.FromSeconds(0.6);
            riseAnimation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            // 淡出效果
            DoubleAnimation fadeOutAnimation = new DoubleAnimation();
            fadeOutAnimation.From = 1;
            fadeOutAnimation.To = 0;
            fadeOutAnimation.Duration = TimeSpan.FromSeconds(0.4);

            riseAnimation.Completed += (s, e) => Close();

            this.BeginAnimation(Window.TopProperty, riseAnimation);
            this.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer?.Stop();
            StartExitAnimation();
        }

        private void DragWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // Hitokoto API 数据模型
        public class HitokotoData
        {
            public string hitokoto { get; set; }
            public string from { get; set; }
        }
    }
}