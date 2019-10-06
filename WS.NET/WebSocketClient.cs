using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace WS.NET
{
	/// <inheritdoc />
	/// <summary>
	/// A Basic Thread safe Client for handling WebSocket Connections.
	/// </summary>
	public class WebSocketClient : IDisposable
	{
		private const int _chunkSize = 12 * 1024;

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
		private Thread ReadThread { get; }
		
		/// <summary>
		/// The URI this WebSocketClient should connect.
		/// </summary>
		private Uri Uri { get; }
		
		/// <summary>
		/// If this WebSocketClient is disposed.
		/// </summary>
		private bool Disposed { get; set; }
		
		/// <summary>
		/// ActionBlock for Sending Messages
		/// </summary>
		private ActionBlock<WebSocketMessage> SendAction { get; }

		/// <summary>
		/// Creates a new instance of WebSocketClient.
		/// </summary>
		/// <param name="url">The URL this WebSocketClient should connect to.</param>
		/// <param name="options">Options of this WebSocketClient.</param>
		public WebSocketClient(string url, IWebSocketOptions options = null)
		{
			Uri = new Uri(url);
			SendAction = new ActionBlock<WebSocketMessage>(_send);
			ReadThread = new Thread(_read);

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
			SendAction = new ActionBlock<WebSocketMessage>(_send);
			ReadThread = new Thread(_read);
			
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
			ReadThread.Start();
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
			var message = new WebSocketMessage(data, type);
			SendAction.Post(message);
			return message.Task;
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
					var buffer = new ArraySegment<byte>(new byte[_chunkSize]);
					
					using (var ms = new MemoryStream())
					{
						WebSocketReceiveResult result;
						do
						{
							result = await ClientWebSocket.ReceiveAsync(buffer, CancellationToken);
							
							ms.Write(buffer.Array ?? throw new Exception(), buffer.Offset, result.Count);
						} while (!result.EndOfMessage);
						
						ms.Seek(0, SeekOrigin.Begin);

						switch (result.MessageType)
						{
							case WebSocketMessageType.Binary:
								Data?.Invoke(this, ms.GetBuffer());
								break;
							case WebSocketMessageType.Close:
								if (ClientWebSocket.CloseStatus != null)
									Close?.Invoke(this,
										new WebSocketCloseEventArgs((int) ClientWebSocket.CloseStatus, ClientWebSocket.CloseStatusDescription, true));
								return;
							case WebSocketMessageType.Text:
								using (var reader = new StreamReader(ms, Encoding.UTF8))
									Message?.Invoke(this, reader.ReadToEnd());
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}	
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
		
		private async Task _send(WebSocketMessage data)
		{
			try
			{
				await ClientWebSocket.SendAsync(data.Data, data.Type, true, CancellationToken);
				data.Complete();
			}
			catch (Exception e)
			{
				data.Complete(e);
			}
		}
	}
}