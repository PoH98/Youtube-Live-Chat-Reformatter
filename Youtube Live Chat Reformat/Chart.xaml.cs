using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Youtube_Live_Chat_Reformat
{
    public partial class Chart : Window
    {
        private readonly string type;
        private readonly List<ISeries> list = new List<ISeries>();
        private static readonly SolidColorPaint TextPaint = new SolidColorPaint(new SKColor(30, 30, 30));
        private Axis xAxis;

        public Chart(string type)
        {
            this.type = type;
            InitializeComponent();

            switch (type)
            {
                case "line":
                    pie.Visibility = Visibility.Hidden;
                    break;
                case "pie":
                    line.Visibility = Visibility.Hidden;
                    break;
            }
        }

        public void UpdateChart(List<CounterData> data)
        {
            if (data == null) return;

            // Take a thread-safe snapshot
            var snapshot = data.ToList();

            Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case "line":
                        if (list.Count == 0)
                        {
                            list.Add(new LineSeries<int>
                            {
                                Values = snapshot.Select(x => x.Count).ToArray(),
                                Fill = null
                            });
                        }
                        else
                        {
                            list[0].Values = snapshot.Select(x => x.Count).ToArray();
                        }

                        if (xAxis == null)
                        {
                            xAxis = new Axis();
                            line.XAxes = new List<Axis> { xAxis };
                        }

                        xAxis.Labels = snapshot.Select(x => x.Keyword).ToList();

                        if (line.Series != list)
                        {
                            line.Series = list;
                        }
                        break;

                    case "pie":
                        if (list.Count != snapshot.Count)
                        {
                            list.Clear();
                            foreach (var item in snapshot)
                            {
                                var currentKeyword = item.Keyword;
                                list.Add(new PieSeries<int>
                                {
                                    Values = new[] { item.Count },
                                    Name = currentKeyword,
                                    DataLabelsPosition = PolarLabelsPosition.Outer,
                                    DataLabelsSize = 15,
                                    DataLabelsPaint = TextPaint,
                                    DataLabelsFormatter = point => $"Selection {currentKeyword}: {point.Model}"
                                });
                            }
                        }
                        else
                        {
                            for (int x = 0; x < snapshot.Count; x++)
                            {
                                var item = snapshot[x];
                                var currentKeyword = item.Keyword;
                                var pieSeries = (PieSeries<int>)list[x];

                                pieSeries.Values = new[] { item.Count };
                                pieSeries.Name = currentKeyword;
                                pieSeries.DataLabelsFormatter = point => $"Selection {currentKeyword}: {point.Model}";
                            }
                        }

                        if (pie.Series != list)
                        {
                            pie.Series = list;
                        }
                        break;
                }
            });
        }
    }
}