using System.Windows;

namespace SqlToER.View
{
    public partial class OptimizeRoundDialog : Window
    {
        /// <summary>
        /// 用户设置的优化轮数（确认后读取）
        /// </summary>
        public int Rounds { get; private set; } = 3;

        public OptimizeRoundDialog()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(RoundsTextBox.Text.Trim(), out int val) && val >= 1 && val <= 20)
            {
                Rounds = val;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("请输入 1~20 的整数", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
