using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace testdemo
{
    public class WindowPosition
    {
        
        public double Left { get; set; } = -1;  // 使用 -1 代替 double.NaN
        public double Top { get; set; } = -1;   // 使用 -1 代替 double.NaN

        public bool IsValid => Left >= 0 && Top >= 0;  // 有效位置是正数
    }
    public class AppSettings
    {
        // 在 AppSettings 类中添加
        public double MainWindowScale { get; set; } = 1.0;
        public List<int> ExcludedFromUpPool { get; set; } = new List<int>();
        public int AutoColorInterval { get; set; } = 30;
        public bool AutoColorEnabled { get; set; } = false;
        public ColorData ThemeColor { get; set; } = new ColorData(0, 0, 255);
        public bool AutoStartEnabled { get; set; } = false; // 新增自启动设置

        // 确保字典初始化
        public Dictionary<int, StudentInfo> StudentList { get; set; } = new Dictionary<int, StudentInfo>();
        public double SecondWindowScale { get; set; } = 1.0;
        public bool SecondWindowDraggable { get; set; } = true;
        public WindowPosition SecondWindowPosition { get; set; } = new WindowPosition();


    }

    public class ColorData
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public ColorData() { }

        public ColorData(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public System.Windows.Media.Color ToMediaColor()
        {
            return System.Windows.Media.Color.FromArgb(128, R, G, B);
        }

        public static ColorData FromMediaColor(System.Windows.Media.Color color)
        {
            return new ColorData(color.R, color.G, color.B);
        }
    }

    // 新增学生信息类，包含姓名和QQ号
    // AppSettings.cs 中的 StudentInfo 类修改
    // AppSettings.cs 中的 StudentInfo 类修改
    // AppSettings.cs 中的 StudentInfo 类修改
    public class StudentInfo
    {
        public string Name { get; set; } = "";
        public string QQNumber { get; set; } = "";
        public bool IsAvatarVisible { get; set; } = true; // 确保这个属性可以被序列化
        public bool AvatarVisible { get; set; } = true; // 添加缺失的属性
        public bool IsExcludedFromUp { get; set; } = false; // 添加缺失的属性

        // 添加 JsonIgnore 属性，避免序列化头像
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Media.ImageSource Avatar
        {
            get
            {
                // 如果头像被隐藏，返回默认头像
                if (!AvatarVisible) // 修改这里使用 AvatarVisible
                    return CreateDefaultAvatarImage();

                // 优先尝试获取QQ头像，失败则使用默认头像
                try
                {
                    var avatar = GetQQAvatar(QQNumber);
                    if (avatar != null) return avatar;
                }
                catch
                {
                    // 忽略异常，使用默认头像
                }
                return CreateDefaultAvatarImage();
            }
        }

        public StudentInfo() { }

        public StudentInfo(string name, string qqNumber)
        {
            Name = name;
            QQNumber = qqNumber;
            AvatarVisible = true; // 默认显示头像
            IsExcludedFromUp = false; // 默认不排除
        }

        // 同步获取QQ头像的方法
        private System.Windows.Media.ImageSource GetQQAvatar(string qqNumber)
        {
            if (string.IsNullOrEmpty(qqNumber))
                return CreateDefaultAvatarImage();

            try
            {
                // 使用QQ头像API
                string avatarUrl = $"https://q1.qlogo.cn/g?b=qq&nk={qqNumber}&s=100";

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();

                return bitmap;
            }
            catch
            {
                return CreateDefaultAvatarImage();
            }
        }

        // 创建默认头像（使用项目中的默认.png）
        private System.Windows.Media.ImageSource CreateDefaultAvatarImage()
        {
            try
            {
                // 尝试加载项目中的默认.png
                var uri = new Uri("pack://application:,,,/默认.png", UriKind.Absolute);
                return new BitmapImage(uri);
            }
            catch
            {
                // 如果加载失败，创建一个简单的默认头像
                var drawingVisual = new System.Windows.Media.DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawEllipse(
                        System.Windows.Media.Brushes.LightGray,
                        new System.Windows.Media.Pen(System.Windows.Media.Brushes.DarkGray, 1),
                        new System.Windows.Point(20, 20),
                        20, 20);
                }

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(40, 40, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(drawingVisual);
                return bitmap;
            }
        }
    }
}