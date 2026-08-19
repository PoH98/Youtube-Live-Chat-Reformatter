using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Web;

namespace Youtube_Live_Chat_Reformat
{
    internal class YoutubeService : IDisposable
    {
        private WebView2 browser;
        private string webPath;

        internal event EventHandler<CommentEvent> CommentReceived;
        internal event EventHandler<string> YoutubeChatFound;

        internal YoutubeService() { }

        internal void InitWebView2(WebView2 browser)
        {
            this.browser = browser;

            // Ensure CoreWebView2 is initialized before binding events
            if (browser.CoreWebView2 != null)
            {
                AttachEvents();
            }

            browser.Source = new Uri("https://accounts.google.com/v3/signin/identifier?continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue%26app%3Ddesktop%26hl%3Dzh-CN%26next%3D%252F&hl=zh-CN&service=youtube&flowName=GlifWebSignIn&flowEntry=ServiceLogin&ddm=1");
            Console.WriteLine("Inited Youtube Services");
        }

        private void AttachEvents()
        {
            browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            browser.CoreWebView2.FrameNavigationStarting += CoreWebView2_FrameNavigationStarting;
            browser.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        }

        internal void LoadChat(string youtubeUrl)
        {
            Uri uri = new Uri(youtubeUrl);
            NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
            var id = query.Get("v");
            if (string.IsNullOrEmpty(id))
            {
                id = uri.Segments.Last();
            }

            string targetUrl = "https://studio.youtube.com/live_chat?is_popout=1&v=" + id;
            if (browser.CoreWebView2 != null)
            {
                browser.CoreWebView2.Navigate(targetUrl);
            }
            else
            {
                browser.Source = new Uri(targetUrl);
            }

            Console.WriteLine("Current stream YT url is " + youtubeUrl);
        }

        private void CoreWebView2_FrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string frameUrl = e.Uri;

            try
            {
                CoreWebView2 coreWebView2 = (CoreWebView2)sender;
                if (webPath == null && frameUrl.StartsWith("https://www.youtube.com/live_chat?continuation"))
                {
                    webPath = frameUrl;

                    YoutubeChatFound?.Invoke(this, coreWebView2.Source);

                    browser.CoreWebView2.Navigate(webPath);
                }
            }
            catch
            {
                // Ignored
            }
        }

        private async void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;

            string currentUrl = browser.CoreWebView2.Source;

            try
            {
                if (currentUrl == webPath || currentUrl.StartsWith("https://studio.youtube.com/live_chat") || currentUrl.StartsWith("https://www.youtube.com/live_chat"))
                {
                    // Clean up unwanted DOM elements
                    await browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('reaction-control-panel-overlay')?.remove();");
                    await browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('chat').style.background = '#00FF00';");
                    await browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.getElementsByTagName('yt-live-chat-viewer-engagement-message-renderer')).forEach(x => x.remove());");
                    await browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.getElementsByTagName('yt-live-chat-header-renderer')).forEach(x => x.remove());");
                    await browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.getElementsByTagName('yt-live-chat-message-input-renderer')).forEach(x => x.remove());");

                    // Inject chat listening scripts using WebView2's window.chrome.webview.postMessage
                    await browser.CoreWebView2.ExecuteScriptAsync(@"
                        let last = '';
                        let txtLogging = null;
                        let scLogging = null;
                        let memLogging = null;
                        let sponsorLogging = null;

                        function getMessageText(element) {
                            return Array.from(element.childNodes)
                                .map(node => {
                                    if (node.nodeType === Node.TEXT_NODE) {
                                        return node.textContent;
                                    }

                                    if (node instanceof HTMLImageElement) {
                                        return node.alt || '';
                                    }

                                    return node.textContent || '';
                                })
                                .join('')
                                .trim();
                        }

                        const seenTextMessages = new Set();

                        function postTextMessage(item) {
                            if (item.closest('yt-live-chat-banner-renderer') ||
                                item.classList.contains('yt-live-chat-banner-renderer')) return;

                            const cid = item.id;
                            if (!cid || seenTextMessages.has(cid)) return;

                            const author = item.querySelector('#author-name');
                            const message = item.querySelector('#message');
                            if (!author || !message) return;

                            seenTextMessages.add(cid);
                            window.chrome.webview.postMessage({
                                type: 'text',
                                cid: cid,
                                name: author.textContent.trim(),
                                text: getMessageText(message),
                                html: item.outerHTML
                            });
                        }

                        function attachTextMessageObserver() {
                            const items = document.querySelector('yt-live-chat-item-list-renderer #items');
                            if (!items) return false;

                            document.querySelectorAll('yt-live-chat-text-message-renderer')
                                .forEach(item => {
                                    if (item.id &&
                                        !item.closest('yt-live-chat-banner-renderer') &&
                                        !item.classList.contains('yt-live-chat-banner-renderer')) {
                                        seenTextMessages.add(item.id);
                                    }
                                });

                            const observer = new MutationObserver(mutations => {
                                mutations.forEach(mutation => {
                                    mutation.addedNodes.forEach(node => {
                                        if (!(node instanceof Element)) return;

                                        const item = node.matches('yt-live-chat-text-message-renderer')
                                            ? node
                                            : node.closest('yt-live-chat-text-message-renderer');
                                        if (item) postTextMessage(item);

                                        node.querySelectorAll('yt-live-chat-text-message-renderer')
                                            .forEach(postTextMessage);
                                    });
                                });
                            });

                            observer.observe(items, { childList: true, subtree: true });
                            return true;
                        }

                        txtLogging = setInterval(function () {
                            if (attachTextMessageObserver()) {
                                clearInterval(txtLogging);
                                txtLogging = null;
                            }
                        }, 100);

                        if(!scLogging) {
                            scLogging = setInterval(function () {
                                (function (t) {
                                    if(t.length <= 0) return;
                                    if (last != (cid = t[t.length - 1].id)) {
                                        for (var e = t.length; e--;) {
                                            if (last == t[e].id) return last = cid;
                                            let userName = t[e].querySelectorAll('#author-name')[0].textContent;
                                            let amount = parseFloat(t[e].querySelectorAll('#purchase-amount-column')[0].textContent.replace(/[^0-9\.]+/g,''));
                                            if(amount) {
                                                window.chrome.webview.postMessage({
                                                    type: 'superchat',
                                                    cid: cid,
                                                    name: userName,
                                                    text: t[e].children[0].children[1].children[0].textContent,
                                                    amount: amount,
                                                    html: t[e].outerHTML
                                                });
                                            }
                                            last = cid;
                                            return;
                                        }
                                    }
                                })(document.getElementsByTagName('yt-live-chat-paid-message-renderer'));
                            }, 100);
                        }

                        if(!memLogging) {
                            memLogging = setInterval(function () {
                                (function (t) {
                                    if(t.length <= 0) return;
                                    if (last != (cid = t[t.length - 1].id)) {
                                        for (var e = t.length; e--;) {
                                            if (last == t[e].id) return last = cid;
                                            window.chrome.webview.postMessage({
                                                type: 'text',
                                                cid: cid,
                                                name: t[e].children[0].children[0].children[1].textContent,
                                                text: t[e].children[0].children[1].textContent,
                                                html: t[e].outerHTML
                                            });
                                            last = cid;
                                            return;
                                        }
                                    }
                                })(document.getElementsByTagName('yt-live-chat-membership-item-renderer'));
                            }, 100);
                        }

                        if(!sponsorLogging) {
                            sponsorLogging = setInterval(function () {
                                (function (t) {
                                    if(t.length <= 0) return;
                                    if (last != (cid = t[t.length - 1].id)) {
                                        for (var e = t.length; e--;) {
                                            if (last == t[e].id) return last = cid;
                                            window.chrome.webview.postMessage({
                                                type: 'text',
                                                cid: cid,
                                                name: t[e].children[0].children[0].children[0].children[3].children[0].children[0].children[0].textContent,
                                                text: t[e].children[0].children[0].children[0].children[3].children[0].children[0].children[2].textContent,
                                                html: t[e].outerHTML
                                            });
                                            last = cid;
                                            return;
                                        }
                                    }
                                })(document.getElementsByTagName('ytd-sponsorships-live-chat-gift-purchase-announcement-renderer'));
                            }, 100);
                        }
                    ");

                    if (File.Exists("Assets\\style.css"))
                    {
                        string cssContent = File.ReadAllText("Assets\\style.css").Replace("`", "\\`");
                        await browser.CoreWebView2.ExecuteScriptAsync($@"
                            let escapeHTMLPolicy = trustedTypes.createPolicy('forceInner', {{
                                createHTML: (to_escape) => to_escape
                            }});
                            const style = document.createElement('style');
                            style.id = 'custom-obs';
                            style.innerHTML = escapeHTMLPolicy.createHTML(`{cssContent}`);
                            document.head.appendChild(style);
                        ");
                    }
                }
            }
            catch
            {
                // Ignored
            }
        }

        private string lastId;

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = json.RootElement;

                string type = root.GetProperty("type").GetString();
                string cid = root.GetProperty("cid").GetString();

                if (lastId == cid) return;
                lastId = cid;

                string name = root.GetProperty("name").GetString();
                string text = root.GetProperty("text").GetString();
                string html = root.GetProperty("html").GetString();

                if (type == "text")
                {
                    CommentReceived?.Invoke(this, new CommentEvent
                    {
                        Comment = text,
                        User = name,
                        Html = html
                    });
                }
                else if (type == "superchat")
                {
                    double amount = root.GetProperty("amount").GetDouble();
                    CommentReceived?.Invoke(this, new CommentEvent
                    {
                        Comment = text,
                        User = name,
                        SuperChat = true,
                        SuperChatAmount = amount,
                        Html = html
                    });
                }
            }
            catch
            {
                // Ignored
            }
        }

        public void Dispose()
        {
            if (browser?.CoreWebView2 != null)
            {
                browser.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                browser.CoreWebView2.FrameNavigationStarting -= CoreWebView2_FrameNavigationStarting;
                browser.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
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
