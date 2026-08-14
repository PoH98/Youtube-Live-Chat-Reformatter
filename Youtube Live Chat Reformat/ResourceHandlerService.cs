using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Linq;

namespace Youtube_Live_Chat_Reformat
{
    internal class ResourceHandlerService
    {
        private readonly CoreWebView2Environment _environment;

        public ResourceHandlerService(CoreWebView2Environment environment)
        {
            _environment = environment;
        }

        /// <summary>
        /// Registers the request filter and attaches the resource requested event handler.
        /// </summary>
        public void Register(CoreWebView2 coreWebView2)
        {
            // Intercept requests directed to your custom domain
            coreWebView2.AddWebResourceRequestedFilter("https://live.youtube.chat/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.Document);
            coreWebView2.WebResourceRequested += OnWebResourceRequested;
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string relativePath = string.Join("\\", new Uri(e.Request.Uri).Segments.Where(x => x != "/"));
            string fullPath = Path.GetFullPath(Path.Combine("Assets", relativePath));

            try
            {
                if (File.Exists(fullPath))
                {
                    FileStream stream = File.OpenRead(fullPath);
                    string mimeType = GetMimeType(fullPath);
                    
                    e.Response = _environment.CreateWebResourceResponse(
                        stream, 
                        200, 
                        "OK", 
                        $"Content-Type: {mimeType}");
                    return;
                }
            }
            catch
            {
                // Fallback on exception
            }

            // Return default/empty response when file is missing or throws an error
            MemoryStream emptyStream = new MemoryStream(new byte[] { 0, 0 });
            e.Response = _environment.CreateWebResourceResponse(
                emptyStream, 
                404, 
                "Not Found", 
                "Content-Type: application/octet-stream");
        }

        private static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }
    }
}