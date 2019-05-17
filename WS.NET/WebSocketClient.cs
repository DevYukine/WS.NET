using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WS.NET
{
	/// <inheritdoc />
	/// <summary>
	/// A Basic Client for handling WebSocket Connections.
	/// </summary>
	public class WebSocketClient : IDisposable
	{
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
		/// If the ReadThread should be exited.
		/// </summary>
		private bool Exit { get; set; }
		
		/// <summary>
		/// The ReadThread of this WebSocketClient
		/// </summary>
		private Thread ReadThread { get; set; }
		
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
			ReadThread = new Thread(_listen);
			ReadThread.Start();
		}

		/// <summary>
		/// Sends Data to the WebSocket Server.
		/// </summary>
		/// <param name="data">The data to send to.</param>
		/// <param name="type">The type of the data.</param>
		/// <returns>Task</returns>
		/// <exception cref="Exception">If The Connection is Closed.</exception>
		public async Task SendAsync(byte[] data, WebSocketMessageType type = WebSocketMessageType.Text)
		{
			if (Status == WebSocketState.Closed) throw new Exception("Cannot Send a Message to a Closed Connection");
			await ClientWebSocket.SendAsync(data, type, true, CancellationToken);
		}

		/// <summary>
		/// Sends Data to the WebSocket Server.
		/// </summary>
		/// <param name="data">The data to send to.</param>
		/// <returns>Task</returns>
		/// <exception cref="Exception">If The Connection is Closed.</exception>
		public Task SendAsync(string data)
			=> SendAsync(Encoding.UTF8.GetBytes(data));

		/// <summary>
		/// Closes the Connection to the WebSocket Server.
		/// </summary>
		/// <param name="closeStatus">The Status this Connection should be closed with.</param>
		/// <param name="closeText">The Status text this Connection should be closed with.</param>
		/// <returns>Task</returns>
		/// <exception cref="Exception">If the Connection is already Closed</exception>
		public async Task CloseAsync(WebSocketCloseStatus closeStatus, string closeText)
		{
			if (Status == WebSocketState.Closed) throw new Exception("Websocket already Closed");
			Exit = true;
			await ClientWebSocket.CloseAsync(closeStatus, closeText, CancellationToken);
			Close?.Invoke(this, new WebSocketCloseEventArgs((int) closeStatus, closeText, false));
		}

		/// <summary>
		/// Continuously reading from the WebSocket Connection.
		/// </summary>
		private async void _listen()
		{
			while (ClientWebSocket.State == WebSocketState.Open)
			{
				var message = "";
				var binary = new List<byte>();
				
				READ:
				if (Exit) return;
				
				var buffer = new byte[32 * 1024];
				WebSocketReceiveResult res;
				try
				{
					res = await ClientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken);
				}
				catch (Exception e)
				{
					Error?.Invoke(this, e);
					break;
				}
				
				if (res == null)
					goto READ;
				
				// ReSharper disable once SwitchStatementMissingSomeCases
				switch (res.MessageType)
				{
					case WebSocketMessageType.Close:
						if (ClientWebSocket.CloseStatus != null)
							Close?.Invoke(this,
								new WebSocketCloseEventArgs((int) ClientWebSocket.CloseStatus, ClientWebSocket.CloseStatusDescription, true));
						return;
					case WebSocketMessageType.Text:
					{
						if (!res.EndOfMessage)
						{
							message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
							goto READ;
						}
						message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
						
						Message?.Invoke(this, message);
						break;
					}
					case WebSocketMessageType.Binary:
						var exactDataBuffer = new byte[res.Count];
						Array.Copy(buffer, 0, exactDataBuffer, 0, res.Count);
						if (!res.EndOfMessage)
						{
							binary.AddRange(exactDataBuffer);
							goto READ;
						}

						binary.AddRange(exactDataBuffer);
						var binaryData = binary.ToArray();
						Data?.Invoke(this, binaryData);
						break;
				}
			}

			if (ClientWebSocket.State == WebSocketState.Closed || ClientWebSocket.State == WebSocketState.CloseReceived || Exit) return;
			if (ClientWebSocket.CloseStatus != null)
				Close?.Invoke(this,
					new WebSocketCloseEventArgs((int) ClientWebSocket.CloseStatus.Value, ClientWebSocket.CloseStatusDescription, true));
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Disposed) return;
			ClientWebSocket?.Abort();
			ClientWebSocket?.Dispose();
			Disposed = true;
		}
	}
}