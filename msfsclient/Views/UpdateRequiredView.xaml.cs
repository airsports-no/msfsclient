using Microsoft.UI.Xaml;

namespace msfsclient.Views
{
    /// <summary>The blocking "update required" screen (minimum version from mobile/config).</summary>
    public sealed partial class UpdateRequiredView : ShellPart
    {
        public UpdateRequiredView()
        {
            this.InitializeComponent();
        }

        private void Store_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.State.UpdateUrl is { } url) Vm.Platform.OpenUri(url);
        }
    }
}
