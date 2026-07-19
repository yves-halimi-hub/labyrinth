using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EFYVLabyMake.App.State
{
    // Minimal INotifyPropertyChanged base: no MVVM framework, matching the
    // hand-rolled style of the rest of the repository.
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
