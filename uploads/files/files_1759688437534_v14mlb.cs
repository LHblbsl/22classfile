using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace testdemo
{
    public partial class AvatarEditorWindow : Window
    {
        private Dictionary<int, StudentInfo> _studentList;
        private Dictionary<int, bool> _avatarVisibility = new Dictionary<int, bool>();
        private List<StudentAvatarViewModel> _studentViewModels;

        public AvatarEditorWindow(Dictionary<int, StudentInfo> studentList)
        {
            InitializeComponent();
            _studentList = studentList;
            LoadStudentAvatars();
        }

        private void LoadStudentAvatars()
        {
            _studentViewModels = new List<StudentAvatarViewModel>();

            foreach (var kvp in _studentList)
            {
                // 初始化所有头像为可见
                _avatarVisibility[kvp.Key] = kvp.Value.IsAvatarVisible;

                _studentViewModels.Add(new StudentAvatarViewModel
                {
                    StudentId = kvp.Key,
                    Name = kvp.Value.Name,
                    Avatar = kvp.Value.Avatar // 直接使用 StudentInfo 的 Avatar 属性
                });
            }

            AvatarItemsControl.ItemsSource = _studentViewModels;
        }

        private void RefreshAvatarDisplay()
        {
            // 刷新所有头像显示
            foreach (var viewModel in _studentViewModels)
            {
                if (_studentList.ContainsKey(viewModel.StudentId))
                {
                    // 直接使用 StudentInfo 的 Avatar 属性，它会根据可见性返回正确的头像
                    viewModel.Avatar = _studentList[viewModel.StudentId].Avatar;
                }
            }

            // 刷新ItemsControl
            AvatarItemsControl.ItemsSource = null;
            AvatarItemsControl.ItemsSource = _studentViewModels;
        }

        

        private void HideAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is int studentId)
            {
                // 切换头像可见状态
                _avatarVisibility[studentId] = !_avatarVisibility[studentId];

                // 更新学生对象的可见性设置
                if (_studentList.ContainsKey(studentId))
                {
                    _studentList[studentId].IsAvatarVisible = _avatarVisibility[studentId];
                }

                // 刷新显示
                RefreshAvatarDisplay();
            }
        }

        private void HideAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 隐藏所有头像
            foreach (var studentId in _avatarVisibility.Keys.ToList())
            {
                _avatarVisibility[studentId] = false;
                if (_studentList.ContainsKey(studentId))
                {
                    _studentList[studentId].IsAvatarVisible = false;
                }
            }

            // 刷新显示
            RefreshAvatarDisplay();
        }

        private void ShowAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示所有头像
            foreach (var studentId in _avatarVisibility.Keys.ToList())
            {
                _avatarVisibility[studentId] = true;
                if (_studentList.ContainsKey(studentId))
                {
                    _studentList[studentId].IsAvatarVisible = true;
                }
            }

            // 刷新显示
            RefreshAvatarDisplay();
        }

        // AvatarEditorWindow.xaml.cs 中的 ApplyButton_Click 方法修改
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存头像可见性设置到应用设置
                foreach (var kvp in _avatarVisibility)
                {
                    if (_studentList.ContainsKey(kvp.Key))
                    {
                        _studentList[kvp.Key].IsAvatarVisible = kvp.Value;
                    }
                }

                // 通知主窗口更新设置
                if (Owner is Settings settingsWindow)
                {
                    settingsWindow.UpdateAvatarSettings(_studentList);

                    // 立即保存设置到文件
                    settingsWindow.SaveAppSettings();
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class StudentAvatarViewModel
    {
        public int StudentId { get; set; }
        public string Name { get; set; }
        public ImageSource Avatar { get; set; }
    }
}