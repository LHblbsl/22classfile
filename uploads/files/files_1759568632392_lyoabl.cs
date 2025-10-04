using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Globalization; // 添加这个命名空间
using System.Windows.Data; // 添加这个命名空间
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;

// 为 System.IO.Path 添加别名以避免冲突
using IOPath = System.IO.Path;
namespace testdemo
{
    public partial class SecondWindow : Window
    {
        private GoldAnimationWindow _goldAnimationWindow;
        private const int GUARANTEED_UP_DRAW = 9; // 第9抽必出UP
        private const int INCREASED_RATE_DRAW = 5; // 第5抽开始增
        private Dictionary<int, string> _studentData = new Dictionary<int, string>();
        private Random _random = new Random();
        private int _currentSelectedStudentId = -1;
        private DrawHistoryManager _drawHistoryManager = new DrawHistoryManager(20);
        // 头像缓存：学号 -> 头像ImageSource
        private static Dictionary<int, ImageSource> _avatarCache = new Dictionary<int, ImageSource>();
        // 在 SecondWindow 中添加静态属性以便访问
        private static SecondWindow _instance;
        public static SecondWindow Instance => _instance;
        public enum WindowState
        {
            Start,
            Middle,
            Hidden,
            End
        }
        private double _currentScale = 1.0;
        // 添加字段来跟踪动画状态
        private CancellationTokenSource _animationCancellationTokenSource;
        private bool _isAnimationInProgress = false;
        private MainWindow _mainWindow;
        private WindowState _currentState = WindowState.End;
        private DispatcherTimer _colorSyncTimer;
        private bool _isAnimating = false;
        private double _originalHeight = 150;
        private int _animationRound = 0;
        private const int TOTAL_ANIMATION_ROUNDS = 5;
        private DispatcherTimer _animationTimer;
        private bool _isDraggable = true; // 添加这行
        // 在 SecondWindow 类中添加字段
        private NumberSettingsWindow? _numberSettingsWindow;
        private bool _isNumberSettingsWindowOpen = false;
        private List<int> _currentUpPool = new List<int>(); // 当前UP池（4人）
        private List<int> _historyUpPool = new List<int>(); // 历史UP池
        private int _drawCountSinceLastUp = 0; // 距离上次出UP的抽数
        public SecondWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _instance = this;
            this.Loaded += SecondWindow_Loaded;
            _originalHeight = secondCapsuleBorder.Height;
            secondCapsuleBorder.Background = new SolidColorBrush(Settings.ThemeColor);
            _mainWindow = mainWindow;
            secondCapsuleBorder.Visibility = Visibility.Collapsed;
            this.Hide();

            SyncInitialColor();

            _colorSyncTimer = new DispatcherTimer();
            _colorSyncTimer.Interval = TimeSpan.FromSeconds(30);
            _colorSyncTimer.Tick += SyncColorWithMainWindow;
            _colorSyncTimer.Start();

            var settings = DataService.LoadSettings();
            LoadStudentData(settings.StudentList);
            InitializeUpPool();

            // 应用保存的缩放设置
            ApplyScale(settings.SecondWindowScale);

            // 应用保存的拖动设置
            SetDraggable(settings.SecondWindowDraggable);

            // 初始化信息区
            UpdateInfoArea();
            InitializeDragSystem();

            RestoreWindowPosition();
            EnsureWindowInScreen();

            Console.WriteLine($"二号窗口初始化完成 - 缩放: {settings.SecondWindowScale}, 拖动: {settings.SecondWindowDraggable}");
        }
        private void EnsureWindowInScreen()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // 考虑缩放后的实际宽度和高度
            double scaledWidth = this.ActualWidth * _currentScale;
            double scaledHeight = this.ActualHeight * _currentScale;

            if (this.Left + scaledWidth > screenWidth)
                this.Left = screenWidth - scaledWidth - 10;

            if (this.Top + scaledHeight > screenHeight)
                this.Top = screenHeight - scaledHeight - 10;

            if (this.Left < 0) this.Left = 10;
            if (this.Top < 0) this.Top = 10;

            Console.WriteLine($"确保窗口在屏幕内 - 缩放后尺寸: {scaledWidth}x{scaledHeight}, 位置: {Left},{Top}");
        }
        // 应用缩放
        // 应用缩放
        public void ApplyScale(double scale)
        {
            try
            {
                _currentScale = scale;
                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;

                // 确保窗口在屏幕范围内
                EnsureWindowInScreen();

                Console.WriteLine($"缩放已应用: {scale}, 窗口位置: Left={Left}, Top={Top}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"应用缩放失败: {ex.Message}");
            }
        }
        private bool _isCurrentStudentUp = false;
        private List<int> _actualUpStudents = new List<int>();
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private DispatcherTimer _dragTimer;
        private const double DRAG_THRESHOLD = 5.0; // 拖动阈值，避免误触

        // 修改 ShouldDrawUpFromCurrentPool 方法，确保正确的UP判断逻辑
        private bool ShouldDrawUpFromCurrentPool()
        {
            // 第9抽必出UP
            if (_drawCountSinceLastUp >= GUARANTEED_UP_DRAW - 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"保底机制触发！第{_drawCountSinceLastUp + 1}抽必出UP");
                Console.ResetColor();
                return true;
            }

            // 第5抽开始增加概率
            if (_drawCountSinceLastUp >= INCREASED_RATE_DRAW - 1)
            {
                double currentProbability = CalculateCurrentProbability();
                double randomValue = _random.NextDouble();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"第{_drawCountSinceLastUp + 1}抽，UP概率: {currentProbability:P0}");
                Console.ResetColor();

                if (randomValue < currentProbability)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("🎉 触发增加概率的UP！");
                    Console.ResetColor();
                    return true;
                }
            }

            return false;
        }

        // 修改 RandomSelectStudent 方法中的UP处理逻辑
        public async void RandomSelectStudent()
        {
            if (_isAnimationInProgress)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("动画正在进行中，跳过本次抽选");
                Console.ResetColor();
                return;
            }
            if (_studentData.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("RandomSelectStudent: 暂无学生数据");
                UpdateStudentDisplay("暂无学生数据", null);
                if (_currentState == WindowState.Start)
                {
                    _currentState = WindowState.Middle;
                    UpdateDebugStateInfo();
                }
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 开始第{_drawCountSinceLastUp + 1}抽 ===");
            Console.ResetColor();

            // 重置显示状态为新抽选做准备
            ResetDisplayForNewSelection();

            // 取消之前的动画
            if (_isAnimationInProgress)
            {
                _animationCancellationTokenSource?.Cancel();
                await Task.Delay(100); // 给一点时间让之前的动画停止
            }

            bool shouldDrawUp = ShouldDrawUpFromCurrentPool();

            if (shouldDrawUp)
            {
                _isCurrentStudentUp = true;
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"触发UP机制！从当前UP池中选择学生");
                Console.ResetColor();

                // 从当前UP池中随机选择一个
                int randomIndex = _random.Next(_currentUpPool.Count);
                _currentSelectedStudentId = _currentUpPool[randomIndex];
                // 记录这个真正触发UP的学生
                _actualUpStudents.Add(_currentSelectedStudentId);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🎉 UP学生: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})");
                Console.ResetColor();

                // 将当前UP池加入历史UP池（在重置前保存）
                AddCurrentUpPoolToHistory();

                // 重置当前UP池（避免选择历史UP池中的学生）
                InitializeNewCurrentUpPool();

                // 重置抽数计数
                _drawCountSinceLastUp = 0;
            }
            else
            {
                _isCurrentStudentUp = false;
                // 普通抽选逻辑 - 排除当前UP池中的学生
                var availableStudents = _studentData.Keys
                    .Where(id => !_drawHistoryManager.IsInHistory(id) && !_currentUpPool.Contains(id))
                    .ToList();

                if (availableStudents.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("可用学生不足，重置筛选条件（排除当前UP池）");
                    Console.ResetColor();

                    availableStudents = _studentData.Keys
                        .Where(id => !_currentUpPool.Contains(id))
                        .ToList();
                }

                if (availableStudents.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("所有学生都在当前UP池中，重置抽奖历史");
                    Console.ResetColor();

                    _drawHistoryManager.ClearHistory();
                    availableStudents = _studentData.Keys
                        .Where(id => !_currentUpPool.Contains(id))
                        .ToList();
                }

                if (availableStudents.Count == 0)
                {
                    // 如果仍然没有可用学生，只能从当前UP池中选择（这种情况应该很少见）
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("警告：所有学生都在当前UP池中，被迫从UP池选择");
                    Console.ResetColor();

                    int randomIndex = _random.Next(_currentUpPool.Count);
                    _currentSelectedStudentId = _currentUpPool[randomIndex];
                }
                else
                {
                    int randomIndex = _random.Next(availableStudents.Count);
                    _currentSelectedStudentId = availableStudents[randomIndex];
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"普通抽选: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})");
                Console.ResetColor();

                // 增加抽数计数
                _drawCountSinceLastUp++;
            }

            // 界面更新逻辑 - 确保无论是否UP都执行
            studentInfoPanel.Visibility = Visibility.Collapsed;
            randomStudentsGrid.Visibility = Visibility.Visible;

            GenerateRandomStudents(4);
            UpdateRandomStudentsDisplay();
            _drawHistoryManager.AddToHistory(_currentSelectedStudentId);

            UpdateDebugStateInfo();

            // 开始动画 - 确保无论是否UP都执行动画
            await StartStudentAnimationAsync();

            // 保存到历史记录（UP池逻辑已在上面的UP机制中处理）
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("正在将选中学生保存到历史记录...");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 随机选择完成 ===");
            Console.ResetColor();
        }

        // 修改 InitializeNewCurrentUpPool 方法，避免选择历史UP池中的学生
        // 修改 InitializeNewCurrentUpPool 方法
        private void InitializeNewCurrentUpPool()
        {
            var settings = DataService.LoadSettings();
            var excludedStudents = settings.ExcludedFromUpPool ?? new List<int>();

            var allStudents = GetAllStudents().Keys.ToList();

            // 排除历史UP池中的学生和明确排除的学生
            var availableStudents = allStudents
                .Except(_historyUpPool)
                .Except(excludedStudents)
                .ToList();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"初始化新UP池：排除{_historyUpPool.Count}名历史UP学生 + {excludedStudents.Count}名手动排除学生");
            Console.WriteLine($"可用学生数量: {availableStudents.Count}");
            Console.ResetColor();

            if (availableStudents.Count < 4)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"可用学生不足4人，清空历史UP池重新选择");
                Console.ResetColor();

                _historyUpPool.Clear();
                availableStudents = allStudents.Except(excludedStudents).ToList();
            }

            _currentUpPool = SelectRandomStudents(4, availableStudents);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"新的当前UP池: {string.Join(", ", _currentUpPool.Select(id => $"{_studentData[id]}({id})"))}");
            Console.ResetColor();

            SaveCurrentUpPoolToFile();
        }

        // 添加辅助方法：从指定列表中随机选择学生
        private List<int> SelectRandomStudents(int count, List<int> availableStudents)
        {
            var selected = new List<int>();
            var random = new Random();

            // 随机打乱并选择
            var shuffled = availableStudents.OrderBy(x => random.Next()).ToList();

            for (int i = 0; i < Math.Min(count, shuffled.Count); i++)
            {
                int id = shuffled[i];
                selected.Add(id);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"选中UP学生[{selected.Count}/{count}]: {_studentData[id]}({id})");
                Console.ResetColor();
            }

            return selected;
        }

        // 修改 DrawMultipleStudentsWithUpAsync 方法，确保UP机制正确工作
        public async Task<List<int>> DrawMultipleStudentsWithUpAsync(int count)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 开始带UP机制的多人抽选 ===");
            Console.WriteLine($"目标人数: {count}, 当前抽数计数: {_drawCountSinceLastUp}");
            Console.ResetColor();

            var results = new List<int>();
            var upResults = new List<int>();

            if (count <= 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("count <= 0，返回空列表");
                Console.ResetColor();
                return results;
            }

            for (int i = 0; i < count; i++)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"--- 第{i + 1}/{count}次抽选 ---");
                Console.ResetColor();

                // 判断是否应该出UP
                bool shouldDrawUp = ShouldDrawUpFromCurrentPool();
                int selectedStudentId = -1;

                if (shouldDrawUp)
                {
                    // 从当前UP池中随机选择
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"触发UP机制！从当前UP池中选择学生");
                    Console.ResetColor();

                    int randomIndex = _random.Next(_currentUpPool.Count);
                    selectedStudentId = _currentUpPool[randomIndex];

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"🎉 UP学生: {_studentData[selectedStudentId]}({selectedStudentId})");
                    Console.ResetColor();

                    // 将当前UP池加入历史UP池
                    AddCurrentUpPoolToHistory();

                    // 重置当前UP池（避免选择历史UP池中的学生）
                    InitializeNewCurrentUpPool();

                    // 重置抽数计数
                    _drawCountSinceLastUp = 0;

                    upResults.Add(selectedStudentId);
                }
                else
                {
                    // 普通抽选逻辑 - 排除当前UP池中的学生和历史记录中的学生
                    var availableStudents = _studentData.Keys
                        .Where(id => !_drawHistoryManager.IsInHistory(id) &&
                                    !results.Contains(id) &&
                                    !_currentUpPool.Contains(id))
                        .ToList();

                    if (availableStudents.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("可用学生不足，重置筛选条件（排除当前UP池）");
                        Console.ResetColor();

                        availableStudents = _studentData.Keys
                            .Where(id => !results.Contains(id) && !_currentUpPool.Contains(id))
                            .ToList();
                    }

                    if (availableStudents.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("所有学生都在当前UP池或已选中，重置抽奖历史");
                        Console.ResetColor();

                        _drawHistoryManager.ClearHistory();
                        availableStudents = _studentData.Keys
                            .Where(id => !results.Contains(id) && !_currentUpPool.Contains(id))
                            .ToList();
                    }

                    if (availableStudents.Count == 0)
                    {
                        // 如果仍然没有可用学生，只能从当前UP池中选择
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("警告：所有学生都在当前UP池或已选中，被迫从UP池选择");
                        Console.ResetColor();

                        int randomIndex = _random.Next(_currentUpPool.Count);
                        selectedStudentId = _currentUpPool[randomIndex];
                    }
                    else
                    {
                        int randomIndex = _random.Next(availableStudents.Count);
                        selectedStudentId = availableStudents[randomIndex];
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"普通学生: {_studentData[selectedStudentId]}({selectedStudentId})");
                    Console.ResetColor();

                    // 增加抽数计数
                    _drawCountSinceLastUp++;
                }

                if (selectedStudentId != -1)
                {
                    results.Add(selectedStudentId);
                    _drawHistoryManager.AddToHistory(selectedStudentId);
                }

                // 添加短暂延迟，让控制台输出更清晰
                await Task.Delay(100);
            }

            // 输出UP统计
            if (upResults.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("=== 本次批量抽选UP统计 ===");
                Console.WriteLine($"共获得 {upResults.Count} 名UP学生:");
                foreach (var upId in upResults)
                {
                    Console.WriteLine($"🎉 {_studentData[upId]}({upId})");
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("本次批量抽选未获得UP学生");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 带UP机制的多人抽选完成 ===");
            Console.WriteLine($"最终选中 {results.Count} 人");
            Console.ResetColor();

            return results;
        }

        // 修改 DrawSingleStudentWithAnimationAsync 方法，确保UP机制正确工作
        public async Task DrawSingleStudentWithAnimationAsync()
        {
            if (_isAnimationInProgress)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("动画正在进行中，跳过本次抽选");
                Console.ResetColor();
                return;
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 开始单次抽选 ===");
            Console.WriteLine($"DrawSingleStudentWithAnimationAsync 被调用，当前时间: {DateTime.Now:HH:mm:ss.fff}");
            Console.ResetColor();

            if (_studentData.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("暂无学生数据");
                UpdateStudentDisplay("暂无学生数据", null);
                Console.ResetColor();
                return;
            }

            // 重置显示状态为新抽选做准备
            ResetDisplayForNewSelection();

            // 取消之前的动画
            if (_isAnimationInProgress)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("检测到动画正在进行，取消之前动画");
                Console.ResetColor();
                _animationCancellationTokenSource?.Cancel();
                await Task.Delay(100);
            }

            // 判断是否应该出UP
            bool shouldDrawUp = ShouldDrawUpFromCurrentPool();

            if (shouldDrawUp)
            {
                // 从当前UP池中随机选择
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"触发UP机制！从当前UP池中选择学生");
                Console.ResetColor();

                int randomIndex = _random.Next(_currentUpPool.Count);
                _currentSelectedStudentId = _currentUpPool[randomIndex];

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🎉 UP学生: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})");
                Console.ResetColor();

                // 将当前UP池加入历史UP池
                AddCurrentUpPoolToHistory();

                // 重置当前UP池（避免选择历史UP池中的学生）
                InitializeNewCurrentUpPool();

                // 重置抽数计数
                _drawCountSinceLastUp = 0;
            }
            else
            {
                // 普通抽选逻辑 - 排除当前UP池中的学生
                var availableStudents = _studentData.Keys
                    .Where(id => !_drawHistoryManager.IsInHistory(id) && !_currentUpPool.Contains(id))
                    .ToList();

                if (availableStudents.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("可用学生不足，重置筛选条件（排除当前UP池）");
                    Console.ResetColor();

                    availableStudents = _studentData.Keys
                        .Where(id => !_currentUpPool.Contains(id))
                        .ToList();
                }

                if (availableStudents.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("所有学生都在当前UP池中，重置抽奖历史");
                    Console.ResetColor();

                    _drawHistoryManager.ClearHistory();
                    availableStudents = _studentData.Keys
                        .Where(id => !_currentUpPool.Contains(id))
                        .ToList();
                }

                if (availableStudents.Count == 0)
                {
                    // 如果仍然没有可用学生，只能从当前UP池中选择
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("警告：所有学生都在当前UP池中，被迫从UP池选择");
                    Console.ResetColor();

                    int randomIndex = _random.Next(_currentUpPool.Count);
                    _currentSelectedStudentId = _currentUpPool[randomIndex];
                }
                else
                {
                    int randomIndex = _random.Next(availableStudents.Count);
                    _currentSelectedStudentId = availableStudents[randomIndex];
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"普通学生: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})");
                Console.ResetColor();

                // 增加抽数计数
                _drawCountSinceLastUp++;
            }

            // 界面更新
            studentInfoPanel.Visibility = Visibility.Collapsed;
            randomStudentsGrid.Visibility = Visibility.Visible;

            GenerateRandomStudents(4);
            UpdateRandomStudentsDisplay();

            // 开始动画
            await StartStudentAnimationAsync();

            // 添加到历史记录
            _drawHistoryManager.AddToHistory(_currentSelectedStudentId);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 单次抽选完成 ===");
            Console.ResetColor();
        }


        private void PreloadAvatars(Dictionary<int, StudentInfo> studentData)
        {
            _avatarCache.Clear();
            foreach (var kvp in studentData)
            {
                int studentId = kvp.Key;
                StudentInfo info = kvp.Value;

                // 如果已经有本地头像，直接使用
                if (info.Avatar != null)
                {
                    _avatarCache[studentId] = info.Avatar;
                }
                else
                {
                    // 否则尝试从QQ头像API下载（这里需要根据实际情况实现下载逻辑）
                    ImageSource avatar = DownloadAvatar(studentId);
                    if (avatar != null)
                    {
                        _avatarCache[studentId] = avatar;
                    }
                }
            }
        }

        private ImageSource DownloadAvatar(int studentId)
        {
            // 假设QQ头像API的URL格式为：https://q1.qlogo.cn/g?b=qq&nk={qqNumber}&s=100
            // 这里需要根据实际情况获取学生的QQ号，这里假设studentId就是QQ号（或者通过其他方式映射）
            string qqNumber = studentId.ToString();
            string url = $"https://q1.qlogo.cn/g?b=qq&nk={qqNumber}&s=100";

            try
            {
                WebRequest request = WebRequest.Create(url);
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze(); // 跨线程使用需要Freeze
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
        private ImageSource GetAvatarFromCache(int studentId)
        {
            if (_avatarCache.TryGetValue(studentId, out ImageSource avatar))
            {
                return avatar;
            }
            return null;
        }
        // 添加字段

        private List<int> _randomStudentIds = new List<int>();

        public void UpdateThemeColor(Color color)
        {
            color.A = ((SolidColorBrush)secondCapsuleBorder.Background).Color.A;
            secondCapsuleBorder.Background = new SolidColorBrush(color);

            // 同步更新历史窗口颜色
            if (_isHistoryWindowOpen && _historyWindow != null)
            {
                _historyWindow.UpdateThemeColor(color);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CloseHistoryWindow();
            SaveWindowPosition();
            base.OnClosed(e);
        }
        // 修改 RandomSelectStudent 方法
       

        private void StartStudentAnimation()
        {
            _animationRound = 0;
            // 确保学生框可见
            randomStudentsGrid.Visibility = Visibility.Visible;
            ResetStudentPositions();
            PlayFastDownAnimation();
        }

        private void ResetStudentPositions()
        {
            // 重置学生框位置到初始状态
            randomStudent1.RenderTransform = new TransformGroup()
            {
                Children = new TransformCollection()
        {
            new ScaleTransform(),
            new SkewTransform(),
            new RotateTransform(),
            new TranslateTransform()
        }
            };

            randomStudent2.RenderTransform = new TransformGroup()
            {
                Children = new TransformCollection()
        {
            new ScaleTransform(),
            new SkewTransform(),
            new RotateTransform(),
            new TranslateTransform()
        }
            };

            randomStudent3.RenderTransform = new TransformGroup()
            {
                Children = new TransformCollection()
        {
            new ScaleTransform(),
            new SkewTransform(),
            new RotateTransform(),
            new TranslateTransform()
        }
            };

            randomStudent4.RenderTransform = new TransformGroup()
            {
                Children = new TransformCollection()
        {
            new ScaleTransform(),
            new SkewTransform(),
            new RotateTransform(),
            new TranslateTransform()
        }
            };
        }
        private HistoryWindow _historyWindow;
        private bool _isHistoryWindowOpen = false;


        private void FastDownAnimation_Completed(object sender, EventArgs e)
        {
            // 移除storyboard的事件处理器，避免重复
            var storyboard = sender as Storyboard;
            if (storyboard != null)
            {
                storyboard.Completed -= FastDownAnimation_Completed;
            }

            _animationRound++;
            debugStateTextBlock.Text = $"[动画轮次: {_animationRound}/{TOTAL_ANIMATION_ROUNDS}]";

            if (_animationRound < TOTAL_ANIMATION_ROUNDS)
            {
                // 继续下一轮动画
                GenerateRandomStudents(4);
                UpdateRandomStudentsDisplay();
                ResetStudentPositionsToTop();

                // 根据轮次选择不同的下落速度
                Storyboard nextStoryboard = null;

                switch (_animationRound)
                {
                    case 1:
                        nextStoryboard = (Storyboard)FindResource("StudentsFastDownAnimation");
                        break;
                    case 2:
                        nextStoryboard = (Storyboard)FindResource("StudentsMediumDownAnimation");
                        break;
                    case 3:
                        nextStoryboard = (Storyboard)FindResource("StudentsSlowDownAnimation");
                        break;
                    case 4:
                        nextStoryboard = (Storyboard)FindResource("StudentsVerySlowDownAnimation");
                        break;
                }

                if (nextStoryboard != null)
                {
                    nextStoryboard.Completed += FastDownAnimation_Completed;
                    nextStoryboard.Begin();
                }
            }
            else
            {
                // 最后一轮动画
                PrepareFinalAnimation();
                PlayFinalAnimation();
            }
        }

        private async Task StartStudentAnimationAsync()
        {
            _isAnimationInProgress = true;
            _animationCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _animationCancellationTokenSource.Token;

            // 动画开始时禁用拖动
            bool wasDraggable = _isDraggable;
            SetDraggable(false);

            try
            {
                // 通知主窗口开始动画，禁用开始按钮
                if (_mainWindow != null)
                {
                    _mainWindow.SetStartButtonEnabled(false);
                    _mainWindow.IsSecondWindowAnimating = true;
                }

                // 如果是UP学生，播放窗口变金动画
                if (_isCurrentStudentUp)
                {
                    await PlayWindowGoldAnimationAsync(cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;

                    // 修复：UP学生也需要继续执行后续动画
                    // 重置状态，确保继续执行普通动画流程
                    _animationRound = 0;
                    randomStudentsGrid.Visibility = Visibility.Visible;
                    ResetStudentPositions();
                }
                else
                {
                    _animationRound = 0;
                    randomStudentsGrid.Visibility = Visibility.Visible;
                    ResetStudentPositions();
                }

                // 第1轮：快速下落 (0.4秒)
                await PlayAnimationAsync("StudentsFastDownAnimation", 400, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _animationRound++;

                // 第2轮：中速下落 (0.6秒)
                await PlayAnimationAsync("StudentsMediumDownAnimation", 600, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _animationRound++;

                // 第3轮：慢速下落 (0.8秒)
                await PlayAnimationAsync("StudentsSlowDownAnimation", 800, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _animationRound++;

                // 第4轮：非常慢下落 (1.0秒)
                await PlayAnimationAsync("StudentsVerySlowDownAnimation", 1000, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _animationRound++;

                // 最终轮：停留动画 (1.2秒)
                PrepareFinalAnimation();
                await PlayAnimationAsync("StudentsFinalAnimation", 1200, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                // 添加1秒钟的高亮动画
                await PlayHighlightAnimationAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                // 动画完成后显示最终结果 - 确保UP学生也能正确显示
                ShowFinalResult();

                debugStateTextBlock.Text = $"[选中: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})]";
            }
            catch (OperationCanceledException)
            {
                // 动画被取消是正常的
            }
            finally
            {
                _isAnimationInProgress = false;
                // 动画结束后恢复原来的拖动状态
                SetDraggable(wasDraggable);
                if (_mainWindow != null)
                {
                    _mainWindow.SetStartButtonEnabled(true);
                    _mainWindow.IsSecondWindowAnimating = false;
                }
            }
        }
        // 添加动画状态检查属性
        public bool IsAnimating => _isAnimationInProgress;
        private async Task PlayHighlightAnimationAsync(CancellationToken cancellationToken)
        {
            // 找到包含选中学生的框并高亮显示
            Border selectedStudentBorder = FindSelectedStudentBorder();

            if (selectedStudentBorder != null)
            {
                // 创建高亮动画任务
                var tcs = new TaskCompletionSource<bool>();

                // 创建高亮动画
                var storyboard = new Storyboard();
                storyboard.Duration = TimeSpan.FromSeconds(1);

                // 边框颜色闪烁动画
                var colorAnimation = new ColorAnimation
                {
                    From = Colors.White,
                    To = Colors.Yellow,
                    Duration = TimeSpan.FromSeconds(0.5),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2) // 闪烁2次，总共1秒
                };

                Storyboard.SetTarget(colorAnimation, selectedStudentBorder);
                Storyboard.SetTargetProperty(colorAnimation, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));

                // 缩放动画
                var scaleAnimation = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.1,
                    Duration = TimeSpan.FromSeconds(0.3),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };

                var scaleTransform = new ScaleTransform();
                selectedStudentBorder.RenderTransform = scaleTransform;
                selectedStudentBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                Storyboard.SetTarget(scaleAnimation, scaleTransform);
                Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("ScaleX"));

                var scaleAnimationY = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.1,
                    Duration = TimeSpan.FromSeconds(0.3),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };

                Storyboard.SetTarget(scaleAnimationY, scaleTransform);
                Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("ScaleY"));

                storyboard.Children.Add(colorAnimation);
                storyboard.Children.Add(scaleAnimation);
                storyboard.Children.Add(scaleAnimationY);

                // 动画完成事件
                void OnCompleted(object sender, EventArgs e)
                {
                    storyboard.Completed -= OnCompleted;
                    tcs.TrySetResult(true);
                }

                storyboard.Completed += OnCompleted;
                storyboard.Begin();

                // 等待动画完成或取消
                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(1100, cancellationToken) // 1.1秒超时
                );

                if (completedTask != tcs.Task)
                {
                    storyboard.Stop();
                    storyboard.Completed -= OnCompleted;
                    throw new OperationCanceledException();
                }

                await tcs.Task;
            }
            else
            {
                // 如果没有找到选中的学生框，等待1秒
                await Task.Delay(1000, cancellationToken);
            }
        }

        private Border FindSelectedStudentBorder()
        {
            // 检查每个学生框，找到包含选中学生的那个
            if (randomStudent1Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                return randomStudent1;
            }
            else if (randomStudent2Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                return randomStudent2;
            }
            else if (randomStudent3Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                return randomStudent3;
            }
            else if (randomStudent4Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                return randomStudent4;
            }

            return null;
        }

        private void PlayFastDownAnimation()
        {
            var storyboard = (Storyboard)FindResource("StudentsFastDownAnimation");
            storyboard.Completed += FastDownAnimation_Completed;
            storyboard.Begin();
        }

        private void PlayMediumDownAnimation()
        {
            var storyboard = (Storyboard)FindResource("StudentsMediumDownAnimation");
            storyboard.Completed += FastDownAnimation_Completed;
            storyboard.Begin();
        }

        private void PlaySlowDownAnimation()
        {
            var storyboard = (Storyboard)FindResource("StudentsSlowDownAnimation");
            storyboard.Completed += FastDownAnimation_Completed;
            storyboard.Begin();
        }

        private void ResetStudentPositionsToTop()
        {
            // 确保学生框完全移到屏幕上方
            var transform1 = randomStudent1.RenderTransform as TransformGroup;
            if (transform1 != null && transform1.Children.Count > 3)
            {
                (transform1.Children[3] as TranslateTransform).Y = -150;
            }

            var transform2 = randomStudent2.RenderTransform as TransformGroup;
            if (transform2 != null && transform2.Children.Count > 3)
            {
                (transform2.Children[3] as TranslateTransform).Y = -150;
            }

            var transform3 = randomStudent3.RenderTransform as TransformGroup;
            if (transform3 != null && transform3.Children.Count > 3)
            {
                (transform3.Children[3] as TranslateTransform).Y = -150;
            }

            var transform4 = randomStudent4.RenderTransform as TransformGroup;
            if (transform4 != null && transform4.Children.Count > 3)
            {
                (transform4.Children[3] as TranslateTransform).Y = -150;
            }
        }

        private void PlayVerySlowDownAnimation()
        {
            // 第四轮使用非常慢的速度
            var storyboard = (Storyboard)FindResource("StudentsVerySlowDownAnimation");
            storyboard.Completed += FastDownAnimation_Completed;
            storyboard.Begin();
        }

        private void SlowDownAnimation_Completed(object sender, EventArgs e)
        {
            // 短暂停顿后继续快速下落
            _animationTimer = new DispatcherTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(300);
            _animationTimer.Tick += (s, args) =>
            {
                _animationTimer.Stop();
                PlayFastDownAnimation();
            };
            _animationTimer.Start();
        }



        private void PrepareFinalAnimation()
        {
            // 最后一轮：选择3名新学生，加上之前预留的1名
            var newStudents = GenerateRandomStudentsForFinalRound();
            UpdateRandomStudentsForFinalRound(newStudents);
        }


        private List<int> GenerateRandomStudentsForFinalRound()
        {
            // 选择3名新学生（排除当前选中的学生和已在历史中的学生）
            var availableIds = _studentData.Keys
                .Where(id => id != _currentSelectedStudentId && !_drawHistoryManager.IsInHistory(id))
                .ToList();

            var selectedStudents = new List<int>();
            var random = new Random();

            while (selectedStudents.Count < 3 && availableIds.Count > 0)
            {
                int randomIndex = random.Next(availableIds.Count);
                selectedStudents.Add(availableIds[randomIndex]);
                availableIds.RemoveAt(randomIndex);
            }

            return selectedStudents;
        }
        private void UpdateRandomStudentsForFinalRound(List<int> newStudents)
        {
            // 将3名新学生和1名预留学生随机分配到4个框中
            var allStudents = new List<int>(newStudents) { _currentSelectedStudentId };
            allStudents = allStudents.OrderBy(x => _random.Next()).ToList();

            // 更新显示 - 确保头像和文字都更新
            if (allStudents.Count > 0)
            {
                randomStudent1Info.Text = $"{_studentData[allStudents[0]]}\n{allStudents[0]}";
                randomStudent1Avatar.Source = GetAvatarFromCache(allStudents[0]);
                // 设置背景头像
                ImageSource bg1 = GetAvatarFromCache(allStudents[0]);
                if (bg1 != null && randomStudent1Bg != null)
                {
                    randomStudent1Bg.Source = bg1;
                }
            }
            if (allStudents.Count > 1)
            {
                randomStudent2Info.Text = $"{_studentData[allStudents[1]]}\n{allStudents[1]}";
                randomStudent2Avatar.Source = GetAvatarFromCache(allStudents[1]);
                ImageSource bg2 = GetAvatarFromCache(allStudents[1]);
                if (bg2 != null && randomStudent2Bg != null)
                {
                    randomStudent2Bg.Source = bg2;
                }
            }
            if (allStudents.Count > 2)
            {
                randomStudent3Info.Text = $"{_studentData[allStudents[2]]}\n{allStudents[2]}";
                randomStudent3Avatar.Source = GetAvatarFromCache(allStudents[2]);
                ImageSource bg3 = GetAvatarFromCache(allStudents[2]);
                if (bg3 != null && randomStudent3Bg != null)
                {
                    randomStudent3Bg.Source = bg3;
                }
            }
            if (allStudents.Count > 3)
            {
                randomStudent4Info.Text = $"{_studentData[allStudents[3]]}\n{allStudents[3]}";
                randomStudent4Avatar.Source = GetAvatarFromCache(allStudents[3]);
                ImageSource bg4 = GetAvatarFromCache(allStudents[3]);
                if (bg4 != null && randomStudent4Bg != null)
                {
                    randomStudent4Bg.Source = bg4;
                }
            }
        }

        private void PlayFinalAnimation()
        {
            ResetStudentPositionsToTop();
            var storyboard = (Storyboard)FindResource("StudentsFinalAnimation");
            storyboard.Completed += FinalAnimation_Completed;
            storyboard.Begin();
        }

       

        // 修改HighlightSelectedStudent方法，为UP学生添加更明显的效果
        private void HighlightSelectedStudent()
        {
            // 检查每个学生框，找到包含选中学生的那个
            Border selectedBorder = null;

            if (randomStudent1Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                selectedBorder = randomStudent1;
            }
            else if (randomStudent2Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                selectedBorder = randomStudent2;
            }
            else if (randomStudent3Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                selectedBorder = randomStudent3;
            }
            else if (randomStudent4Info.Text.Contains(_currentSelectedStudentId.ToString()))
            {
                selectedBorder = randomStudent4;
            }

            if (selectedBorder != null)
            {
                // 如果是UP学生，使用金色高亮
                if (_isCurrentStudentUp)
                {
                    AnimateGoldHighlight(selectedBorder);
                }
                else
                {
                    AnimateHighlight(selectedBorder);
                }
            }
        }

        private void AnimateHighlight(Border studentBorder)
        {
            // 创建高亮动画
            var storyboard = new Storyboard();

            // 边框颜色闪烁动画
            var colorAnimation = new ColorAnimation
            {
                From = Colors.White,
                To = Colors.Yellow,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            Storyboard.SetTarget(colorAnimation, studentBorder);
            Storyboard.SetTargetProperty(colorAnimation, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));

            // 缩放动画
            var scaleAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.1,
                Duration = TimeSpan.FromSeconds(0.3),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            var scaleTransform = new ScaleTransform();
            studentBorder.RenderTransform = scaleTransform;
            studentBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            Storyboard.SetTarget(scaleAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("ScaleX"));

            var scaleAnimationY = new DoubleAnimation
            {
                From = 1.0,
                To = 1.1,
                Duration = TimeSpan.FromSeconds(0.3),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            Storyboard.SetTarget(scaleAnimationY, scaleTransform);
            Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("ScaleY"));

            storyboard.Children.Add(colorAnimation);
            storyboard.Children.Add(scaleAnimation);
            storyboard.Children.Add(scaleAnimationY);
            storyboard.Begin();
        }

        // 添加新方法：生成随机学生
        // 修改 GenerateRandomStudents 方法，确保生成正确的随机学生
        private void GenerateRandomStudents(int count)
        {
            _randomStudentIds.Clear();

            // 获取所有学生ID（排除当前选中的学生）
            var availableIds = _studentData.Keys.Where(id => id != _currentSelectedStudentId).ToList();

            // 如果可用学生数量不足，直接返回
            if (availableIds.Count < count)
            {
                for (int i = 0; i < Math.Min(availableIds.Count, count); i++)
                {
                    _randomStudentIds.Add(availableIds[i]);
                }
                return;
            }

            // 随机选择不重复的学生
            var random = new Random();
            var selectedIndices = new HashSet<int>();

            while (_randomStudentIds.Count < count && selectedIndices.Count < availableIds.Count)
            {
                int randomIndex = random.Next(availableIds.Count);
                if (selectedIndices.Add(randomIndex))
                {
                    _randomStudentIds.Add(availableIds[randomIndex]);
                }
            }
        }

        // 添加新方法：更新随机学生显示
        private void UpdateRandomStudentsDisplay()
        {
            // 更新第一个学生
            if (_randomStudentIds.Count > 0)
            {
                int id1 = _randomStudentIds[0];
                randomStudent1Info.Text = $"{_studentData[id1]}\n{id1}";
                randomStudent1Avatar.Source = GetAvatarFromCache(id1);
                randomStudent1Avatar.Visibility = randomStudent1Avatar.Source != null ? Visibility.Visible : Visibility.Collapsed;

                // 设置背景头像 - 现在randomStudent1Bg是Image控件
                ImageSource bg1 = GetAvatarFromCache(id1);
                if (bg1 != null && randomStudent1Bg != null)
                {
                    randomStudent1Bg.Source = bg1;
                }
            }

            // 对其他学生也做同样的修改...
            if (_randomStudentIds.Count > 1)
            {
                int id2 = _randomStudentIds[1];
                randomStudent2Info.Text = $"{_studentData[id2]}\n{id2}";
                randomStudent2Avatar.Source = GetAvatarFromCache(id2);
                randomStudent2Avatar.Visibility = randomStudent2Avatar.Source != null ? Visibility.Visible : Visibility.Collapsed;

                ImageSource bg2 = GetAvatarFromCache(id2);
                if (bg2 != null && randomStudent2Bg != null)
                {
                    randomStudent2Bg.Source = bg2;
                }
            }

            if (_randomStudentIds.Count > 2)
            {
                int id3 = _randomStudentIds[2];
                randomStudent3Info.Text = $"{_studentData[id3]}\n{id3}";
                randomStudent3Avatar.Source = GetAvatarFromCache(id3);
                randomStudent3Avatar.Visibility = randomStudent3Avatar.Source != null ? Visibility.Visible : Visibility.Collapsed;

                ImageSource bg3 = GetAvatarFromCache(id3);
                if (bg3 != null && randomStudent3Bg != null)
                {
                    randomStudent3Bg.Source = bg3;
                }
            }

            if (_randomStudentIds.Count > 3)
            {
                int id4 = _randomStudentIds[3];
                randomStudent4Info.Text = $"{_studentData[id4]}\n{id4}";
                randomStudent4Avatar.Source = GetAvatarFromCache(id4);
                randomStudent4Avatar.Visibility = randomStudent4Avatar.Source != null ? Visibility.Visible : Visibility.Collapsed;

                ImageSource bg4 = GetAvatarFromCache(id4);
                if (bg4 != null && randomStudent4Bg != null)
                {
                    randomStudent4Bg.Source = bg4;
                }
            }
        }


        private void UpdateStudentDisplay(string info, ImageSource avatar)
        {
            if (studentInfoTextBlock != null)
            {
                studentInfoTextBlock.Text = info;
            }

            if (studentAvatarImage != null)
            {
                studentAvatarImage.Source = avatar;
                studentAvatarImage.Visibility = avatar != null ? Visibility.Visible : Visibility.Collapsed;

                if (avatar == null)
                {
                    studentAvatarImage.Margin = new Thickness(0);
                }
                else
                {
                    studentAvatarImage.Margin = new Thickness(0, 0, 10, 0);
                }
            }
        }




        public void LoadStudentData(Dictionary<int, StudentInfo> studentData)
        {
            _studentData = studentData?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Name
            ) ?? new Dictionary<int, string>();

            // 预加载头像到缓存
            if (studentData != null)
            {
                PreloadAvatars(studentData);
            }

            // 立即更新显示
            if (_currentState == WindowState.Start || _currentState == WindowState.Middle)
            {
                RandomSelectStudent();
            }
        }




        public string GetSelectedStudentName()
        {
            return _currentSelectedStudentId != -1 && _studentData.ContainsKey(_currentSelectedStudentId)
                ? _studentData[_currentSelectedStudentId]
                : string.Empty;
        }

        private void SyncInitialColor()
        {
            secondCapsuleBorder.Background = new SolidColorBrush(Settings.ThemeColor);
        }

        private void SyncColorWithMainWindow(object sender, EventArgs e)
        {
            var storyboard = (Storyboard)FindResource("ColorTransitionStoryboard");
            var colorAnim = (ColorAnimation)storyboard.Children[0];
            colorAnim.From = ((SolidColorBrush)secondCapsuleBorder.Background).Color;
            colorAnim.To = Settings.ThemeColor;
            storyboard.Begin();
        }
        private void UpdateDebugStateInfo()
        {
            if (debugStateTextBlock != null)
            {
                string stateText = _currentState switch
                {
                    WindowState.Start => "开始状态",
                    WindowState.Middle => "中间状态",
                    WindowState.Hidden => "隐藏状态",
                    WindowState.End => "结束状态",
                    _ => "未知状态"
                };

                debugStateTextBlock.Text = $"[{stateText}]";
            }
        }
        /* public void SetState(WindowState state)
         {
             if (_isAnimating) return;

             if (state == WindowState.End)
             {
                 this.Hide();
                 _currentState = WindowState.End;
                 return;
             }

             if (_currentState == WindowState.End)
             {
                 this.Show();
                 secondCapsuleBorder.Visibility = Visibility.Visible;
             }

             // 保存前一个状态用于判断
             WindowState previousState = _currentState;
             _currentState = state;

             switch (state)
             {
                 case WindowState.Start:
                     PlayExpandAnimation();
                     toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/显示.png"));
                     displayArea.Visibility = Visibility.Visible;
                     RandomSelectStudent(); // 开始状态需要点名
                     break;

                 case WindowState.Middle:
                     // 如果是从隐藏状态切换到中间状态，播放展开动画
                     if (previousState == WindowState.Hidden)
                     {
                         PlayExpandAnimation(); // 与开始状态相同的动画
                     }
                     else
                     {
                         secondCapsuleBorder.Width = 800;
                         secondCapsuleBorder.Height = 150;
                     }
                     toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/显示.png"));
                     displayArea.Visibility = Visibility.Visible;
                     // 注意：这里不调用RandomSelectStudent()，保持当前显示的学生
                     break;

                 case WindowState.Hidden:
                     ShrinkToCircle();
                     toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/隐藏.png"));
                     displayArea.Visibility = Visibility.Collapsed;
                     break;
             }
         }*/
        public void SetState(WindowState state)
        {
            if (_isAnimating) return;

            if (state == WindowState.End)
            {
                // 播放关闭动画后再隐藏
                PlayCloseAnimation();
                _currentState = WindowState.End;
                UpdateDebugStateInfo();
                return;
            }
            // 如果是从结束状态切换到开始状态，重置显示
            if (_currentState == WindowState.End && state == WindowState.Start)
            {
                ResetDisplayForNewSelection();
            }
            // 如果是从结束状态切换到其他状态，恢复窗口高度
            if (_currentState == WindowState.End)
            {
                secondCapsuleBorder.Height = _originalHeight; // 恢复原始高度
                this.Show();
                secondCapsuleBorder.Visibility = Visibility.Visible;
            }

            // 保存前一个状态用于判断
            WindowState previousState = _currentState;
            _currentState = state;

            UpdateDebugStateInfo();

            switch (state)
            {
                case WindowState.Start:
                    PlayExpandAnimation();
                    toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/显示.png"));
                    displayArea.Visibility = Visibility.Visible;
                    RandomSelectStudent();
                    break;

                case WindowState.Middle:
                    if (previousState == WindowState.Hidden || previousState == WindowState.Start)
                    {
                        PlayExpandAnimation();
                    }
                    else
                    {
                        secondCapsuleBorder.Width = 800;
                        secondCapsuleBorder.Height = _originalHeight; // 使用原始高度
                    }
                    toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/显示.png"));
                    displayArea.Visibility = Visibility.Visible;
                    break;

                case WindowState.Hidden:
                    ShrinkToCircle();
                    toggleImage.Source = new BitmapImage(new Uri("pack://application:,,,/隐藏.png"));
                    displayArea.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // 添加关闭动画方法
        private void PlayCloseAnimation()
        {
            _isAnimating = true;
            var storyboard = (Storyboard)FindResource("CloseWindowAnimation");
            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
                this.Hide(); // 动画完成后隐藏窗口

                // 恢复窗口高度以确保下次正常显示
                secondCapsuleBorder.Height = _originalHeight;
            };
            storyboard.Begin();
        }

        private void PlayExpandAnimation()
        {
            _isAnimating = true;
            secondCapsuleBorder.Visibility = Visibility.Visible;
            secondCapsuleBorder.Width = 0;
            secondCapsuleBorder.Height = 0;

            var storyboard = (Storyboard)FindResource("ExpandAnimation");
            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
            };
            storyboard.Begin();
        }

        private void ShrinkToCircle()
        {
            _isAnimating = true;
            var storyboard = (Storyboard)FindResource("ShrinkToCircleAnimation");
            storyboard.Completed += (s, e) =>
            {
                _isAnimating = false;
            };
            storyboard.Begin();
        }

        private void ToggleImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating) return;

            // 使用专属的弹性动画
            var storyboard = (Storyboard)FindResource("ToggleImageClickAnimation");
            storyboard.Completed -= ToggleButton_AnimationCompleted;
            storyboard.Completed += ToggleButton_AnimationCompleted;
            Storyboard.SetTarget(storyboard, toggleImage);
            storyboard.Begin();
        }

        private void ToggleButton_AnimationCompleted(object sender, EventArgs e)
        {
            // 修复逻辑：根据当前状态决定下一步操作
            switch (_currentState)
            {
                case WindowState.Start:
                case WindowState.Middle:
                    // 当前是显示状态，点击后隐藏
                    SetState(WindowState.Hidden);
                    break;

                case WindowState.Hidden:
                    // 当前是隐藏状态，点击后显示（回到Middle状态）
                    SetState(WindowState.Middle);
                    break;

                case WindowState.End:
                    // 如果是结束状态，点击后重新开始
                    SetState(WindowState.Start);
                    break;
            }
            _isAnimating = false;
        }

        private void ToggleNumberSettingsWindow()
        {
            if (_isNumberSettingsWindowOpen)
            {
                CloseNumberSettingsWindow();
            }
            else
            {
                ShowNumberSettingsWindow();
            }
        }

        private void ShowNumberSettingsWindow()
        {
            if (_numberSettingsWindow == null)
            {
                _numberSettingsWindow = new NumberSettingsWindow();
                _numberSettingsWindow.Closed += (s, e) =>
                {
                    _isNumberSettingsWindowOpen = false;
                    _numberSettingsWindow = null;
                };
            }

            // 定位窗口
            _numberSettingsWindow.PositionBelowSecondWindow(this);

            // 同步颜色
            _numberSettingsWindow.UpdateThemeColor(((SolidColorBrush)secondCapsuleBorder.Background).Color);

            _numberSettingsWindow.Show();
            _isNumberSettingsWindowOpen = true;
        }


        private void CloseNumberSettingsWindow()
        {
            _numberSettingsWindow?.Close();
            _isNumberSettingsWindowOpen = false;
            _numberSettingsWindow = null;
        }
        private void CloseImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating) return;

            var storyboard = (Storyboard)FindResource("CloseImageClickAnimation");
            storyboard.Completed -= CloseButton_AnimationCompleted;
            storyboard.Completed += CloseButton_AnimationCompleted;
            Storyboard.SetTarget(storyboard, closeImage);
            storyboard.Begin();
        }

        private void CloseButton_AnimationCompleted(object sender, EventArgs e)
        {
            // 关闭逻辑 - 播放关闭动画
            SetState(WindowState.End);
            _isAnimating = false;
        }

        private void CountImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating) return;

            // 使用专属的弹性动画
            var storyboard = (Storyboard)FindResource("CountImageClickAnimation");
            Storyboard.SetTarget(storyboard, countImage);
            storyboard.Begin();

            // 切换人数设置窗口显示状态
            ToggleNumberSettingsWindow();
        }

        private void HistoryImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating) return;

            // 播放动画
            var storyboard = (Storyboard)FindResource("HistoryImageClickAnimation");
            Storyboard.SetTarget(storyboard, historyImage);
            storyboard.Begin();

            // 切换历史窗口显示状态
            ToggleHistoryWindow();
        }

        private void ToggleHistoryWindow()
        {
            if (_isHistoryWindowOpen)
            {
                CloseHistoryWindow();
            }
            else
            {
                ShowHistoryWindow();
            }
        }


        private void ShowHistoryWindow()
        {
            if (_historyWindow == null)
            {
                _historyWindow = new HistoryWindow();
                _historyWindow.Closed += (s, e) =>
                {
                    _isHistoryWindowOpen = false;
                    _historyWindow = null;
                };
            }

            // 加载历史数据 - 这行需要移到使用 history 变量之前
            var history = _drawHistoryManager.GetHistory();

            // 使用实际触发UP的学生列表
            _historyWindow.LoadHistory(history, _studentData, _actualUpStudents);

            // 定位窗口
            _historyWindow.PositionBelowSecondWindow(this);

            // 同步颜色
            _historyWindow.UpdateThemeColor(((SolidColorBrush)secondCapsuleBorder.Background).Color);

            _historyWindow.Show();
            _isHistoryWindowOpen = true;
        }

        private void CloseHistoryWindow()
        {
            _historyWindow?.Close();
            _isHistoryWindowOpen = false;
            _historyWindow = null;
        }



        public bool AddStudent(int studentId, string studentName)
        {
            if (studentId < 1 || studentId > 99)
                return false;

            if (_studentData.ContainsKey(studentId))
                return false;

            _studentData[studentId] = studentName;

            // 预加载该学生的头像
            ImageSource avatar = DownloadAvatar(studentId);
            if (avatar != null)
            {
                _avatarCache[studentId] = avatar;
            }

            if (_currentState == WindowState.Start || _currentState == WindowState.Middle)
            {
                RandomSelectStudent();
            }
            return true;
        }

        // 移除学生时也移除缓存中的头像（可选）
        public bool RemoveStudent(int studentId)
        {
            bool removed = _studentData.Remove(studentId);
            if (removed)
            {
                _avatarCache.Remove(studentId); // 同时移除缓存
                if (_currentState == WindowState.Start || _currentState == WindowState.Middle)
                {
                    RandomSelectStudent();
                }
            }
            return removed;
        }

        // 清除学生数据时也清除缓存
        public void ClearStudentData()
        {
            _studentData.Clear();
            _avatarCache.Clear(); // 清除缓存
            if (_currentState == WindowState.Start || _currentState == WindowState.Middle)
            {
                UpdateStudentDisplay("暂无学生数据", null);
            }
        }

        public class DrawHistoryManager
        {
            private readonly int _maxHistorySize;
            private readonly List<int> _drawHistory;

            public DrawHistoryManager(int maxHistorySize = 20)
            {
                _maxHistorySize = maxHistorySize;
                _drawHistory = new List<int>(maxHistorySize);
                LoadHistoryFromFile(); // 启动时加载历史记录
            }

            public void AddToHistory(int studentId)
            {
                if (_drawHistory.Contains(studentId))
                {
                    _drawHistory.Remove(studentId);
                }

                _drawHistory.Insert(0, studentId);

                if (_drawHistory.Count > _maxHistorySize)
                {
                    _drawHistory.RemoveAt(_drawHistory.Count - 1);
                }

                SaveHistoryToFile(); // 每次添加后保存
            }

            public bool IsInHistory(int studentId)
            {
                return _drawHistory.Contains(studentId);
            }

            public void ClearHistory()
            {
                _drawHistory.Clear();
                SaveHistoryToFile(); // 清除后保存
            }

            public List<int> GetHistory()
            {
                return new List<int>(_drawHistory);
            }

            public int HistoryCount => _drawHistory.Count;

            // 新增：保存历史记录到文件
            private void SaveHistoryToFile()
            {
                try
                {
                    string historyPath = GetHistoryFilePath();
                    string directory = IOPath.GetDirectoryName(historyPath);

                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };

                    string json = JsonSerializer.Serialize(_drawHistory, options);
                    File.WriteAllText(historyPath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"保存历史记录失败: {ex.Message}");
                }
            }

            // 新增：从文件加载历史记录
            private void LoadHistoryFromFile()
            {
                try
                {
                    string historyPath = GetHistoryFilePath();

                    if (File.Exists(historyPath))
                    {
                        string json = File.ReadAllText(historyPath);
                        var history = JsonSerializer.Deserialize<List<int>>(json);

                        if (history != null)
                        {
                            _drawHistory.Clear();
                            _drawHistory.AddRange(history.Take(_maxHistorySize)); // 确保不超过最大大小
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载历史记录失败: {ex.Message}");
                }
            }

            // 获取历史记录文件路径
            private static string GetHistoryFilePath()
            {
                return IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "testdemo", "draw_history.json");
            }
        }

        private void ShowFinalResult()
        {
            // 隐藏随机学生网格
            randomStudentsGrid.Visibility = Visibility.Collapsed;

            // 显示最终结果边框
            finalResultBorder.Visibility = Visibility.Visible;

            // 设置最终结果内容
            if (_currentSelectedStudentId != -1 && _studentData.ContainsKey(_currentSelectedStudentId))
            {
                string studentName = _studentData[_currentSelectedStudentId];
                finalResultText.Text = $"{studentName}\n{_currentSelectedStudentId}";

                // 设置头像 - 确保从缓存获取最新头像
                ImageSource avatar = GetAvatarFromCache(_currentSelectedStudentId);
                finalResultAvatar.Source = avatar;
                finalResultAvatar.Visibility = avatar != null ? Visibility.Visible : Visibility.Collapsed;

                // 设置背景 - 关键修复：确保使用当前学生的头像作为背景
                if (avatar != null)
                {
                    // 创建新的ImageBrush，确保背景图片正确更新
                    finalResultBorder.Background = new ImageBrush
                    {
                        ImageSource = avatar,
                        Stretch = Stretch.UniformToFill,
                        Opacity = 0.37
                    };
                }

                // 如果是UP学生，添加金色特效
                if (_isCurrentStudentUp)
                {
                    ApplyUpStudentEffects();
                }
            }

            UpdateInfoArea();

            // 添加一些动画效果
            AnimateFinalResultAppearance();
        }

        private void AnimateFinalResultAppearance()
        {
            // 创建淡入和缩放动画
            var storyboard = new Storyboard();

            // 淡入动画
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            Storyboard.SetTarget(fadeInAnimation, finalResultBorder);
            Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath("Opacity"));

            // 缩放动画
            var scaleAnimationX = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1 }
            };

            var scaleAnimationY = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1 }
            };

            // 确保有变换
            if (finalResultBorder.RenderTransform is not ScaleTransform)
            {
                finalResultBorder.RenderTransform = new ScaleTransform();
                finalResultBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            Storyboard.SetTarget(scaleAnimationX, finalResultBorder.RenderTransform);
            Storyboard.SetTargetProperty(scaleAnimationX, new PropertyPath("ScaleX"));

            Storyboard.SetTarget(scaleAnimationY, finalResultBorder.RenderTransform);
            Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("ScaleY"));

            storyboard.Children.Add(fadeInAnimation);
            storyboard.Children.Add(scaleAnimationX);
            storyboard.Children.Add(scaleAnimationY);

            storyboard.Begin();
        }
        private void ResetDisplayForNewSelection()
        {
            // 隐藏最终结果
            finalResultBorder.Visibility = Visibility.Collapsed;
            finalResultBorder.Opacity = 0;

            // 重置最终结果边框样式
            finalResultBorder.BorderThickness = new Thickness(1);
            finalResultBorder.BorderBrush = Brushes.White;

            // 重置文本样式
            finalResultText.Foreground = Brushes.White;
            finalResultText.Effect = null;

            // 关键修复：完全重置背景，避免残留旧图片
            finalResultBorder.Background = new SolidColorBrush(Colors.Transparent);

            // 重置变换
            finalResultBorder.RenderTransform = new ScaleTransform(1, 1);

            // 显示随机学生网格（但先隐藏，等动画开始再显示）
            randomStudentsGrid.Visibility = Visibility.Collapsed;

            // 清除内容
            finalResultText.Text = "";
            finalResultAvatar.Source = null;

            // 重置UP标志
            _isCurrentStudentUp = false;

            // 恢复窗口背景色（如果需要）
            var currentColor = ((SolidColorBrush)secondCapsuleBorder.Background).Color;
            if (currentColor != Settings.ThemeColor)
            {
                secondCapsuleBorder.Background = new SolidColorBrush(Settings.ThemeColor);
            }
        }
        // 辅助方法：播放动画并等待完成
        private async Task PlayAnimationAsync(string animationKey, int durationMs, CancellationToken cancellationToken)
        {
            var storyboard = (Storyboard)FindResource(animationKey);

            // 重置学生位置到顶部
            ResetStudentPositionsToTop();

            // 更新随机学生显示（除了最终轮）
            if (animationKey != "StudentsFinalAnimation")
            {
                GenerateRandomStudents(4);
                UpdateRandomStudentsDisplay();
            }

            var tcs = new TaskCompletionSource<bool>();

            void OnCompleted(object sender, EventArgs e)
            {
                storyboard.Completed -= OnCompleted;
                tcs.TrySetResult(true);
            }

            storyboard.Completed += OnCompleted;
            storyboard.Begin();

            // 使用 WhenAny 来同时等待动画完成或取消
            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(durationMs + 100, cancellationToken) // 给一点额外时间缓冲
            );

            if (completedTask == tcs.Task)
            {
                // 动画正常完成
                await tcs.Task;
            }
            else
            {
                // 超时或被取消，停止动画
                storyboard.Stop();
                storyboard.Completed -= OnCompleted;
                throw new OperationCanceledException();
            }
        }

        // 添加清空抽奖历史记录的方法
        public void ClearDrawHistory()
        {
            _drawHistoryManager.ClearHistory();

            // 可选：如果需要立即刷新历史窗口显示
            if (_isHistoryWindowOpen && _historyWindow != null)
            {
                _historyWindow.LoadHistory(new List<int>(), _studentData);
            }
        }

        // 添加静态方法供其他类访问头像缓存
        public static ImageSource GetAvatarFromCacheStatic(int studentId)
        {
            if (_avatarCache.TryGetValue(studentId, out ImageSource avatar))
            {
                return avatar;
            }
            return null;
        }

        // 在 SecondWindow 类中添加
        // 修改SecondWindow.xaml.cs中的DrawMultipleStudentsAsync方法


        // 删除或注释掉ShowMultipleResultsAsync方法，因为现在在NumberSettingsWindow中显示


        /*  private Border CreateStudentBorder(int studentId, string studentName)
          {
              // 模仿现有的圆角矩形框样式
              var border = new Border
              {
                  Width = 150,
                  Height = 150,
                  Margin = new Thickness(10),
                  CornerRadius = new CornerRadius(20),
                  BorderThickness = new Thickness(2),
                  BorderBrush = new SolidColorBrush(Colors.White),
                  Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
              };

              var grid = new Grid();

              // 背景头像（模糊效果）
              var backgroundImage = new Image
              {
                  Source = GetAvatarFromCache(studentId),
                  Stretch = Stretch.UniformToFill,
                  Opacity = 0.3
              };

              // 前景内容
              var stackPanel = new StackPanel
              {
                  VerticalAlignment = VerticalAlignment.Center,
                  HorizontalAlignment = HorizontalAlignment.Center
              };

              var avatarImage = new Image
              {
                  Source = GetAvatarFromCache(studentId),
                  Width = 60,
                  Height = 60,
                  Margin = new Thickness(0, 0, 0, 10)
              };

              var textBlock = new TextBlock
              {
                  Text = $"{studentName}\n{studentId}",
                  Foreground = Brushes.White,
                  FontSize = 16,
                  FontWeight = FontWeights.Bold,
                  TextAlignment = TextAlignment.Center
              };

              stackPanel.Children.Add(avatarImage);
              stackPanel.Children.Add(textBlock);

              grid.Children.Add(backgroundImage);
              grid.Children.Add(stackPanel);
              border.Child = grid;

              return border;
          }*/
        // 修改DrawMultipleStudentsAsync方法，修复重复选择问题
        public async Task<List<int>> DrawMultipleStudentsAsync(int count)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 开始多人抽选 ===");
            Console.WriteLine($"DrawMultipleStudentsAsync 被调用，count: {count}, 当前时间: {DateTime.Now:HH:mm:ss.fff}");

            var results = new List<int>();

            if (count <= 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("count <= 0，返回空列表");
                Console.ResetColor();
                return results;
            }

            // 获取可用学生（排除历史记录中的）
            // 普通抽选逻辑，不涉及UP池
            var availableStudents = _studentData.Keys
                .Where(id => !_drawHistoryManager.IsInHistory(id))
                .ToList();

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"可用学生数量: {availableStudents.Count}, 总学生数量: {_studentData.Count}");

            if (availableStudents.Count < count)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("可用学生不足，重置历史记录");
                _drawHistoryManager.ClearHistory();
                availableStudents = _studentData.Keys.ToList();
                Console.WriteLine($"重置后可用学生数量: {availableStudents.Count}");
            }

            // 随机选择不重复的学生
            var random = new Random();
            var selectedStudents = new List<int>();

            // 随机打乱可用学生列表
            var shuffledStudents = availableStudents.OrderBy(x => random.Next()).ToList();

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"打乱后学生列表: {string.Join(", ", shuffledStudents.Take(10))}..." + (shuffledStudents.Count > 10 ? $" (共{shuffledStudents.Count}人)" : ""));

            // 选择前count个学生，确保不重复
            for (int i = 0; i < Math.Min(count, shuffledStudents.Count); i++)
            {
                int selectedId = shuffledStudents[i];

                if (!selectedStudents.Contains(selectedId))
                {
                    selectedStudents.Add(selectedId);
                    _drawHistoryManager.AddToHistory(selectedId);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"选中学生[{i + 1}/{count}]: {_studentData[selectedId]}({selectedId})");
                }
            }

            // 保存到UP池
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("正在将选中学生保存到UP池...");
            SaveToUpPool(selectedStudents);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== 多人抽选完成 ===");
            Console.WriteLine($"最终选中 {selectedStudents.Count} 人: {string.Join(", ", selectedStudents.Select(id => $"{_studentData[id]}({id})"))}");
            Console.ResetColor();

            return selectedStudents;
        }

        // 修改DrawSingleStudentWithAnimationAsync方法，确保每次只选一个
      
        // 添加获取选中学生ID的方法
        public int GetSelectedStudentId()
        {
            return _currentSelectedStudentId;
        }
        private void InitializeUpPool()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("程序启动，开始检查存储文件中的UP池信息");

            string currentUpPath = GetCurrentUpPoolFilePath();
            string historyUpPath = GetHistoryUpPoolFilePath();

            // 加载历史UP池
            if (File.Exists(historyUpPath))
            {
                Console.WriteLine("检测到历史UP池信息，开始读取");
                _historyUpPool = LoadUpPoolFromFile(historyUpPath);

                // 确保不超过20人
                if (_historyUpPool.Count > 20)
                {
                    _historyUpPool = _historyUpPool.Take(20).ToList();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"历史UP池超过20人，已截取前20人");
                }

                Console.WriteLine($"历史UP池信息读取完成，包含[{_historyUpPool.Count}/20]条记录");
            }

            // 加载当前UP池
            if (File.Exists(currentUpPath))
            {
                Console.WriteLine("检测到当前UP池信息，开始读取");
                _currentUpPool = LoadUpPoolFromFile(currentUpPath);
                Console.WriteLine($"当前UP池信息读取完成: {string.Join(", ", _currentUpPool.Select(id => $"{_studentData[id]}({id})"))}");
            }
            else
            {
                Console.WriteLine("未检测到当前UP池信息，首次启动，初始化当前UP池");
                InitializeNewCurrentUpPool();
            }

            Console.ResetColor();
        }

        // 初始化新的当前UP池

        private List<int> SelectRandomStudentsExcludingHistoryUpPool(int count, List<int> allStudents)
        {
            var available = allStudents.Except(_historyUpPool).ToList();
            var random = new Random();
            var selected = new List<int>();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"从历史UP池外选择{count}人，排除{_historyUpPool.Count}个历史UP学生");
            Console.WriteLine($"可用学生数量: {available.Count}");

            if (available.Count < count)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"可用学生不足{count}人，清空历史UP池重新选择");
                _historyUpPool.Clear();
                available = allStudents;
            }

            // 随机选择
            while (selected.Count < count && available.Count > 0)
            {
                int index = random.Next(available.Count);
                int id = available[index];
                selected.Add(id);
                available.RemoveAt(index);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"选中UP学生[{selected.Count}/{count}]: {_studentData[id]}({id})");
            }

            Console.ResetColor();
            return selected;
        }
        private string GetUpPoolFilePath()
        {
            return IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "testdemo", "up_pool.json");
        }
        private List<int> LoadUpPoolFromFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
            }
            catch
            {
                return new List<int>();
            }
        }
        private List<int> SelectRandomStudentsExcludingUpPool(int count, List<int> allStudents)
        {
            var upPool = LoadUpPoolFromFile(GetUpPoolFilePath());
            var available = allStudents.Except(upPool).ToList();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"开始随机选取{count}人，排除UP池中的{upPool.Count}人");
            Console.WriteLine($"UP池中的学生: {(upPool.Count > 0 ? string.Join(", ", upPool.Select(id => $"{_studentData[id]}({id})")) : "无")}");
            Console.WriteLine($"可用学生数量: {available.Count}");

            var selected = new List<int>();
            var random = new Random();

            // 如果可用学生不足，使用所有学生
            if (available.Count < count)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"可用学生不足{count}人，使用所有学生进行选择");
                available = allStudents;
            }

            // 随机打乱并选择
            var shuffled = available.OrderBy(x => random.Next()).ToList();

            for (int i = 0; i < Math.Min(count, shuffled.Count); i++)
            {
                int id = shuffled[i];
                selected.Add(id);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"选中[{i + 1}/{count}]: {_studentData[id]}({id})");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"成功选取{selected.Count}个不重复的人");
            Console.ResetColor();

            return selected;
        }
        private void SaveToUpPool(List<int> newStudents)
        {
            if (newStudents == null || newStudents.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("SaveToUpPool: 传入的学生列表为空");
                Console.ResetColor();
                return;
            }

            var upPool = LoadUpPoolFromFile(GetUpPoolFilePath());

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"UP池当前状态: {upPool.Count}/20 人");
            if (upPool.Count > 0)
            {
                Console.WriteLine($"当前UP池: {string.Join(", ", upPool.Take(5).Select(id => $"{_studentData[id]}({id})"))}" +
                                 (upPool.Count > 5 ? "..." : ""));
            }

            foreach (var id in newStudents)
            {
                if (upPool.Count >= 20)
                {
                    var removed = upPool[19];
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"UP池已满(20/20)，新增{_studentData[id]}({id})到第0号位置，移出最后一位: {_studentData[removed]}({removed})");
                    upPool.RemoveAt(19);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"UP池当前有[{upPool.Count}]人，新增{_studentData[id]}({id})到第0号位置");
                }

                upPool.Insert(0, id);
            }

            SaveUpPoolToFile(upPool);

            // 显示更新后的UP池状态
            var updatedPool = LoadUpPoolFromFile(GetUpPoolFilePath());
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"UP池更新完成: {updatedPool.Count}/20 人");
            Console.WriteLine($"最新UP池: {string.Join(", ", updatedPool.Take(5).Select(id => $"{_studentData[id]}({id})"))}" +
                             (updatedPool.Count > 5 ? "..." : ""));
            Console.ResetColor();
        }
        private void SaveUpPoolToFile(List<int> upPool)
        {
            try
            {
                string path = GetUpPoolFilePath();
                string dir =  IOPath.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(upPool, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"保存up池失败: {ex.Message}");
            }
        }
        // 判断是否应该从当前UP池中抽选


        // 将当前UP池加入历史

        private void AddCurrentUpPoolToHistory()
        {
            try
            {
                // 加载现有的历史UP池
                var currentHistory = LoadUpPoolFromFile(GetHistoryUpPoolFilePath());

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"准备将当前UP池加入历史，当前历史UP池有{currentHistory.Count}/20人");
                Console.WriteLine($"当前UP池: {string.Join(", ", _currentUpPool.Select(id => $"{_studentData[id]}({id})"))}");

                // 将当前UP池的学生添加到历史记录的开头（去重）
                foreach (var studentId in _currentUpPool)
                {
                    // 先从历史记录中移除该学生（如果已存在）
                    currentHistory.Remove(studentId);

                    // 添加到开头
                    currentHistory.Insert(0, studentId);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"添加 {_studentData[studentId]}({studentId}) 到历史UP池");
                }

                // 确保不超过20人
                if (currentHistory.Count > 20)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"历史UP池超过20人，截取前20人，移除 {currentHistory.Count - 20} 人");
                    currentHistory = currentHistory.Take(20).ToList();
                }

                _historyUpPool = currentHistory;
                SaveHistoryUpPoolToFile();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"当前UP池已加入历史，历史UP池现在有{_historyUpPool.Count}/20个学生");
                Console.WriteLine($"历史UP池: {string.Join(", ", _historyUpPool.Select(id => $"{_studentData[id]}({id})"))}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"添加当前UP池到历史时出错: {ex.Message}");
                Console.ResetColor();
            }
        }
        // 添加文件路径方法
        private string GetCurrentUpPoolFilePath()
        {
            return IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "testdemo", "current_up_pool.json");
        }

        private string GetHistoryUpPoolFilePath()
        {
            return IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "testdemo", "history_up_pool.json");
        }

        // 添加保存方法
        private void SaveCurrentUpPoolToFile()
        {
            try
            {
                string path = GetCurrentUpPoolFilePath();
                string dir = IOPath.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_currentUpPool, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"保存当前UP池失败: {ex.Message}");
                Console.ResetColor();
            }
        }

        private void SaveHistoryUpPoolToFile()
        {
            try
            {
                string path = GetHistoryUpPoolFilePath();
                string dir = IOPath.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_historyUpPool, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"已保存历史UP池到文件: {_historyUpPool.Count}/20人");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"保存历史UP池失败: {ex.Message}");
                Console.ResetColor();
            }
        }

        // 添加UP相关的辅助方法
        public bool IsStudentInCurrentUpPool(int studentId)
        {
            return _currentUpPool.Contains(studentId);
        }

        public string GetStudentName(int studentId)
        {
            return _studentData.ContainsKey(studentId) ? _studentData[studentId] : "未知学生";
        }

        // 修改批量抽选方法，确保UP机制生效
        private async Task PlayWindowGoldAnimationAsync(CancellationToken cancellationToken)
        {
            // 先播放全屏金色水波动画
            await ShowGoldWaveAnimationAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            var storyboard = (Storyboard)FindResource("WindowToGoldAnimation");
            var tcs = new TaskCompletionSource<bool>();

            void OnCompleted(object sender, EventArgs e)
            {
                storyboard.Completed -= OnCompleted;
                tcs.TrySetResult(true);
            }

            storyboard.Completed += OnCompleted;
            storyboard.Begin();

            // 等待动画完成或取消
            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(2000, cancellationToken) // 2秒超时
            );

            if (completedTask != tcs.Task)
            {
                storyboard.Stop();
                storyboard.Completed -= OnCompleted;
                throw new OperationCanceledException();
            }

            await tcs.Task;
        }

        // 添加新的全屏水波动画方法
        private async Task ShowGoldWaveAnimationAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            // 在UI线程上创建和显示动画窗口
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _goldAnimationWindow = new GoldAnimationWindow();
                    _goldAnimationWindow.Closed += (s, e) =>
                    {
                        tcs.TrySetResult(true);
                        _goldAnimationWindow = null;
                    };

                    _goldAnimationWindow.Show();
                    _goldAnimationWindow.StartAnimation();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建金色动画窗口失败: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            });

            // 等待动画完成或取消
            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(1500, cancellationToken) // 1.5秒超时
            );

            if (completedTask != tcs.Task)
            {
                // 超时或被取消，关闭窗口
                await Dispatcher.InvokeAsync(() =>
                {
                    _goldAnimationWindow?.Close();
                    _goldAnimationWindow = null;
                });
                throw new OperationCanceledException();
            }

            await tcs.Task;
        }

        // 添加UP学生特效应用方法
        private void ApplyUpStudentEffects()
        {
            // 修改最终结果边框样式
            finalResultBorder.BorderThickness = new Thickness(4);
            finalResultBorder.BorderBrush = (SolidColorBrush)FindResource("GoldBorderBrush");

            // 修改文本颜色为金色
            finalResultText.Foreground = (SolidColorBrush)FindResource("GoldTextBrush");

            // 添加文本发光效果
            var glowEffect = new DropShadowEffect
            {
                Color = Colors.Gold,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.8
            };
            finalResultText.Effect = glowEffect;

            // 关键修复：确保背景图片仍然显示，只是添加金色叠加
            var currentBackground = finalResultBorder.Background as ImageBrush;
            if (currentBackground != null)
            {
                // 创建容器视觉
                var container = new Grid();

                // 原有的背景图片
                var backgroundRect = new Rectangle();
                backgroundRect.Fill = currentBackground;

                // 金色叠加层
                var goldOverlay = new Rectangle
                {
                    Fill = (LinearGradientBrush)FindResource("GoldGradientBrush"),
                    Opacity = 0.2
                };

                container.Children.Add(backgroundRect);
                container.Children.Add(goldOverlay);

                finalResultBorder.Background = new VisualBrush
                {
                    Visual = container,
                    Stretch = Stretch.UniformToFill
                };
            }
        }

        // 添加金色高亮动画方法
        private void AnimateGoldHighlight(Border studentBorder)
        {
            // 创建金色高亮动画
            var storyboard = new Storyboard();

            // 边框颜色闪烁动画（金色）
            var colorAnimation = new ColorAnimation
            {
                From = Colors.White,
                To = Colors.Gold,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            Storyboard.SetTarget(colorAnimation, studentBorder);
            Storyboard.SetTargetProperty(colorAnimation, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));

            // 更明显的缩放动画
            var scaleAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.15,
                Duration = TimeSpan.FromSeconds(0.3),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            var scaleTransform = new ScaleTransform();
            studentBorder.RenderTransform = scaleTransform;
            studentBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            Storyboard.SetTarget(scaleAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleAnimation, new PropertyPath("ScaleX"));

            var scaleAnimationY = new DoubleAnimation
            {
                From = 1.0,
                To = 1.15,
                Duration = TimeSpan.FromSeconds(0.3),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            Storyboard.SetTarget(scaleAnimationY, scaleTransform);
            Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath("ScaleY"));

            storyboard.Children.Add(colorAnimation);
            storyboard.Children.Add(scaleAnimation);
            storyboard.Children.Add(scaleAnimationY);
            storyboard.Begin();
        }
        // 添加UP学生高亮显示的方法
        private void HighlightUpStudent(Border studentBorder)
        {
            if (studentBorder == null) return;

            // 显示金色叠加层
            var goldOverlay = studentBorder.FindName("randomStudent1GoldOverlay") as Rectangle;
            if (goldOverlay != null)
            {
                goldOverlay.Visibility = Visibility.Visible;
            }

            // 加粗边框
            studentBorder.BorderThickness = new Thickness(4);
            studentBorder.BorderBrush = (SolidColorBrush)FindResource("GoldBorderBrush");
        }

        // 在动画完成后调用高亮UP学生
        private void FinalAnimation_Completed(object sender, EventArgs e)
        {
            // 动画完成，显示最终结果
            debugStateTextBlock.Text = $"[选中: {_studentData[_currentSelectedStudentId]}({_currentSelectedStudentId})]";

            // 找到包含选中学生的框
            Border selectedBorder = FindSelectedStudentBorder();

            // 如果是UP学生，添加特殊效果
            if (_isCurrentStudentUp && selectedBorder != null)
            {
                HighlightUpStudent(selectedBorder);
            }

            // 高亮显示选中的学生
            HighlightSelectedStudent();
        }
        // 添加更新信息区的方法
        // 修改UpdateInfoArea方法中的UP显示部分
        private void UpdateInfoArea()
        {
            // 更新抽数显示
            drawCountText.Text = $"当前抽数: {_drawCountSinceLastUp + 1}";

            // 更新UP显示 - 显示具体学号，每两个换行
            if (_currentUpPool.Count > 0)
            {
                // 按学号排序
                var sortedIds = _currentUpPool.OrderBy(id => id).ToList();

                // 每两个学号一组，用逗号分隔，组间换行
                var groupedNumbers = new List<string>();
                for (int i = 0; i < sortedIds.Count; i += 2)
                {
                    if (i + 1 < sortedIds.Count)
                    {
                        // 两个学号一组
                        groupedNumbers.Add($"{sortedIds[i]}, {sortedIds[i + 1]}");
                    }
                    else
                    {
                        // 最后一个单独的学号
                        groupedNumbers.Add(sortedIds[i].ToString());
                    }
                }

                string upNumbers = string.Join("\n", groupedNumbers);
                currentUpText.Text = upNumbers;

                // 添加ToolTip显示完整UP池信息（姓名+学号）
                var upInfo = string.Join("\n", _currentUpPool.Select(id =>
                    $"{GetStudentName(id)} ({id})"));
                currentUpText.ToolTip = $"当前UP池:\n{upInfo}";
            }
            else
            {
                currentUpText.Text = "无UP";
                currentUpText.ToolTip = "暂无UP学生";
            }

            // 更新概率显示
            double probability = CalculateCurrentProbability();
            probabilityText.Text = $"当前UP概率: {probability:P0}";
        }

        // 计算当前概率的方法
        private double CalculateCurrentProbability()
        {
            if (_drawCountSinceLastUp < INCREASED_RATE_DRAW - 1)
            {
                return 0.0; // 小于5抽为0%
            }

            // 根据抽数返回不同概率
            return (_drawCountSinceLastUp - (INCREASED_RATE_DRAW - 1)) switch
            {
                0 => 0.20, // 第5抽: 20%
                1 => 0.40, // 第6抽: 40%
                2 => 0.60, // 第7抽: 60%
                3 => 0.80, // 第8抽: 80%
                _ => 1.00  // 第9抽及以上: 100%
            };
        }
        

        // 获取所有学生数据
        public Dictionary<int, string> GetAllStudents()
        {
            return new Dictionary<int, string>(_studentData);
        }
        private List<int> LoadAllUpStudentsFromHistory()
        {
            try
            {
                // 从历史UP池文件中读取
                string historyUpPath = GetHistoryUpPoolFilePath();
                if (File.Exists(historyUpPath))
                {
                    string json = File.ReadAllText(historyUpPath);
                    return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
                }

                // 如果没有历史文件，返回当前UP池
                return new List<int>(_currentUpPool);
            }
            catch
            {
                return new List<int>();
            }
        }

        // 在 SecondWindow.xaml.cs 中添加
        public void UpdateCurrentUpPool(List<int> newUpPool)
        {
            try
            {
                _currentUpPool = newUpPool;
                SaveCurrentUpPoolToFile();

                // 更新信息显示
                UpdateInfoArea();

                Console.WriteLine($"UP池已更新: {string.Join(", ", newUpPool.Select(id => $"{_studentData[id]}({id})"))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新UP池失败: {ex.Message}");
            }
        }

        // 初始化拖动系统
        private void InitializeDragSystem()
        {
            // 添加鼠标事件处理
            secondCapsuleBorder.MouseLeftButtonDown += CapsuleBorder_MouseLeftButtonDown;
            secondCapsuleBorder.MouseLeftButtonUp += CapsuleBorder_MouseLeftButtonUp;
            secondCapsuleBorder.MouseMove += CapsuleBorder_MouseMove;

            // 删除计时器相关代码
            _dragTimer?.Stop();
            _dragTimer = null;
        }

        // 设置可拖动性
        // 修改 SetDraggable 方法
        // 设置可拖动性
        public void SetDraggable(bool draggable)
        {
            _isDraggable = draggable;

            // 立即更新UI状态
            if (!draggable && _isDragging)
            {
                StopDragging();
            }

            // 视觉反馈
            if (draggable)
            {
                secondCapsuleBorder.Cursor = Cursors.SizeAll;

                // 立即启用拖动事件
                secondCapsuleBorder.MouseLeftButtonDown += CapsuleBorder_MouseLeftButtonDown;
                secondCapsuleBorder.MouseLeftButtonUp += CapsuleBorder_MouseLeftButtonUp;
                secondCapsuleBorder.MouseMove += CapsuleBorder_MouseMove;
            }
            else
            {
                secondCapsuleBorder.Cursor = Cursors.Arrow;

                // 立即禁用拖动事件
                secondCapsuleBorder.MouseLeftButtonDown -= CapsuleBorder_MouseLeftButtonDown;
                secondCapsuleBorder.MouseLeftButtonUp -= CapsuleBorder_MouseLeftButtonUp;
                secondCapsuleBorder.MouseMove -= CapsuleBorder_MouseMove;

                // 确保立即释放鼠标捕获
                if (secondCapsuleBorder.IsMouseCaptured)
                {
                    secondCapsuleBorder.ReleaseMouseCapture();
                }

                // 重置拖动状态
                _isDragging = false;
            }

            Console.WriteLine($"拖动设置已更新: {draggable}, 事件已{(draggable ? "启用" : "禁用")}");
        }

        // 鼠标按下事件
        private void CapsuleBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggable || _isAnimationInProgress) return;

            _dragStartPoint = e.GetPosition(this);
            _isDragging = true;
            secondCapsuleBorder.CaptureMouse();
        }

        // 鼠标释放事件
        private void CapsuleBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || !_isDraggable || _isAnimationInProgress) return;

            Point currentPosition = e.GetPosition(this);
            Vector displacement = currentPosition - _dragStartPoint;

            // 检查是否达到拖动阈值
            if (displacement.Length < DRAG_THRESHOLD) return;

            // 直接应用位移
            this.Left += displacement.X;
            this.Top += displacement.Y;

            // 更新起始点，实现连续拖动
            _dragStartPoint = currentPosition;
        }

        // 修改鼠标释放事件
        private void CapsuleBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            StopDragging();
        }

        // 鼠标离开事件
        private void CapsuleBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging && !secondCapsuleBorder.IsMouseCaptured)
            {
                StopDragging();
            }
        }

        // 拖动计时器处理
        private void DragTimer_Tick(object sender, EventArgs e)
        {
            if (!_isDragging || !_isDraggable || _isAnimationInProgress) return;

            try
            {
                Point currentPosition = Mouse.GetPosition(this);
                Vector displacement = currentPosition - _dragStartPoint;

                // 检查是否达到拖动阈值
                if (displacement.Length < DRAG_THRESHOLD) return;

                // 应用平滑移动
                this.Left += displacement.X * 0.8; // 阻尼系数，使移动更平滑
                this.Top += displacement.Y * 0.8;

                // 更新起始点，实现连续拖动
                _dragStartPoint = currentPosition;
            }
            catch
            {
                StopDragging();
            }
        }

        // 停止拖动
        private void StopDragging()
        {
            if (_isDragging)
            {
                _isDragging = false;
                secondCapsuleBorder.ReleaseMouseCapture();

                // 保存位置
                SaveWindowPosition();
            }
        }

        // 重置到默认位置

        public void ResetToDefaultPosition()
        {
            try
            {
                // 将窗口重置到屏幕中央顶部，考虑缩放
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;

                // 考虑缩放后的实际宽度和高度
                double scaledWidth = this.Width * _currentScale;
                double scaledHeight = this.Height * _currentScale;

                // 确保窗口完全在屏幕内
                this.Left = (screenWidth - scaledWidth) / 2;
                this.Top = Math.Max(50, (screenHeight - scaledHeight) / 4); // 距离顶部1/4处或至少50像素

                Console.WriteLine($"重置到默认位置: Left={this.Left}, Top={this.Top}, 缩放: {_currentScale}");

                // 保存新位置
                SaveWindowPosition();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重置位置失败: {ex.Message}");
            }
        }


        // 保存窗口位置
        private void SaveWindowPosition()
        {
            try
            {
                // 确保位置有效
                if (double.IsNaN(this.Left) || double.IsNaN(this.Top) ||
                    this.Left < 0 || this.Top < 0)
                {
                    Console.WriteLine("窗口位置无效，跳过保存");
                    return;
                }

                var settings = DataService.LoadSettings();
                settings.SecondWindowPosition = new WindowPosition
                {
                    Left = this.Left,
                    Top = this.Top
                };
                DataService.SaveSettings(settings);

                Console.WriteLine($"位置已保存: Left={this.Left}, Top={this.Top}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存位置失败: {ex.Message}");
            }
        }

        // 修改 RestoreWindowPosition 方法
        private void RestoreWindowPosition()
        {
            try
            {
                var settings = DataService.LoadSettings();
                if (settings.SecondWindowPosition != null && settings.SecondWindowPosition.IsValid)
                {
                    // 使用Dispatcher确保在UI线程上设置位置
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.Left = settings.SecondWindowPosition.Left;
                        this.Top = settings.SecondWindowPosition.Top;
                        Console.WriteLine($"位置已恢复: Left={this.Left}, Top={this.Top}");

                        // 确保窗口在屏幕范围内
                        EnsureWindowInScreen();
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ResetToDefaultPosition();
                    }));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"恢复位置失败: {ex.Message}");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResetToDefaultPosition();
                }));
            }
        }

        // 在窗口关闭时保存位置
        private void SecondWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口在屏幕内
            EnsureWindowInScreen();

            // 移除事件处理器，避免重复执行
            this.Loaded -= SecondWindow_Loaded;
        }
    }
}