using System.Windows;

namespace VirtualDesktopBar
{
    public partial class HotkeySettingsWindow : Window
    {
        public uint ResultSwitchModifiers { get; private set; }
        public uint ResultMoveModifiers { get; private set; }

        public HotkeySettingsWindow(uint currentSwitch, uint currentMove)
        {
            InitializeComponent();
            ApplyToControls(currentSwitch, currentMove);

            ChkSwitchCtrl.Checked += (_, _) => UpdatePreview();
            ChkSwitchCtrl.Unchecked += (_, _) => UpdatePreview();
            ChkSwitchAlt.Checked += (_, _) => UpdatePreview();
            ChkSwitchAlt.Unchecked += (_, _) => UpdatePreview();
            ChkSwitchShift.Checked += (_, _) => UpdatePreview();
            ChkSwitchShift.Unchecked += (_, _) => UpdatePreview();
            ChkSwitchWin.Checked += (_, _) => UpdatePreview();
            ChkSwitchWin.Unchecked += (_, _) => UpdatePreview();

            ChkMoveCtrl.Checked += (_, _) => UpdatePreview();
            ChkMoveCtrl.Unchecked += (_, _) => UpdatePreview();
            ChkMoveAlt.Checked += (_, _) => UpdatePreview();
            ChkMoveAlt.Unchecked += (_, _) => UpdatePreview();
            ChkMoveShift.Checked += (_, _) => UpdatePreview();
            ChkMoveShift.Unchecked += (_, _) => UpdatePreview();
            ChkMoveWin.Checked += (_, _) => UpdatePreview();
            ChkMoveWin.Unchecked += (_, _) => UpdatePreview();

            UpdatePreview();
        }

        private void ApplyToControls(uint switchMod, uint moveMod)
        {
            ChkSwitchAlt.IsChecked = (switchMod & MainWindow.MOD_ALT) != 0;
            ChkSwitchCtrl.IsChecked = (switchMod & MainWindow.MOD_CONTROL) != 0;
            ChkSwitchShift.IsChecked = (switchMod & MainWindow.MOD_SHIFT) != 0;
            ChkSwitchWin.IsChecked = (switchMod & MainWindow.MOD_WIN) != 0;

            ChkMoveAlt.IsChecked = (moveMod & MainWindow.MOD_ALT) != 0;
            ChkMoveCtrl.IsChecked = (moveMod & MainWindow.MOD_CONTROL) != 0;
            ChkMoveShift.IsChecked = (moveMod & MainWindow.MOD_SHIFT) != 0;
            ChkMoveWin.IsChecked = (moveMod & MainWindow.MOD_WIN) != 0;
        }

        private uint GetSwitchModifiers()
        {
            uint mod = 0;
            if (ChkSwitchAlt.IsChecked == true) mod |= MainWindow.MOD_ALT;
            if (ChkSwitchCtrl.IsChecked == true) mod |= MainWindow.MOD_CONTROL;
            if (ChkSwitchShift.IsChecked == true) mod |= MainWindow.MOD_SHIFT;
            if (ChkSwitchWin.IsChecked == true) mod |= MainWindow.MOD_WIN;
            return mod;
        }

        private uint GetMoveModifiers()
        {
            uint mod = 0;
            if (ChkMoveAlt.IsChecked == true) mod |= MainWindow.MOD_ALT;
            if (ChkMoveCtrl.IsChecked == true) mod |= MainWindow.MOD_CONTROL;
            if (ChkMoveShift.IsChecked == true) mod |= MainWindow.MOD_SHIFT;
            if (ChkMoveWin.IsChecked == true) mod |= MainWindow.MOD_WIN;
            return mod;
        }

        private void UpdatePreview()
        {
            TxtSwitchPreview.Text = $"현재: {MainWindow.ModifiersToString(GetSwitchModifiers())} + [1~9]";
            TxtMovePreview.Text = $"현재: {MainWindow.ModifiersToString(GetMoveModifiers())} + [1~9]";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ApplyToControls(MainWindow.MOD_ALT, MainWindow.MOD_CONTROL | MainWindow.MOD_ALT);
            UpdatePreview();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            uint switchMod = GetSwitchModifiers();
            uint moveMod = GetMoveModifiers();

            if (switchMod == 0 || moveMod == 0)
            {
                System.Windows.MessageBox.Show("단축키에는 최소 1개 이상의 조합키(Ctrl, Alt, Shift, Win)가 포함되어야 합니다.", "단축키 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (switchMod == moveMod)
            {
                System.Windows.MessageBox.Show("데스크톱 전환 단축키와 창 이동 단축키의 조합키가 동일할 수 없습니다.", "단축키 중복", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultSwitchModifiers = switchMod;
            ResultMoveModifiers = moveMod;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
