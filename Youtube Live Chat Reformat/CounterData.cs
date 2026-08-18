using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Youtube_Live_Chat_Reformat
{
    public class CounterData : INotifyPropertyChanged
    {
        private string keyword;
        private int count;

        public string Keyword
        {
            get => keyword;
            set
            {
                if (keyword == value) return;
                keyword = value;
                OnPropertyChanged();
            }
        }

        public int Count
        {
            get => count;
            set
            {
                if (count == value) return;
                count = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
