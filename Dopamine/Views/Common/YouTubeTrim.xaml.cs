using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Dopamine.Views.Common
{
    public partial class YouTubeTrim : UserControl
    {
        public YouTubeTrim()
        {
            InitializeComponent();
        }

        private void TimeTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            // Commit the typed time instead of letting Enter trigger the dialog's default (Save) button
            TextBox textBox = (TextBox)sender;
            BindingExpression binding = textBox.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();
            textBox.SelectAll();
            e.Handled = true;
        }
    }
}
