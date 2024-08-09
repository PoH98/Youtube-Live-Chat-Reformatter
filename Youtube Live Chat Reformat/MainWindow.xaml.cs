using CefSharp;
using CefSharp.Wpf;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;


namespace Youtube_Live_Chat_Reformat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private YoutubeService _youtubeService;
        private string Url;
        public string liteDBString;
        private Counter Counter;
        private WebSocket WebSocket;
        public MainWindow()
        {
            CefSettings settings = new CefSettings
            {
                CachePath = Path.GetFullPath("cache"),
            };
            settings.RegisterScheme(new CefCustomScheme
            {
                SchemeName = "https",
                DomainName = "live.youtube.chat",
                SchemeHandlerFactory = new ResourceHandlerService()
            });
            if (!Directory.Exists("Temp"))
            {
                _ = Directory.CreateDirectory("Temp");
            }
            _ = Cef.Initialize(settings);
            InitializeComponent();
        }

        private void StackPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            Border sp = sender as Border;
            DoubleAnimation db = new DoubleAnimation();
            //db.From = 12;
            db.To = 30;
            db.Duration = TimeSpan.FromSeconds(0.2);
            db.AutoReverse = false;
            db.RepeatBehavior = new RepeatBehavior(1);
            sp.BeginAnimation(StackPanel.HeightProperty, db);
        }

        private void StackPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            Border sp = sender as Border;
            DoubleAnimation db = new DoubleAnimation();
            //db.From = 12;
            db.To = 1;
            db.Duration = TimeSpan.FromSeconds(0.2);
            db.AutoReverse = false;
            db.RepeatBehavior = new RepeatBehavior(1);
            sp.BeginAnimation(StackPanel.HeightProperty, db);
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _youtubeService?.Dispose();
            Url = UrlTextBox.Text;
            FilterPanel.Visibility = Visibility.Visible;
            try
            {
                Uri uri = new Uri(Url);
                NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
                _youtubeService = new YoutubeService();
                liteDBString = "Filename=Temp\\" + query.Get("v") + ";Connection=shared; journal=false";
                _youtubeService.CommentReceived += _youtubeService_CommentReceived;
                _youtubeService.InitChromium(Url, browser);
                if (Counter != null)
                {
                    Counter.Reset();
                }
            }
            catch
            {
                _ = MessageBox.Show("Invalid Url!", "Operation failed successfully!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void _youtubeService_CommentReceived(object sender, YoutubeService.CommentEvent e)
        {
            LiteDatabase _liteDatabase = new LiteDatabase(liteDBString);
            ILiteCollection<ChatData> collection = _liteDatabase.GetCollection<ChatData>("chat");
            IEnumerable<ChatData> list = collection.FindAll();
            ChatData last = list.Count() > 0 ? list.Last() : new ChatData();
            if (!(last.User == e.User && last.Comment == e.Comment))
            {
                if(WebSocket != null)
                {
                    var data = Encoding.UTF8.GetBytes(e.Html);
                    WebSocket.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                var insert = new ChatData
                {
                    Comment = e.Comment,
                    User = e.User,
                    SCAmount = e.SuperChat ? e.SuperChatAmount : 0
                };
                _ = collection.Insert(insert);
                if (Counter != null)
                {
                    Counter.AddMessage(insert);
                }
            }
            _liteDatabase.Dispose();
        }

        private void FilterWindow(object sender, RoutedEventArgs e)
        {
            if(Counter != null)
            {
                return;
            }
            Counter counter = new Counter(this);
            Counter = counter;
            counter.Closing += Counter_Closing;
            counter.Show();
        }

        private void Counter_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Counter = null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!HttpListener.IsSupported)
            {
                return;
            }

            // Create a listener.
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:16470/");
            listener.Start();
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    // Note: The GetContext method blocks while waiting for a request.
                    HttpListenerContext context = listener.GetContext();
                    Thread exec = new Thread(async () =>
                    {
                        HttpListenerRequest request = context.Request;
                        if (request.Url.Segments.Length == 1)
                        {
                            // Obtain a response object.
                            HttpListenerResponse response = context.Response;
                            // Construct a response.
                            string responseString = await browser.GetBrowser().MainFrame.GetSourceAsync();
                            responseString = Regex.Replace(responseString, "<script.*?>.*?</script>", "", RegexOptions.IgnoreCase);
                            var bodyIndex = responseString.IndexOf("</body>");
                            var injector = File.ReadAllText("Assets\\inject.js");
                            responseString = responseString.Insert(bodyIndex, @"
<script>
   document.getElementById('item-offset').style.height = 'auto';
   document.getElementById('item-offset').style.minHeight = '100%';
   const el = document.querySelectorAll('#item-offset #items')[0];
   const socket = new WebSocket('ws://localhost:16470/socks');
    socket.onmessage = function(event) {
      const template = document.createElement('template');
      template.innerHTML = event.data;
      const result = template.content.children[0];
      el.appendChild(result);
    };
    setInterval(()=>{
      el.scrollTo(0, el.scrollHeight);
    }, 100);
    "+ injector +@"
</script>");
                            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                            // Get a response stream and write the response to it.
                            response.ContentLength64 = buffer.Length;
                            Stream output = response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);
                            // You must close the output stream.
                            output.Close();
                        }
                        else if (request.Url.Segments.Contains("socks"))
                        {
                            WebSocketContext webSocketContext = null;
                            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                            if(WebSocket != null)
                            {
                                await WebSocket.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                            }
                            WebSocket = webSocketContext.WebSocket;
                        }
                        else
                        {
                            HttpListenerResponse response = context.Response;
                            response.Abort();
                        }
                    });
                    exec.Start();

                }
            });
            t.IsBackground = true;
            t.Start();
        }
        
    }
}
