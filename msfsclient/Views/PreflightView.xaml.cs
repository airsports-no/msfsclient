using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.Preflight;

namespace msfsclient.Views
{
    public sealed partial class PreflightView : ShellPart
    {
        private IReadOnlyList<PreflightCheck> _checks = [];

        public PreflightView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<CheckItem> Checks { get; private set; } = [];

        protected override void Refresh()
        {
            var checks = Vm.Checks;
            if (checks.SequenceEqual(_checks)) return;
            _checks = checks;
            Checks = checks.Select(c => new CheckItem(c)).ToList();
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            switch (((sender as FrameworkElement)?.Tag as CheckItem)?.Check.Action)
            {
                case CheckAction.OpenClockSettings:
                    Vm.Platform.OpenClockSettings();
                    break;
                case CheckAction.ConnectSimulator:
                    Vm.ConnectSimulator();
                    break;
            }
        }
    }
}
