using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Vchange
{
    public partial class MessageDialog : Window
    {
        /// <summary>确认模式下用户是否点击了“是”。</summary>
        public bool Confirmed { get; private set; }

        public MessageDialog(string message, string title = "提示", bool confirm = false,
            string? yesText = null, string? noText = null)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;

            if (confirm)
            {
                SingleButtonPanel.Visibility = Visibility.Collapsed;
                ConfirmButtonPanel.Visibility = Visibility.Visible;
                ConfirmYesButton.Content = yesText ?? "是";
                ConfirmNoButton.Content = noText ?? "否";
            }

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
            Confirmed = true;
            Close();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
