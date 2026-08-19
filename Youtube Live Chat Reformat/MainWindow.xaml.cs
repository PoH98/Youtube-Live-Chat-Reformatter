using LiteDB;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        public string liteDBString;
        private Counter Counter;
        private WebSocket WebSocket;
        private LiteDatabase database;
        private ILiteCollection<ChatData> collection;
        private readonly SemaphoreSlim _webSocketLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _commentLock = new SemaphoreSlim(1, 1);
        private ResourceHandlerService resourceHandler;
        private string lastUser;
        private string lastComment;

        public MainWindow()
        {
            DataContext = new MainWindowContext();
            InitializeComponent();
        }

        private void StackPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            Border sp = sender as Border;
            DoubleAnimation db = new DoubleAnimation();
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
            db.To = 1;
            db.Duration = TimeSpan.FromSeconds(0.2);
            db.AutoReverse = false;
            db.RepeatBehavior = new RepeatBehavior(1);
            sp.BeginAnimation(StackPanel.HeightProperty, db);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists("Temp"))
                {
                    _ = Directory.CreateDirectory("Temp");
                }
                Uri uri = new Uri(((MainWindowContext)DataContext).Url);
                NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
                liteDBString = "Filename=Temp\\" + query.Get("v") + ";Connection=shared; journal=false";
                ResetDatabaseConnection();
                _youtubeService.LoadChat(((MainWindowContext)DataContext).Url);
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

        private async void _youtubeService_CommentReceived(object sender, YoutubeService.CommentEvent e)
        {
            await _commentLock.WaitAsync();
            try
            {
                if (collection == null)
                {
                    if (string.IsNullOrEmpty(liteDBString)) return;

                    database = new LiteDatabase(liteDBString);
                    collection = database.GetCollection<ChatData>("chat");
                    ChatData last = collection.FindAll().LastOrDefault();
                    lastUser = last?.User;
                    lastComment = last?.Comment;
                }

                if (!(lastUser == e.User && lastComment == e.Comment))
                {
                    if (WebSocket != null && WebSocket.State == WebSocketState.Open)
                    {
                        await _webSocketLock.WaitAsync();

                        try
                        {
                            var data = Encoding.UTF8.GetBytes(e.Html);
                            await WebSocket.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch (WebSocketException)
                        {
                            WebSocket = null;
                        }
                        finally
                        {
                            _webSocketLock.Release();
                        }
                    }
                    if (!string.IsNullOrEmpty(e.Comment))
                    {
                        var insert = new ChatData
                        {
                            Comment = e.Comment,
                            User = e.User,
                            SCAmount = e.SuperChat ? e.SuperChatAmount : 0
                        };
                        _ = collection.Insert(insert);
                        lastUser = e.User;
                        lastComment = e.Comment;
                        if (Counter != null)
                        {
                            Counter.AddMessage(insert);
                        }
                    }
                }
            }
            catch
            {
                // Keep chat ingestion alive if a single message fails.
            }
            finally
            {
                _commentLock.Release();
            }
        }

        private void ResetDatabaseConnection()
        {
            collection = null;
            database?.Dispose();
            database = null;
            lastUser = null;
            lastComment = null;
        }

        private void _youtubeService_YoutubeChatFound(object sender, string e)
        {
            Uri uri = new Uri(e);
            NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
            liteDBString = "Filename=Temp\\" + query.Get("v") + ";Connection=shared; journal=false";
            ResetDatabaseConnection();
            Dispatcher.Invoke(() =>
            {
                ((MainWindowContext)DataContext).Url = "https://studio.youtube.com/live_chat?is_popout=1&v=" + query.Get("v");
            });
        }

        private void FilterWindow(object sender, RoutedEventArgs e)
        {
            if (Counter != null)
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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string cacheFolderPath = Path.GetFullPath("cache");
            var env = await CoreWebView2Environment.CreateAsync(null, cacheFolderPath);
            resourceHandler = new ResourceHandlerService(env);

            await browser.EnsureCoreWebView2Async(env);
            resourceHandler.Register(browser.CoreWebView2);

            if (File.Exists("debug.txt"))
            {
                browser.CoreWebView2.OpenDevToolsWindow();
            }

            if (!HttpListener.IsSupported)
            {
                return;
            }

            _youtubeService = new YoutubeService();
            _youtubeService.InitWebView2(browser);
            _youtubeService.YoutubeChatFound += _youtubeService_YoutubeChatFound;
            _youtubeService.CommentReceived += _youtubeService_CommentReceived;

            // Create HttpListener
            try
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:16470/");
                listener.Start();
                Thread t = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            HttpListenerContext context = listener.GetContext();
                            Thread exec = new Thread(async () =>
                            {
                                HttpListenerRequest request = context.Request;
                                if (request.Url.Segments.Length == 1)
                                {
                                    HttpListenerResponse response = context.Response;

                                    // WebView2 ExecuteScriptAsync must run on the UI thread
                                    string rawJsonHtml = await Dispatcher.InvokeAsync(async () =>
                                    {
                                        return await browser.ExecuteScriptAsync("document.documentElement.outerHTML");
                                    }).Task.Unwrap();

                                    // Deserialize JSON string output from ExecuteScriptAsync
                                    string responseString = System.Text.Json.JsonSerializer.Deserialize<string>(rawJsonHtml);

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
    " + injector + @"
</script>");
                                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                                    response.ContentLength64 = buffer.Length;
                                    response.ContentEncoding = Encoding.UTF8;
                                    response.ContentType = "text/html; charset=utf-8";
                                    Stream output = response.OutputStream;
                                    output.Write(buffer, 0, buffer.Length);
                                    output.Close();
                                }
                                else if (request.Url.Segments.Contains("socks"))
                                {
                                    WebSocketContext webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                                    if (WebSocket != null)
                                    {
                                        try
                                        {
                                            await WebSocket.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                                        }
                                        catch
                                        {

                                        }
                                        finally
                                        {
                                            WebSocket = null;
                                        }
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
                        catch
                        {
                            //ignore
                        }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch (HttpListenerException ex)
            {
                MessageBox.Show($"Failed to start local server on port 16470: {ex.Message}", "Port In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public class MainWindowContext : INotifyPropertyChanged
    {
        private string url;
        public string Url
        {
            get => url;
            set
            {
                url = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Url"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
