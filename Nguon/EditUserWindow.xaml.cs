using System.Windows;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class EditUserWindow : Window
    {
        private readonly User _user;
        private readonly UserService _userService;

        public EditUserWindow(User user, UserService userService)
        {
            InitializeComponent();
            _user = user;
            _userService = userService;

            txtUsername.Text = user.Username;
            txtFullName.Text = user.FullName;
            txtEmail.Text = user.Email;
            txtPhone.Text = user.Phone;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var result = _userService.UpdateProfile(
                _user.Id,
                txtFullName.Text,
                txtEmail.Text,
                txtPhone.Text);

            if (result.ok)
            {
                MessageBox.Show("Cập nhật thành công!");
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(result.message, "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}