using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Vchange
{
    public partial class MessageDialog : Window
    {
        public MessageDialog(string message, string title = "提示")
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;

            Loaded += (s, e) =>
            {
                // 淡入 + 轻微放大
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
