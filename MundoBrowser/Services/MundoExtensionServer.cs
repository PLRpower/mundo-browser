using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services
{
    public class MundoExtensionServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly IWebViewService _webViewService;
        private readonly Thread _serverThread;
        private bool _isRunning;

        public MundoExtensionServer(IWebViewService webViewService)
        {
            _webViewService = webViewService;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:50000/");
            
            _isRunning = true;
            _serverThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = "MundoExtensionServer"
            };
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _serverThread.Start();
                System.Diagnostics.Debug.WriteLine("MundoExtensionServer started on http://127.0.0.1:50000/");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start MundoExtensionServer: {ex.Message}");
            }
        }

        private void Listen()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => ProcessRequestAsync(context));
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error accepting request: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Add CORS headers so extensions can call it from any origin (their chrome-extension:// origin)
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.Url?.AbsolutePath == "/api/tabs/active" && request.HttpMethod == "GET")
                {
                    await HandleGetActiveTabAsync(response);
                }
                else if (request.Url?.AbsolutePath == "/api/tabs/execute" && request.HttpMethod == "POST")
                {
                    await HandleExecuteScriptAsync(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing request: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        private async Task HandleGetActiveTabAsync(HttpListenerResponse response)
        {
            string url = "https://www.google.com";
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_webViewService.ActiveWebView?.CoreWebView2 != null)
                {
                    url = _webViewService.ActiveWebView.CoreWebView2.Source;
                }
            });

            var tabData = new
            {
                id = 1,
                url = url,
                active = true,
                currentWindow = true,
                title = "Current Page"
            };

            var json = JsonSerializer.Serialize(tabData);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        private async Task HandleExecuteScriptAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string requestBody;
            using (var reader = new StreamReader(request.InputStream))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            // details typically has: { code: "..." } or { files: ["..."] }
            // Since we receive it from polyfill, we expect standard injection parameters
            // For simplicity, let's assume the polyfill sends { script: "..." } or similar
            string scriptToExecute = "";
            try
            {
                using var doc = JsonDocument.Parse(requestBody);
                if (doc.RootElement.TryGetProperty("script", out var scriptElement))
                {
                    scriptToExecute = scriptElement.GetString() ?? "";
                }
            }
            catch
            {
                // Fallback
            }

            string resultJson = "null";

            if (!string.IsNullOrEmpty(scriptToExecute))
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webViewService.ActiveWebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            resultJson = await _webViewService.ActiveWebView.CoreWebView2.ExecuteScriptAsync(scriptToExecute);
                        }
                        catch { }
                    }
                });
            }

            var resultObj = new { result = resultJson };
            var responseJson = JsonSerializer.Serialize(resultObj);
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);

            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        public void Dispose()
        {
            _isRunning = false;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
        }
    }
}
