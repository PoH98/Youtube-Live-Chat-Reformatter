using LiteDB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace Youtube_Live_Chat_Reformat
{
    public partial class Counter : Window
    {
        private const int MaxRenderedMessages = 1000;
        private readonly MainWindow window;
        private readonly List<Chart> charts = new List<Chart>();
        private readonly List<ChatData> chatDatas = new List<ChatData>();
        private readonly object chatLock = new object();
        private readonly Thread worker;
        private volatile bool pause;
        private volatile bool stopping;
        private volatile bool tableReady;
        private int renderPending;
        private List<CounterData> latestCounters = new List<CounterData>();

        public Counter(MainWindow mainWindow)
        {
            window = mainWindow;
            InitializeComponent();
            using (LiteDatabase database = new LiteDatabase(window.liteDBString))
            {
                chatDatas.AddRange(database.GetCollection<ChatData>("chat").FindAll());
            }
            worker = new Thread(WorkerLoop) { IsBackground = true };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await tableView.EnsureCoreWebView2Async();
            tableView.CoreWebView2.NavigationCompleted += (navigationSender, args) => tableReady = args.IsSuccess;
            tableView.NavigateToString(File.ReadAllText("Assets\\counter.html"));
            worker.Start();
        }

        private void Window_Closing(object sender, CancelEventArgs e) => stopping = true;

        internal void AddMessage(ChatData message)
        {
            lock (chatLock) chatDatas.Add(message);
        }

        internal void Reset()
        {
            List<ChatData> loaded;
            using (LiteDatabase database = new LiteDatabase(window.liteDBString))
            {
                loaded = database.GetCollection<ChatData>("chat").FindAll().ToList();
            }
            lock (chatLock)
            {
                chatDatas.Clear();
                chatDatas.AddRange(loaded);
            }
        }

        private void WorkerLoop()
        {
            while (!stopping)
            {
                if (!pause) Tick();
                Thread.Sleep(pause ? 200 : 1000);
            }
        }

        private void Tick()
        {
            List<ChatData> snapshot;
            lock (chatLock) snapshot = chatDatas.ToList();

            string filterText = string.Empty;
            bool onlyOnce = false;
            Dispatcher.Invoke(() =>
            {
                filterText = filter.Text;
                onlyOnce = showOnce.IsChecked ?? false;
            });

            List<string> filters = filterText.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            List<int> numberFilters = new List<int>();
            List<string> stringFilters = new List<string>();
            ParseFilters(filters, stringFilters, numberFilters);

            IEnumerable<ChatData> result = snapshot.Where(x => x.Comment != null && x.User != null);
            if (onlyOnce) result = result.GroupBy(x => x.User).Select(x => x.First());
            if (filters.Count > 0) result = result.Where(x => QueryFilter(x, stringFilters, numberFilters));

            List<ChatData> resultList = result.ToList();
            List<CounterData> counters = BuildCounters(resultList, stringFilters, numberFilters);
            latestCounters = counters;
            var payload = new
            {
                messages = resultList.Skip(Math.Max(0, resultList.Count - MaxRenderedMessages))
                    .Select(x => new { user = x.User, comment = x.Comment }),
                counters = counters.Select(x => new { keyword = x.Keyword, count = x.Count })
            };
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            int superChatCount = snapshot.Count(x => x.SCAmount > 0);

            if (Interlocked.Exchange(ref renderPending, 1) == 0)
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        Count.Content = resultList.Count;
                        SCAmount.Content = superChatCount;
                        if (tableReady)
                            await tableView.ExecuteScriptAsync("window.updateTables(" + json + ");");
                    }
                    catch
                    {
                        // A navigation or close can invalidate an in-flight browser update.
                    }
                    finally
                    {
                        Interlocked.Exchange(ref renderPending, 0);
                    }
                }));
            }

            List<Chart> chartSnapshot;
            lock (charts) chartSnapshot = charts.ToList();
            foreach (Chart chart in chartSnapshot) chart.UpdateChart(counters);
        }

        private static void ParseFilters(IEnumerable<string> filters, ICollection<string> strings, ICollection<int> numbers)
        {
            foreach (string value in filters)
            {
                string[] range = value.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out int first) && int.TryParse(range[1], out int second))
                {
                    int min = Math.Min(first, second);
                    int max = Math.Max(first, second);
                    foreach (int number in Enumerable.Range(min, max - min + 1)) numbers.Add(number);
                }
                else if (int.TryParse(value, out int number)) numbers.Add(number);
                else strings.Add(value);
            }
        }

        private static List<CounterData> BuildCounters(IEnumerable<ChatData> messages, IEnumerable<string> strings, IEnumerable<int> numbers)
        {
            List<ChatData> snapshot = messages.ToList();
            List<CounterData> result = strings.Select(value => new CounterData
            {
                Keyword = value,
                Count = snapshot.Count(x => x.Comment.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            }).ToList();
            result.AddRange(numbers.Select(value => new CounterData
            {
                Keyword = value.ToString(),
                Count = snapshot.Count(x => x.Comment == value.ToString())
            }));
            return result;
        }

        private static bool QueryFilter(ChatData item, ICollection<string> strings, ICollection<int> numbers)
        {
            if (numbers.Count > 0 && int.TryParse(item.Comment, out int number) && numbers.Contains(number)) return true;
            return strings.Any(value => item.Comment.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            using (LiteDatabase database = new LiteDatabase(window.liteDBString))
                database.GetCollection<ChatData>("chat").DeleteAll();
            lock (chatLock) chatDatas.Clear();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            pause = !pause;
            pauseBtn.Content = pause ? "Start" : "Stop";
            if (!pause) Button_Click(sender, e);
        }

        private void Pie_Chart_Click(object sender, RoutedEventArgs e) => OpenChart("pie");
        private void Line_Chart_Click(object sender, RoutedEventArgs e) => OpenChart("line");

        private void OpenChart(string type)
        {
            Chart chart = new Chart(type);
            chart.Show();
            chart.UpdateChart(latestCounters);
            chart.Closing += Chart_Closing;
            lock (charts) charts.Add(chart);
        }

        private void Chart_Closing(object sender, CancelEventArgs e)
        {
            lock (charts) charts.Remove(sender as Chart);
        }
    }
}
