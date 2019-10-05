using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WS.NET
{
	/// <inheritdoc />
	/// <summary>
	/// A Basic Thread safe Client for handling WebSocket Connections.
	/// </summary>
	public class WebSocketClient : IDisposable
	{
		private const int _receiveChunkSize = 32 * 1024;

		/// <summary>
		/// Event emitted when the WebSocket Connection Closes.
		/// </summary>
		public event EventHandler<WebSocketCloseEventArgs> Close;
		
		/// <summary>
		/// Event emitted if the Connection encounters issues.
		/// </summary>
		public event EventHandler<Exception> Error;
		
		/// <summary>
		/// Event emitted if the WebSocket receive a Text Message.
		/// </summary>
		public event EventHandler<string> Message;
		
		/// <summary>
		/// Event emitted if the WebSocket receive Binary Data.
		/// </summary>
		public event EventHandler<byte[]> Data;
		
		/// <summary>
		/// Event emitted when the WebSocket Connection open.
		/// </summary>
		public event EventHandler Open;

		/// <summary>
		/// The State of the WebSocket Connection.
		/// </summary>
		public WebSocketState Status 
			=> ClientWebSocket.State;
		
		/// <summary>
		/// The CancellationToken of this WebSocketClient.
		/// </summary>
		private CancellationToken CancellationToken { get; } = new CancellationTokenSource().Token;
		
		/// <summary>
		/// The ClientWebSocket of this WebSocketClient.
		/// </summary>
		private ClientWebSocket ClientWebSocket { get; } = new ClientWebSocket();

		/// <summary>
		/// If the Read and Send Thread should be exited.
		/// </summary>
		private bool Exit { get; set; }
		
		/// <summary>
		/// The Read Thread of this WebSocketClient
		/// </summary>
		private Thread ReadThread { get; set; }

		/// <summary>
		/// The Send Thread of this WebSocketClient
		/// </summary>
		private Thread SendThread { get; set; }
		
		/// <summary>
		/// Queue holding Messages to Send
		/// </summary>
		private ConcurrentQueue<Sendable> Messages { get; } = new ConcurrentQueue<Sendable>();
		
		/// <summary>
		/// The URI this WebSocketClient should connect.
		/// </summary>
		private Uri Uri { get; }
		
		/// <summary>
		/// If this WebSocketClient is disposed.
		/// </summary>
		private bool Disposed { get; set; }
		
		/// <summary>
		/// Creates a new instance of WebSocketClient.
		/// </summary>
		/// <param name="url">The URL this WebSocketClient should connect to.</param>
		/// <param name="options">Options of this WebSocketClient.</param>
		public WebSocketClient(string url, IWebSocketOptions options = null)
		{
			Uri = new Uri(url);
			SetupThreads();

			if (options == null) return;
			if (options.Headers != null) foreach (var (item1, item2) in options.Headers) ClientWebSocket.Options.SetRequestHeader(item1, item2);
			if (options.Proxy != null) ClientWebSocket.Options.Proxy = options.Proxy;
		}

		/// <summary>
		/// Creates a new instance of WebSocketClient.
		/// </summary>
		/// <param name="uri">The Uri this WebSocketClient should connect to.</param>
		/// <param name="options">Options of this WebSocketClient.</param>
		public WebSocketClient(Uri uri, IWebSocketOptions options = null)
		{
			Uri = uri;
			SetupThreads();
			
			if (options == null) return;
			if (options.Headers != null) foreach (var (item1, item2) in options.Headers) ClientWebSocket.Options.SetRequestHeader(item1, item2);
			if (options.Proxy != null) ClientWebSocket.Options.Proxy = options.Proxy;
		}

		/// <summary>
		/// Connects this WebSocketClient to the WebSocket Server.
		/// </summary>
		/// <returns>Task.</returns>
		/// <exception cref="Exception">If the WebSocket Connection is already Connected or Connecting.</exception>
		public async Task ConnectAsync()
		{
			if (Status == WebSocketState.Open || Status == WebSocketState.Connecting) throw new Exception("Websocket already Connected or Connecting");
			await ClientWebSocket.ConnectAsync(Uri, CancellationToken);
			Exit = false;
			Open?.Invoke(this, EventArgs.Empty);
			StartThreads();
		}

		/// <summary>
		/// Queues Messages up to be Send to the WebSocket Server.
		/// </summary>
		/// <param name="data">The data to send to.</param>
		/// <param name="type">The type of the data.</param>
		/// <returns>Task</returns>
		/// <exception cref="InvalidOperationException">If The Connection is Closed.</exception>
		/// <exception cref="ExternalException">If the Connection closes before the Message can be send</exception>
		public Task SendAsync(byte[] data, WebSocketMessageType type = WebSocketMessageType.Text)
		{
			if (Status == WebSocketState.Closed) throw new InvalidOperationException("Cannot Send a Message on a Closed Connection");
			var tcs = new TaskCompletionSource<bool>();
			var sendable = new Sendable(data, type);
			sendable.Success += (sender, args) => tcs.SetResult(true);
			sendable.Error += (sender, error) => tcs.SetException(error);
			Messages.Enqueue(sendable);
			return tcs.Task;
		}

		/// <summary>
		/// Queues Messages up to be Send to the WebSocket Server.
		/// </summary>
		/// <param name="data">The data to send.</param>
		/// <returns>Task</returns>
		/// <exception cref="InvalidOperationException">If The Connection is Closed.</exception>
		public Task SendAsync(string data)
			=> SendAsync(Encoding.UTF8.GetBytes(data));

		/// <summary>
		/// Closes the Connection to the WebSocket Server.
		/// </summary>
		/// <param name="closeStatus">The Status this Connection should be closed with.</param>
		/// <param name="closeText">The Status text this Connection should be closed with.</param>
		/// <returns>Task</returns>
		/// <exception cref="InvalidOperationException">If the Connection is already Closed</exception>
		public async Task CloseAsync(WebSocketCloseStatus closeStatus, string closeText)
		{
			if (Status == WebSocketState.Closed) throw new InvalidOperationException("Websocket already Closed");
			Exit = true;
			await ClientWebSocket.CloseAsync(closeStatus, closeText, CancellationToken);
			Close?.Invoke(this, new WebSocketCloseEventArgs((int) closeStatus, closeText, false));
		}
		
		/// <inheritdoc />
		public void Dispose()
		{
			if (Disposed) return;
			ClientWebSocket?.Abort();
			ClientWebSocket?.Dispose();
			Disposed = true;
		}

		/// <summary>
		/// Continuously reading from the WebSocket Connection.
		/// </summary>
		private async void _read()
		{
			try
			{
				while (ClientWebSocket.State == WebSocketState.Open && !Exit)
				{
					var buffer = new byte[_receiveChunkSize];
					var message = "";
					var binary = new List<byte>();
					
					WebSocketReceiveResult result;
					do
					{

						do
						{
							result = await ClientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
								CancellationToken);
						} while (result == null);

						switch (result.MessageType)
						{
							case WebSocketMessageType.Close:
								if (ClientWebSocket.CloseStatus != null)
									Close?.Invoke(this,
										new WebSocketCloseEventArgs((int) ClientWebSocket.CloseStatus, ClientWebSocket.CloseStatusDescription, true));
								return;
							case WebSocketMessageType.Text:
								message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
								break;
							case WebSocketMessageType.Binary:
								var exactDataBuffer = new byte[result.Count];
								Array.Copy(buffer, 0, exactDataBuffer, 0, result.Count);
								binary.AddRange(exactDataBuffer);
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
					} while (!result.EndOfMessage);

					switch (result.MessageType)
					{
						case WebSocketMessageType.Binary:
							Data?.Invoke(this, binary.ToArray());
							break;
						case WebSocketMessageType.Close:
							break;
						case WebSocketMessageType.Text:
							Message?.Invoke(this, message);
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
					
				}
			}
			catch (Exception e)
			{
				Error?.Invoke(this, e);
			}
			finally
			{
				Dispose();
			}
		}

		/// <summary>
		/// Continuously sending if Messages are queued up.
		/// </summary>
		private async void _send()
		{
			while (ClientWebSocket.State != WebSocketState.Closed && !Exit)
			{
				while (Messages.TryDequeue(out var res))
				{
					try
					{
						await ClientWebSocket.SendAsync(res.Data, res.Type, true, CancellationToken);
						res.Emit();
					}
					catch (Exception e)
					{
						res.Emit(e);
					}
				}
			}

			foreach (var message in Messages) 
				message.Emit(new ExternalException("Websocket Connection closed"));

			Messages.Clear();
		}

		/// <summary>
		/// Creates the Read and Write Threads for this WebsocketClient
		/// </summary>
		private void SetupThreads()
		{
			ReadThread = new Thread(_read);
			SendThread = new Thread(_send);
		}

		/// <summary>
		/// Starts the Read and Write Threads for this WebsocketClient
		/// </summary>
		private void StartThreads()
		{
			ReadThread.Start();
			SendThread.Start();
		}
	}
}