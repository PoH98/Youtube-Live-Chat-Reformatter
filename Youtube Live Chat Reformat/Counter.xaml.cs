using LiteDB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Youtube_Live_Chat_Reformat
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Counter : Window
    {
        private readonly MainWindow window;
        private bool pause;
        private readonly ObservableCollection<CounterData> counters = new ObservableCollection<CounterData>();
        private readonly ObservableCollection<ChatData> displayedChats = new ObservableCollection<ChatData>();
        private List<Chart> charts = new List<Chart>();
        private readonly Thread t;
        private readonly List<ChatData> chatDatas = new List<ChatData>();
        private readonly object _chatLock = new object();
        public Counter(MainWindow mainWindow)
        {
            window = mainWindow;
            InitializeComponent();
            grid.ItemsSource = displayedChats;
            counter.ItemsSource = counters;
            LiteDatabase _liteDatabase = new LiteDatabase(window.liteDBString);
            var chat = _liteDatabase.GetCollection<ChatData>("chat");
            chatDatas.AddRange(chat.FindAll());
            t = new Thread(() =>
            {
                do
                {
                    Tick();
                }
                while (true);
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            t.IsBackground = true;
            t.Start();
        }

        internal void AddMessage(ChatData message)
        {
            lock (_chatLock)
            {
                chatDatas.Add(message);
            }
        }

        internal void Reset()
        {
            chatDatas.Clear();
            LiteDatabase _liteDatabase = new LiteDatabase(window.liteDBString);
            var chat = _liteDatabase.GetCollection<ChatData>("chat");
            chatDatas.AddRange(chat.FindAll());
        }

        private void Tick()
        {
            if (pause)
            {
                Thread.Sleep(200);
                return;
            }
            List<CounterData> updatedCounters = new List<CounterData>();
            List<ChatData> snapshot;
            lock (_chatLock)
            {
                snapshot = chatDatas.ToList();
            }
            IQueryable<ChatData> list = snapshot.AsQueryable().Where(x => x.Comment != null && x.User != null);
            List<string> filters = new List<string>();
            Dispatcher.Invoke(() =>
            {
                filters = filter.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            });
            List<int> numFilters = new List<int>();
            List<string> strFilters = new List<string>();
            if (filters.Count > 0)
            {
                foreach (string filter in filters)
                {
                    if (filter.Contains("-"))
                    {
                        string[] predict = filter.Split('-');
                        if (predict.Length > 1)
                        {
                            if (!int.TryParse(predict[0], out int min))
                            {
                                goto Label;
                            }
                            if (!int.TryParse(predict[1], out int max))
                            {
                                goto Label;
                            }
                            var cache = max;
                            max = Math.Max(cache, min);
                            min = Math.Min(cache, min);
                            numFilters.AddRange(Enumerable.Range(min, max - min + 1));
                            continue;
                        }
                    }
                Label:
                    if (int.TryParse(filter, out int num))
                    {
                        numFilters.Add(num);
                    }
                    else
                    {
                        strFilters.Add(filter);
                    }
                }
                var result = list;
                Dispatcher.Invoke(() =>
                {
                    if (showOnce.IsChecked ?? false)
                    {
                        result = result.GroupBy(x => x.User).Select(x => x.First());
                    }
                });
                result = result.Where((x) => QueryFilter(x, strFilters, numFilters));
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        List<ChatData> resultList = result.ToList();
                        UpdateDisplayedChats(resultList);
                        Count.Content = resultList.Count;
                        foreach (string filter in strFilters)
                        {
                            updatedCounters.Add(new CounterData
                            {
                                Count = result.Count(x => x.Comment.StartsWith(filter)),
                                Keyword = filter,
                            });
                        }
                        foreach (int filter in numFilters)
                        {
                            updatedCounters.Add(new CounterData
                            {
                                Count = result.Count(x => x.Comment == filter.ToString()),
                                Keyword = filter.ToString(),
                            });
                        }
                        UpdateCounters(updatedCounters);
                    }
                    catch
                    {

                    }
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var result = list;
                        if (showOnce.IsChecked ?? false)
                        {
                            result = result.GroupBy(x => x.User).Select(x => x.First());
                        }
                        List<ChatData> resultList = result.ToList();
                        UpdateDisplayedChats(resultList);
                        Count.Content = resultList.Count;
                        UpdateCounters(updatedCounters);
                    }
                    catch
                    {

                    }
                });
            }
            Dispatcher.Invoke(() =>
            {
                try
                {
                    SCAmount.Content = list.Where(x => x.SCAmount > 0).Count();
                }
                catch
                {

                }
            });

            List<Chart> chartsSnapshot;
            lock (charts)
            {
                chartsSnapshot = charts.ToList();
            }
            foreach (var chart in chartsSnapshot)
            {
                chart.UpdateChart(counters.ToList());
            }
            Thread.Sleep(1000);
        }

        private void UpdateCounters(IList<CounterData> updatedCounters)
        {
            for (int i = 0; i < updatedCounters.Count; i++)
            {
                CounterData updated = updatedCounters[i];
                if (i >= counters.Count)
                {
                    counters.Add(updated);
                    continue;
                }

                CounterData current = counters[i];
                current.Keyword = updated.Keyword;
                current.Count = updated.Count;
            }

            while (counters.Count > updatedCounters.Count)
            {
                counters.RemoveAt(counters.Count - 1);
            }
        }

        private void UpdateDisplayedChats(IList<ChatData> updatedChats)
        {
            for (int i = 0; i < updatedChats.Count; i++)
            {
                if (i >= displayedChats.Count)
                {
                    displayedChats.Add(updatedChats[i]);
                }
                else if (!ReferenceEquals(displayedChats[i], updatedChats[i]))
                {
                    displayedChats[i] = updatedChats[i];
                }
            }

            while (displayedChats.Count > updatedChats.Count)
            {
                displayedChats.RemoveAt(displayedChats.Count - 1);
            }
        }

        private bool QueryFilter(ChatData x, IEnumerable<string> strFilters, IEnumerable<int> numFilters)
        {
            bool match = false;
            if (int.TryParse(x.Comment, out _) && numFilters.Count() > 0)
            {
                match = numFilters.Any(y => x.Comment == y.ToString());
            }
            if (!match && strFilters.Count() > 0)
            {
                match = strFilters.Any(y => ContainsCaseInsensitive(x.Comment, y));
            }
            return match;
        }

        public bool ContainsCaseInsensitive(string source, string substring)
        {
            return source?.IndexOf(substring, StringComparison.OrdinalIgnoreCase) > -1;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            LiteDatabase _liteDatabase = new LiteDatabase(window.liteDBString);
            ILiteCollection<ChatData> chat = _liteDatabase.GetCollection<ChatData>("chat");
            _ = chat.DeleteAll();
            _liteDatabase.Dispose();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            pause = !pause;
            if (pause)
            {
                pauseBtn.Content = "Start";
            }
            else
            {
                //clean history
                LiteDatabase _liteDatabase = new LiteDatabase(window.liteDBString);
                ILiteCollection<ChatData> chat = _liteDatabase.GetCollection<ChatData>("chat");
                _ = chat.DeleteAll();
                _liteDatabase.Dispose();
                pauseBtn.Content = "Stop";
            }
        }

        private void Pie_Chart_Click(object sender, RoutedEventArgs e)
        {
            Chart chart = new Chart("pie");
            chart.Show();
            chart.Closing += Chart_Closing;
            charts.Add(chart);
        }

        private void Chart_Closing(object sender, CancelEventArgs e)
        {
            charts.Remove(sender as Chart);
        }

        private void Line_Chart_Click(object sender, RoutedEventArgs e)
        {
            Chart chart = new Chart("line");
            chart.Show();
            chart.Closing += Chart_Closing;
            charts.Add(chart);
        }
    }
}
