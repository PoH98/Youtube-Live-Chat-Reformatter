using LiteDB;
using LiveChartsCore;
using LiveChartsCore.ConditionalDraw;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using LiveChartsCore.SkiaSharpView.WPF;
using LiveChartsCore.Themes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Web.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Youtube_Live_Chat_Reformat
{
    /// <summary>
    /// Interaction logic for Chart.xaml
    /// </summary>
    public partial class Chart : Window
    {
        private readonly string type;
        private readonly List<ISeries> list = new List<ISeries>();
        public Chart(string type)
        {
            this.type = type;
            InitializeComponent();
            switch (type)
            {
                case "line":
                    pie.Visibility = Visibility.Hidden; break;
                case "pie":
                    line.Visibility = Visibility.Hidden; break;
            }
        }

        public void UpdateChart(List<CounterData> data)
        {
            Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case "line":
                        if(list.Count == 0)
                        {
                            list.Add(new LineSeries<int>
                            {
                                Values = data.Select(x => x.Count).ToArray(),
                                Fill = null
                            });
                        }
                        else
                        {
                            list[0].Values = data.Select(x => x.Count);
                        }
                        var xaxis = new List<Axis>
                        {
                            new Axis
                            {
                                Labels = data.Select(x => x.Keyword).ToList()
                            }
                        };
                        line.XAxes = xaxis;
                        line.Series = list;
                        break;
                    case "pie":
                        if (list.Count != data.Count)
                        {
                            list.Clear();
                            foreach(var item in data)
                            {
                                list.Add(new PieSeries<int>
                                {
                                    Values = new int[] { item.Count },
                                    Name = item.Keyword,
                                    DataLabelsPosition = PolarLabelsPosition.Outer,
                                    DataLabelsSize = 15,
                                    DataLabelsPaint = new SolidColorPaint(new SKColor(30, 30, 30)),
                                    DataLabelsFormatter = ((point) => $"Selection {item.Keyword}: {point.Model}")
                                });
                            }
                        }
                        else
                        {
                            for(int x = 0; x < data.Count; x++)
                            {
                                list[x].Values = new int[] { data[x].Count };
                            }
                        }
                        pie.Series = list;
                        break;
                }
            });
        }
    }
}
