using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.ViewModels;

namespace msfsclient.Views
{
    /// <summary>
    /// Base of every screen: gives the XAML <see cref="Vm"/> and passes the view model's change notifications on while the
    /// screen is shown, so one-way x:Bind bindings to the screen's own computed properties update too.
    /// </summary>
    public partial class ShellPart : UserControl, INotifyPropertyChanged
    {
        public ShellPart()
        {
            Vm = App.Shell;
            Loaded += (_, _) =>
            {
                Vm.PropertyChanged += OnVmChanged;
                Refresh();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            };
            Unloaded += (_, _) => Vm.PropertyChanged -= OnVmChanged;
        }

        public ShellViewModel Vm { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Recomputes what the screen derives from the state (called before the bindings are refreshed).</summary>
        protected virtual void Refresh()
        {
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            Refresh();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        protected void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
