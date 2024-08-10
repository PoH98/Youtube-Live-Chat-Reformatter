using CefSharp;
using CefSharp.Wpf;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Web;

namespace Youtube_Live_Chat_Reformat
{
    internal class YoutubeService : IDisposable
    {
        private ChromiumWebBrowser browser;
        private string webPath;
        public event EventHandler<CommentEvent> CommentReceived;
        public YoutubeService() { }

        public void InitChromium(string youtubeUrl, ChromiumWebBrowser browser)
        {
            this.browser = browser;
            Uri uri = new Uri(youtubeUrl);
            NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
            browser.Load("https://www.youtube.com/live_chat?is_popout=1&v=" + query.Get("v"));
            if (File.Exists("debug.txt"))
            {
                browser.ShowDevTools();
            }
            browser.JavascriptObjectRepository.Register("bound", new CefObject(this), true);
            browser.FrameLoadEnd += Browser_FrameLoadEnd;
            Console.WriteLine("Current stream YT url is " + youtubeUrl);
            Console.WriteLine("Inited Youtube Services");
        }

        private void Browser_FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (webPath == null && e.Frame.Url.Split('=')[0] == "https://www.youtube.com/live_chat?continuation")
            {
                browser.Load(webPath = e.Frame.Url);
            }
            else if (e.Frame.Url == webPath || e.Frame.Url.StartsWith("https://studio.youtube.com/live_chat") || e.Frame.Url.StartsWith("https://www.youtube.com/live_chat"))
            {
                e.Frame.ExecuteJavaScriptAsync("document.getElementById(\"reaction-control-panel-overlay\").remove();");
                e.Frame.ExecuteJavaScriptAsync("document.getElementById(\"chat\").style.background = \"#00FF00\";");
                e.Frame.ExecuteJavaScriptAsync("Array.prototype.slice.call(document.getElementsByTagName(\"yt-live-chat-viewer-engagement-message-renderer\")).forEach((x) => x.remove())");
                e.Frame.ExecuteJavaScriptAsync("Array.prototype.slice.call(document.getElementsByTagName(\"yt-live-chat-header-renderer\")).forEach((x) => x.remove())");
                e.Frame.ExecuteJavaScriptAsync("Array.prototype.slice.call(document.getElementsByTagName(\"yt-live-chat-message-input-renderer\")).forEach((x) => x.remove())");

                e.Frame.ExecuteJavaScriptAsync(@"(async function () {
                    await CefSharp.BindObjectAsync('boundAsync', 'bound');
                    var last = """";
                    setInterval(function () {
                        (function (t) {
                            if(t.length <= 0){
                               return;
                            }
                            if (last != (cid = t[t.length - 1].id))
                                for (var e = t.length; e--;) {
                                    if (last == t[e].id) return last = cid;
                                    bound.onText(t[e].children[1].children[1].children[1].textContent, t[e].children[1].children[3].textContent, t[e].outerHTML);
                                    last = cid;
                                    return;
                                }
                        })(document.getElementsByTagName(""yt-live-chat-text-message-renderer""))
                    }, 25);
                })()");

                e.Frame.ExecuteJavaScriptAsync(@"(async function () {
                    await CefSharp.BindObjectAsync('boundAsync', 'bound');
                    var last = """";
                    setInterval(function () {
                        (function (t) {
                            if(t.length <= 0){
                               return;
                            }
                            if (last != (cid = t[t.length - 1].id))
                                for (var e = t.length; e--;) {
                                    if (last == t[e].id) return last = cid;
                                    let userName = t[e].querySelectorAll(""#author-name"")[0].textContent;
                                    let amount = parseFloat(t[e].querySelectorAll(""#purchase-amount-column"")[0].textContent.replace(/[^0-9\.]+/g,""""))
                                    amount && bound.onSuperChat(userName, t[e].children[0].children[1].children[0].textContent, amount, t[e].outerHTML);
                                    last = cid;
                                    return;
                                }
                        })(document.getElementsByTagName(""yt-live-chat-paid-message-renderer""))
                    }, 100);
                })()");

                e.Frame.ExecuteJavaScriptAsync(@"(async function () {
                    await CefSharp.BindObjectAsync('boundAsync', 'bound');
                    var last = """";
                    setInterval(function () {
                        (function (t) {
                            if(t.length <= 0){
                               return;
                            }
                            if (last != (cid = t[t.length - 1].id))
                                for (var e = t.length; e--;) {
                                    if (last == t[e].id) return last = cid;
                                    bound.onText(t[e].children[0].children[0].children[1].textContent, t[e].children[0].children[1].textContent, t[e].outerHTML);
                                    last = cid;
                                    return;
                                }
                        })(document.getElementsByTagName(""yt-live-chat-membership-item-renderer""))
                    }, 100);
                })()");

                e.Frame.ExecuteJavaScriptAsync(@"(async function () {
                    await CefSharp.BindObjectAsync('boundAsync', 'bound');
                    var last = """";
                    setInterval(function () {
                        (function (t) {
                            if(t.length <= 0){
                               return;
                            }
                            if (last != (cid = t[t.length - 1].id))
                                for (var e = t.length; e--;) {
                                    if (last == t[e].id) return last = cid;
                                    bound.onText(t[e].children[0].children[0].children[0].children[3].children[0].children[0].children[0].textContent, t[e].children[0].children[0].children[0].children[3].children[0].children[0].children[2].textContent, t[e].outerHTML);
                                    last = cid;
                                    return;
                                }
                        })(document.getElementsByTagName(""ytd-sponsorships-live-chat-gift-purchase-announcement-renderer""))
                    }, 100);
                })()");

                if (File.Exists("Assets\\style.css"))
                {
                    e.Frame.ExecuteJavaScriptAsync(@"
                        let escapeHTMLPolicy = trustedTypes.createPolicy(""forceInner"", {
                            createHTML: (to_escape) => to_escape
                        })
                        const style = document.createElement('style');
                        style.id = 'custom-obs';
                        style.innerHTML = escapeHTMLPolicy.createHTML(`" + File.ReadAllText("Assets\\style.css") + @"`);
                        document.head.appendChild(style);");
                }
            }
        }

        public void Dispose()
        {
            browser.JavascriptObjectRepository.UnRegisterAll();
            browser.FrameLoadEnd -= Browser_FrameLoadEnd;
        }

        private class CefObject
        {
            private readonly YoutubeService service;
            public CefObject(YoutubeService service)
            {
                this.service = service;
            }

            public void onText(string name, string text, string html)
            {
                service.CommentReceived?.Invoke(this, new CommentEvent
                {
                    Comment = text,
                    User = name,
                    Html = html
                });
            }

            public void onSuperChat(string name, string text, double scAmount, string html)
            {
                service.CommentReceived?.Invoke(this, new CommentEvent
                {
                    Comment = text,
                    User = name,
                    SuperChat = true,
                    SuperChatAmount = scAmount,
                    Html = html
                });
            }
        }

        public class CommentEvent : EventArgs
        {
            public string Comment { get; set; }
            public string User { get; set; }
            public bool SuperChat { get; set; }
            public double SuperChatAmount { get; set; }
            public string Html { get; set; }
        }
    }
}
