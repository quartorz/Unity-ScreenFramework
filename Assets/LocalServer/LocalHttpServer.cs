using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalServer
{
	/// <summary>
	/// HttpListener ベースの極小ローカル HTTP サーバー。
	/// 空きポートを自動取得して起動する。停止すると即終了。
	/// </summary>
	public sealed class LocalHttpServer : IDisposable
	{
		public delegate void Handler(HttpListenerRequest req, HttpListenerResponse res);

		readonly HttpListener _listener = new HttpListener();
		readonly Dictionary<string, Handler> _routes = new Dictionary<string, Handler>();
		readonly CancellationTokenSource _cts = new CancellationTokenSource();
		Task _loop;

		public int Port { get; private set; }
		public string BaseUrl => $"http://127.0.0.1:{Port}";

		public void Map(string method, string path, Handler handler)
		{
			_routes[Key(method, path)] = handler;
		}

		public void Start()
		{
			Port = PickFreePort();
			_listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
			_listener.Start();
			_loop = Task.Run(LoopAsync);
			Debug.Log($"[LocalHttpServer] listening on {BaseUrl}");
		}

		async Task LoopAsync()
		{
			while (!_cts.IsCancellationRequested)
			{
				HttpListenerContext ctx;
				try
				{
					ctx = await _listener.GetContextAsync().ConfigureAwait(false);
				}
				catch (ObjectDisposedException) { return; }
				catch (HttpListenerException) { return; }

				_ = Task.Run(() => Handle(ctx));
			}
		}

		void Handle(HttpListenerContext ctx)
		{
			var req = ctx.Request;
			var res = ctx.Response;
			try
			{
				var key = Key(req.HttpMethod, req.Url.AbsolutePath);
				if (_routes.TryGetValue(key, out var h))
				{
					h(req, res);
				}
				else
				{
					res.StatusCode = 404;
					WriteText(res, "not found");
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[LocalHttpServer] handler error: {e}");
				try
				{
					res.StatusCode = 500;
					WriteText(res, e.Message);
				}
				catch { }
			}
			finally
			{
				try { res.OutputStream.Close(); } catch { }
			}
		}

		public void Dispose()
		{
			try { _cts.Cancel(); } catch { }
			try { _listener.Stop(); } catch { }
			try { _listener.Close(); } catch { }
			Debug.Log("[LocalHttpServer] stopped");
		}

		static string Key(string method, string path) => method.ToUpperInvariant() + " " + path;

		static int PickFreePort()
		{
			var l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			var port = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			return port;
		}

		public static void WriteJson(HttpListenerResponse res, string json)
		{
			var bytes = Encoding.UTF8.GetBytes(json);
			res.ContentType = "application/json; charset=utf-8";
			res.ContentLength64 = bytes.Length;
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}

		public static void WriteText(HttpListenerResponse res, string text)
		{
			var bytes = Encoding.UTF8.GetBytes(text);
			res.ContentType = "text/plain; charset=utf-8";
			res.ContentLength64 = bytes.Length;
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}

		public static string ReadBody(HttpListenerRequest req)
		{
			using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
			return sr.ReadToEnd();
		}
	}
}
